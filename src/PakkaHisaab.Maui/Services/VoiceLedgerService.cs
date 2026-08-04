using System.Globalization;
using CommunityToolkit.Maui.Media;
using PakkaHisaab.Maui.Helpers;
using PakkaHisaab.Shared.Domain;
using PakkaHisaab.Shared.Enums;

namespace PakkaHisaab.Maui.Services;

public enum VoiceOutcome
{
    Success,
    PermissionDenied,
    NoSpeechDetected,
    HelperNotRecognized,
    IntentNotRecognized,
    Error
}

/// <summary>Outcome of a voice command. <see cref="ShowOnCalendar"/> is true for attendance/delivery
/// commands — the recordings that live on the Calendar screen — so the caller can jump straight
/// there and show what was just logged, instead of leaving the user to go find it themselves.</summary>
public record VoiceLedgerResult(VoiceOutcome Outcome, string? Confirmation = null, Guid HelperId = default, bool ShowOnCalendar = false);

public interface IVoiceLedgerService
{
    /// <summary>Listens via the native speech recognizer, parses the utterance and applies it.
    /// Always returns a result — check <see cref="VoiceLedgerResult.Outcome"/> for why it failed.</summary>
    Task<VoiceLedgerResult> CaptureAndApplyAsync(CancellationToken ct = default);
}

/// <summary>
/// Voice-to-Ledger: MAUI native ISpeechToText → best-effort English translation → shared
/// rule-based parser → IDataService. The parser itself is local and the core flow works fully
/// offline; "Deducted 500 rupees from Geeta" becomes a ledger row in one breath. When the
/// device's language isn't English, ListenAsync recognizes speech in that language/script (see
/// below), so recognized text is translated to English first — the parser and the helper names
/// stored in the database (see HelperFormViewModel) are English/Latin-only by design.
/// </summary>
public sealed class VoiceLedgerService : IVoiceLedgerService
{
    static readonly TimeSpan ListenTimeout = TimeSpan.FromSeconds(12);

    readonly ISpeechToText _speech;
    readonly IDataService _data;
    readonly ITelemetryService _telemetry;
    readonly ITranslationService _translate;

    public VoiceLedgerService(ISpeechToText speech, IDataService data, ITelemetryService telemetry, ITranslationService translate)
    {
        _speech = speech;
        _data = data;
        _telemetry = telemetry;
        _translate = translate;
    }

    public async Task<VoiceLedgerResult> CaptureAndApplyAsync(CancellationToken ct = default)
    {
        try
        {
            var granted = await _speech.RequestPermissions(ct);
            if (!granted)
                return new VoiceLedgerResult(VoiceOutcome.PermissionDenied);

            // The recognizer has no built-in timeout — on a flaky mic or silent input it can hang
            // indefinitely, which reads to the user as the mic button "not responding".
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(ListenTimeout);

            SpeechToTextResult result;
            try
            {
                result = await _speech.ListenAsync(CultureInfo.CurrentUICulture, new Progress<string>(), timeoutCts.Token);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                return new VoiceLedgerResult(VoiceOutcome.NoSpeechDetected);
            }

            if (!result.IsSuccessful || string.IsNullOrWhiteSpace(result.Text))
            {
                if (result.Exception is not null)
                    _telemetry.TrackError(result.Exception, "voice_listen");
                return new VoiceLedgerResult(VoiceOutcome.NoSpeechDetected);
            }

            // ListenAsync recognized speech in whatever language the device UI is set to (see
            // above) — the parser below only understands English keywords and the English
            // helper names stored in the database, so translate before parsing. A no-op (and no
            // network call) when the recognized text is already ASCII/English.
            var text = await _translate.TranslateToEnglishAsync(result.Text, ct);

            var helpers = await _data.GetHelpersAsync();
            var command = VoiceLedgerParser.Parse(text, helpers.Select(h => h.Name).ToList());
            _telemetry.Track("voice_command", ("intent", command.Intent.ToString()));

            // Intent checked first: if the action itself wasn't understood, saying "couldn't
            // find that helper" would be misleading — neither half of the command landed.
            if (command.Intent == VoiceIntent.Unknown)
                return new VoiceLedgerResult(VoiceOutcome.IntentNotRecognized, BuildSuggestionMessage(command));

            var helper = helpers.FirstOrDefault(h =>
                h.Name.Equals(command.HelperNameHint, StringComparison.OrdinalIgnoreCase));
            if (helper is null && helpers.Count == 1)
                helper = helpers[0]; // only one helper — no ambiguity
            if (helper is null)
                return new VoiceLedgerResult(VoiceOutcome.HelperNotRecognized, BuildSuggestionMessage(command));

            var today = DateOnly.FromDateTime(DateTime.Today);
            var period = DateTime.Today.ToString("yyyy-MM");

            switch (command.Intent)
            {
                case VoiceIntent.MarkAttendance when command.Attendance.HasValue:
                    await _data.SetAttendanceAsync(helper.Id, today, command.Attendance.Value);
                    return new VoiceLedgerResult(VoiceOutcome.Success, $"{helper.Name}: {command.Attendance}", helper.Id, ShowOnCalendar: true);

                case VoiceIntent.LogDelivery:
                    await _data.SetUnitsAsync(helper.Id, today, command.Units);
                    return new VoiceLedgerResult(VoiceOutcome.Success, $"{helper.Name}: {command.Units:0.##} {helper.UnitLabel}", helper.Id, ShowOnCalendar: true);

                case VoiceIntent.LogAdvance:
                case VoiceIntent.LogDeduction:
                case VoiceIntent.LogBonus:
                case VoiceIntent.LogPayment:
                    var type = command.Intent switch
                    {
                        VoiceIntent.LogAdvance => LedgerEntryType.Advance,
                        VoiceIntent.LogDeduction => LedgerEntryType.Deduction,
                        VoiceIntent.LogBonus => LedgerEntryType.Bonus,
                        _ => LedgerEntryType.SalaryPayment
                    };
                    await _data.AddLedgerEntryAsync(new Shared.Dtos.LedgerEntryDto
                    {
                        HelperId = helper.Id, Type = type, Amount = command.Amount,
                        Method = PaymentMethod.Cash, Period = period,
                        Note = $"[voice] {command.RawText}"
                    });
                    return new VoiceLedgerResult(VoiceOutcome.Success, $"{helper.Name}: {type} ₹{command.Amount:N0}", helper.Id);

                case VoiceIntent.DeleteAdvance:
                case VoiceIntent.DeleteBonus:
                    var deleteType = command.Intent == VoiceIntent.DeleteAdvance
                        ? LedgerEntryType.Advance
                        : LedgerEntryType.Bonus;
                    // Voice has no way to point at a specific row, so "delete the bonus" means
                    // the most recent entry of that type still open in the current period.
                    var entries = await _data.GetLedgerAsync(helper.Id, period);
                    var toDelete = entries.FirstOrDefault(e => e.Type == deleteType);
                    if (toDelete is null)
                        return new VoiceLedgerResult(VoiceOutcome.Success, $"{helper.Name}: no {deleteType} entry to delete", helper.Id);

                    await _data.DeleteLedgerEntryAsync(toDelete.Id);
                    return new VoiceLedgerResult(VoiceOutcome.Success, $"{helper.Name}: {deleteType} ₹{toDelete.Amount:N0} deleted", helper.Id);

                default:
                    return new VoiceLedgerResult(VoiceOutcome.IntentNotRecognized);
            }
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            _telemetry.TrackError(ex, "voice_capture");
            return new VoiceLedgerResult(VoiceOutcome.Error);
        }
    }

    /// <summary>Turns VoiceLedgerParser's structured, UI-free suggestion into the specific,
    /// localized message the user actually sees — falls back to the generic keyed message only
    /// if the parser genuinely had nothing more specific to offer.</summary>
    static string BuildSuggestionMessage(VoiceCommand command)
    {
        var loc = LocalizationResourceManager.Instance;
        return command.Suggestion switch
        {
            VoiceSuggestionKind.GenericExample =>
                loc.Get("Voice_SuggestGeneric", command.SuggestionArgs![0]),
            VoiceSuggestionKind.KeywordFoundNoAmount =>
                loc.Get("Voice_SuggestNoAmount", command.SuggestionArgs![0], command.SuggestionArgs[1]),
            VoiceSuggestionKind.DeliveryNoUnits =>
                loc.Get("Voice_SuggestNoUnits", command.SuggestionArgs![0]),
            VoiceSuggestionKind.HelperNotFound =>
                loc.Get("Voice_SuggestHelperNotFound", string.Join(", ", command.SuggestionArgs!)),
            VoiceSuggestionKind.HelperAmbiguous =>
                loc.Get("Voice_SuggestHelperAmbiguous", command.SuggestionArgs![0], command.SuggestionArgs[1]),
            _ => command.Intent == VoiceIntent.Unknown ? loc["Voice_NotUnderstood"] : loc["Voice_HelperNotRecognized"]
        };
    }
}
