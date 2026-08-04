using System.Globalization;
using CommunityToolkit.Maui.Media;
using PakkaHisaab.Maui.Helpers;
using PakkaHisaab.Shared.Domain;
using PakkaHisaab.Shared.Dtos;
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
/// offline; "Deducted 500 rupees from Geeta" becomes a ledger row in one breath. Helper names
/// are stored and displayed exactly as entered, in whatever language/script — never rewritten.
/// When the device's language isn't English, ListenAsync recognizes speech in that language/
/// script (see below), so both the recognized text and a throwaway English translation of each
/// helper's name are used purely to resolve intent/helper for this one command; the result is
/// reported back using each helper's real, unmodified name.
/// </summary>
public sealed class VoiceLedgerService : IVoiceLedgerService
{
    static readonly TimeSpan ListenTimeout = TimeSpan.FromSeconds(12);

    // Android's on-device speech recognizer needs a region-qualified BCP-47 tag (e.g. "or-IN")
    // to find an installed language pack for that language — LocalizationResourceManager's
    // SupportedLanguages deliberately uses bare codes like "or" for UI-string resx lookup
    // (where a region doesn't matter), but passing that same bare code straight to the
    // recognizer makes it silently fall back to the device's default locale instead of erroring
    // (confirmed via logcat: requesting "or" recognized speech as en_IN, not Odia, so nothing
    // downstream — translation, matching — ever had real Odia text to work with). This app is
    // India-focused, so every Indic language maps to its "-IN" region; the rest map to a
    // sensible default region for that language.
    static readonly Dictionary<string, string> SpeechRecognitionLocales = new(StringComparer.OrdinalIgnoreCase)
    {
        ["en"] = "en-IN", ["hi"] = "hi-IN", ["bn"] = "bn-IN", ["te"] = "te-IN", ["mr"] = "mr-IN",
        ["ta"] = "ta-IN", ["gu"] = "gu-IN", ["ur"] = "ur-IN", ["kn"] = "kn-IN", ["or"] = "or-IN",
        ["ml"] = "ml-IN", ["pa"] = "pa-IN", ["zh-Hans"] = "zh-CN", ["es"] = "es-ES",
        ["fr"] = "fr-FR", ["ar"] = "ar-SA", ["pt"] = "pt-BR", ["de"] = "de-DE",
        ["ja"] = "ja-JP", ["ru"] = "ru-RU", ["id"] = "id-ID", ["sw"] = "sw-KE", ["ko"] = "ko-KR"
    };

    static CultureInfo GetSpeechRecognitionCulture()
    {
        var code = CultureInfo.CurrentUICulture.Name;
        if (!SpeechRecognitionLocales.TryGetValue(code, out var mapped))
            return CultureInfo.CurrentUICulture;

        try { return new CultureInfo(mapped); }
        catch (CultureNotFoundException) { return CultureInfo.CurrentUICulture; }
    }

    readonly ISpeechToText _speech;
    readonly IDataService _data;
    readonly ITelemetryService _telemetry;
    readonly ITranslationService _translate;

    // In-memory only — never persisted, never synced, never shown. A repeat customer's helper
    // list rarely changes between voice commands in the same app session, so caching each
    // helper's English match-name here means only the FIRST voice command after adding/renaming
    // a non-English helper pays for a translation call; every command after that is instant.
    // Keyed by helper Id, invalidated automatically if the stored (unmodified) Name changes.
    readonly Dictionary<Guid, (string Name, string Translated)> _nameCache = new();

    public VoiceLedgerService(ISpeechToText speech, IDataService data, ITelemetryService telemetry, ITranslationService translate)
    {
        _speech = speech;
        _data = data;
        _telemetry = telemetry;
        _translate = translate;
    }

    async Task<string> GetCachedTranslatedNameAsync(HelperDto helper, CancellationToken ct)
    {
        if (_nameCache.TryGetValue(helper.Id, out var cached) && cached.Name == helper.Name)
            return cached.Translated;

        // Transliteration, not translation: a name is a sound, not a sentence with a meaning —
        // e.g. Hindi "आशा" must become "aasha" (phonetic), never "Hope" (what the word means).
        var translated = await _translate.TransliterateToLatinAsync(helper.Name, ct);
        _nameCache[helper.Id] = (helper.Name, translated);
        return translated;
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
                result = await _speech.ListenAsync(GetSpeechRecognitionCulture(), new Progress<string>(), timeoutCts.Token);
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

            var helpers = await _data.GetHelpersAsync();

            // ListenAsync recognized speech in whatever language the device UI is set to (see
            // above), and helper names are stored exactly as the user typed them — never
            // rewritten to another script (see HelperFormViewModel). The parser below only
            // understands English keywords/names, so both are translated to English here for
            // matching purposes only; the translated names are never persisted or shown, they
            // exist only long enough to resolve which helper was meant. Run in parallel — each
            // call is a no-op (no network round-trip) when its input is already ASCII/English.
            var translateCommand = _translate.TranslateToEnglishAsync(result.Text, ct);
            var translateNames = Task.WhenAll(helpers.Select(h => GetCachedTranslatedNameAsync(h, ct)));
            await Task.WhenAll(translateCommand, translateNames);
            var text = translateCommand.Result;
            var translatedNames = translateNames.Result;

            // First English translation wins if two helpers happen to translate to the same
            // name — matches the pre-existing "ambiguous name" handling in VoiceLedgerParser,
            // which already refuses to guess between equally-plausible helpers.
            var byTranslatedName = new Dictionary<string, HelperDto>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < helpers.Count; i++)
                byTranslatedName.TryAdd(translatedNames[i], helpers[i]);

            var command = VoiceLedgerParser.Parse(text, byTranslatedName.Keys.ToList());
            _telemetry.Track("voice_command", ("intent", command.Intent.ToString()));

            // Intent checked first: if the action itself wasn't understood, saying "couldn't
            // find that helper" would be misleading — neither half of the command landed.
            if (command.Intent == VoiceIntent.Unknown)
                return new VoiceLedgerResult(VoiceOutcome.IntentNotRecognized, BuildSuggestionMessage(command));

            HelperDto? helper = command.HelperNameHint is not null
                && byTranslatedName.TryGetValue(command.HelperNameHint, out var matched)
                    ? matched
                    : null;
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
