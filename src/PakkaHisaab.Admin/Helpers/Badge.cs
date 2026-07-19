using PakkaHisaab.Shared.Enums;

namespace PakkaHisaab.Admin.Helpers;

/// <summary>Maps domain enums to (Text, Kind) pairs consumed by the &lt;ph-badge&gt; tag helper.</summary>
public static class Badge
{
    public static (string Text, string Kind) Attendance(AttendanceStatus s) => s switch
    {
        AttendanceStatus.Present => ("Present", "green"),
        AttendanceStatus.Absent => ("Absent", "red"),
        AttendanceStatus.HalfDay => ("Half-Day", "amber"),
        _ => (s.ToString(), "gray")
    };

    public static (string Text, string Kind) LedgerType(LedgerEntryType t) => t switch
    {
        LedgerEntryType.Advance => ("Advance", "amber"),
        LedgerEntryType.SalaryPayment => ("Salary Payment", "green"),
        LedgerEntryType.Bonus => ("Bonus", "teal"),
        LedgerEntryType.Deduction => ("Deduction", "red"),
        LedgerEntryType.DeliveryCharge => ("Delivery Charge", "gray"),
        _ => (t.ToString(), "gray")
    };

    public static (string Text, string Kind) Settlement(SettlementStatus s) => s switch
    {
        SettlementStatus.Paid => ("Paid", "green"),
        SettlementStatus.Pending => ("Pending", "amber"),
        _ => (s.ToString(), "gray")
    };

    public static (string Text, string Kind) Method(PaymentMethod m) => m switch
    {
        PaymentMethod.Upi => ("UPI", "teal"),
        PaymentMethod.Cash => ("Cash", "gray"),
        PaymentMethod.BankTransfer => ("Bank Transfer", "teal"),
        _ => (m.ToString(), "gray")
    };

    public static (string Text, string Kind) Bool(bool value, string trueText, string falseText) =>
        value ? (trueText, "green") : (falseText, "gray");
}
