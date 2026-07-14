using System.Globalization;
using CommunityToolkit.Maui.Media;
using PakkaHisaab.Shared.Domain;
using PakkaHisaab.Shared.Enums;

namespace PakkaHisaab.Maui.Services;

public interface IVoiceLedgerService
{
    /// <summary>Listens via the native speech recognizer, parses the utterance and applies it.
    /// Returns a human-readable confirmation, or null when nothing was understood.</summary>
    Task<string?> CaptureAndApplyAsync(CancellationToken ct = default);
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

    public async Task<string?> CaptureAndApplyAsync(CancellationToken ct = default)
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
                return $"{helper.Name}: {command.Attendance}";

            case VoiceIntent.LogDelivery:
                await _data.SetUnitsAsync(helper.Id, today, command.Units);
                return $"{helper.Name}: {command.Units:0.##} {helper.UnitLabel}";

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
                return $"{helper.Name}: {type} ₹{command.Amount:N0}";

            default:
                return null;
        }
    }
}
