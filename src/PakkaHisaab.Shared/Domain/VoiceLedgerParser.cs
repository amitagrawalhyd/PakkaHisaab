using System.Globalization;
using System.Text.RegularExpressions;
using PakkaHisaab.Shared.Enums;

namespace PakkaHisaab.Shared.Domain;

public record VoiceCommand(
    VoiceIntent Intent,
    string? HelperNameHint,
    decimal Amount,
    decimal Units,
    AttendanceStatus? Attendance,
    string RawText);

public enum VoiceIntent
{
    Unknown = 0,
    LogAdvance = 1,
    LogDeduction = 2,
    MarkAttendance = 3,
    LogDelivery = 4,
    LogPayment = 5,
    LogBonus = 6,
    DeleteAdvance = 7,
    DeleteBonus = 8
}

/// <summary>
/// Rule-based NLP for the Voice-to-Ledger feature. Runs fully offline on-device;
/// the API exposes the same parser at /ai/parse for thin clients — one implementation, two hosts.
/// Handles English and romanized Hindi keywords, e.g.:
///   "Deducted 500 rupees from Geeta"        → LogDeduction  (500, Geeta)
///   "Gave Raju an advance of 200"           → LogAdvance    (200, Raju)
///   "Geeta was absent today"                → MarkAttendance(Absent, Geeta)
///   "Raju delivered 1.5 litres"             → LogDelivery   (1.5, Raju)
///   "Paid Geeta 4500 salary"                → LogPayment    (4500, Geeta)
///   "Delete Geeta's advance"                → DeleteAdvance (Geeta)
///   "Remove Raju's bonus"                   → DeleteBonus   (Raju)
/// </summary>
public static class VoiceLedgerParser
{
    static readonly Regex AmountRx = new(@"(?<amt>\d+(?:[.,]\d{1,2})?)\s*(?:rupees|rupee|rs\.?|₹)?",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    static readonly Regex UnitsRx = new(@"(?<units>\d+(?:[.,]\d{1,2})?)\s*(?:liters?|litres?|l\b|packets?|units?|kg)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    static readonly string[] AdvanceWords = { "advance", "udhaar", "udhar", "peshgi", "gave", "diya" };
    static readonly string[] DeductWords = { "deduct", "deducted", "cut", "kata", "kaata", "minus" };
    static readonly string[] AbsentWords = { "absent", "chhutti", "chutti", "leave", "didn't come", "did not come", "nahi aayi", "nahi aaya" };
    static readonly string[] PresentWords = { "present", "came", "aayi", "aaya" };
    static readonly string[] HalfDayWords = { "half day", "half-day", "aadha din" };
    static readonly string[] DeliveryWords = { "delivered", "delivery", "liter", "litre", "milk", "doodh" };
    static readonly string[] PaymentWords = { "paid", "salary", "settle", "settled", "tankha", "pagaar", "pagar" };
    static readonly string[] BonusWords = { "bonus", "diwali", "baksheesh", "inaam" };
    static readonly string[] DeleteWords = { "delete", "remove", "undo", "cancel", "hata", "hatao", "mita", "mitao" };

    public static VoiceCommand Parse(string text, IReadOnlyCollection<string> knownHelperNames)
    {
        var t = (text ?? string.Empty).Trim().ToLowerInvariant();

        string? nameHint = knownHelperNames
            .OrderByDescending(n => n.Length)
            .FirstOrDefault(n => t.Contains(n.ToLowerInvariant()));

        decimal amount = 0, units = 0;
        var am = AmountRx.Match(t);
        if (am.Success)
            amount = decimal.Parse(am.Groups["amt"].Value.Replace(',', '.'), CultureInfo.InvariantCulture);
        var um = UnitsRx.Match(t);
        if (um.Success)
            units = decimal.Parse(um.Groups["units"].Value.Replace(',', '.'), CultureInfo.InvariantCulture);

        bool Any(string[] words) => words.Any(t.Contains);

        // Delete checked first: "delete/remove ... bonus/advance" carries no amount, so it would
        // never satisfy the amount-gated Log* checks below anyway, but keeping it first avoids any
        // ambiguity with words (e.g. "gave", "diya") shared with the Advance/Bonus word lists.
        if (Any(DeleteWords) && Any(BonusWords))
            return new VoiceCommand(VoiceIntent.DeleteBonus, nameHint, 0, 0, null, text!);
        if (Any(DeleteWords) && Any(AdvanceWords))
            return new VoiceCommand(VoiceIntent.DeleteAdvance, nameHint, 0, 0, null, text!);

        if (Any(HalfDayWords))
            return new VoiceCommand(VoiceIntent.MarkAttendance, nameHint, 0, 0, AttendanceStatus.HalfDay, text!);
        if (Any(AbsentWords))
            return new VoiceCommand(VoiceIntent.MarkAttendance, nameHint, 0, 0, AttendanceStatus.Absent, text!);
        if (Any(DeliveryWords) && units > 0)
            return new VoiceCommand(VoiceIntent.LogDelivery, nameHint, 0, units, null, text!);
        if (Any(DeductWords) && amount > 0)
            return new VoiceCommand(VoiceIntent.LogDeduction, nameHint, amount, 0, null, text!);
        if (Any(BonusWords) && amount > 0)
            return new VoiceCommand(VoiceIntent.LogBonus, nameHint, amount, 0, null, text!);
        if (Any(PaymentWords) && amount > 0)
            return new VoiceCommand(VoiceIntent.LogPayment, nameHint, amount, 0, null, text!);
        if (Any(AdvanceWords) && amount > 0)
            return new VoiceCommand(VoiceIntent.LogAdvance, nameHint, amount, 0, null, text!);
        if (Any(PresentWords))
            return new VoiceCommand(VoiceIntent.MarkAttendance, nameHint, 0, 0, AttendanceStatus.Present, text!);

        return new VoiceCommand(VoiceIntent.Unknown, nameHint, amount, units, null, text!);
    }
}
