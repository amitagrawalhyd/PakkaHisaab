using System.Globalization;
using CommunityToolkit.Maui.Media;
using PakkaHisaab.Shared.Domain;
using PakkaHisaab.Shared.Enums;

namespace PakkaHisaab.Maui.Services;

/// <summary>Outcome of a voice command. <see cref="ShowOnCalendar"/> is true for attendance/delivery
/// commands — the recordings that live on the Calendar screen — so the caller can jump straight
/// there and show what was just logged, instead of leaving the user to go find it themselves.</summary>
public record VoiceLedgerResult(string Confirmation, Guid HelperId, bool ShowOnCalendar);

public interface IVoiceLedgerService
{
    /// <summary>Listens via the native speech recognizer, parses the utterance and applies it.
    /// Returns null when nothing was understood or no matching helper/intent was found.</summary>
    Task<VoiceLedgerResult?> CaptureAndApplyAsync(CancellationToken ct = default);
}

/// <summary>
/// Voice-to-Ledger: MAUI native ISpeechToText → shared rule-based parser → IDataService.
/// Works completely offline (the parser is local); "Deducted 500 rupees from Geeta" becomes
/// a ledger row in one breath.
/// </summary>
public sealed class VoiceLedgerService : IVoiceLedgerService
{
    readonly ISpeechToText _speech;
    readonly IDataService _data;
    readonly ITelemetryService _telemetry;

    public VoiceLedgerService(ISpeechToText speech, IDataService data, ITelemetryService telemetry)
    {
        _speech = speech;
        _data = data;
        _telemetry = telemetry;
    }

    public async Task<VoiceLedgerResult?> CaptureAndApplyAsync(CancellationToken ct = default)
    {
        var granted = await _speech.RequestPermissions(ct);
        if (!granted) return null;

        var result = await _speech.ListenAsync(
            CultureInfo.CurrentUICulture,
            new Progress<string>(), ct);

        if (!result.IsSuccessful || string.IsNullOrWhiteSpace(result.Text))
            return null;

        var helpers = await _data.GetHelpersAsync();
        var command = VoiceLedgerParser.Parse(result.Text, helpers.Select(h => h.Name).ToList());
        _telemetry.Track("voice_command", ("intent", command.Intent.ToString()));

        var helper = helpers.FirstOrDefault(h =>
            h.Name.Equals(command.HelperNameHint, StringComparison.OrdinalIgnoreCase));
        if (helper is null && helpers.Count == 1)
            helper = helpers[0]; // only one helper — no ambiguity
        if (helper is null || command.Intent == VoiceIntent.Unknown)
            return null;

        var today = DateOnly.FromDateTime(DateTime.Today);
        var period = DateTime.Today.ToString("yyyy-MM");

        switch (command.Intent)
        {
            case VoiceIntent.MarkAttendance when command.Attendance.HasValue:
                await _data.SetAttendanceAsync(helper.Id, today, command.Attendance.Value);
                return new VoiceLedgerResult($"{helper.Name}: {command.Attendance}", helper.Id, ShowOnCalendar: true);

            case VoiceIntent.LogDelivery:
                await _data.SetUnitsAsync(helper.Id, today, command.Units);
                return new VoiceLedgerResult($"{helper.Name}: {command.Units:0.##} {helper.UnitLabel}", helper.Id, ShowOnCalendar: true);

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
                return new VoiceLedgerResult($"{helper.Name}: {type} ₹{command.Amount:N0}", helper.Id, ShowOnCalendar: false);

            default:
                return null;
        }
    }
}
