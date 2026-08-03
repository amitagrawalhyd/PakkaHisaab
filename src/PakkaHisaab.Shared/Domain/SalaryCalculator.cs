using PakkaHisaab.Shared.Dtos;
using PakkaHisaab.Shared.Enums;

namespace PakkaHisaab.Shared.Domain;

/// <summary>Result of a monthly settlement computation.</summary>
public record SettlementBreakdown(
    string Period,
    decimal GrossWage,
    int WorkingDaysInMonth,
    int AbsentDays,
    decimal HalfDays,
    int AllowedAbsences,
    int CarriedOverLeaves,
    decimal UnpaidAbsenceDays,
    decimal AbsenceDeduction,
    decimal Advances,
    decimal Bonuses,
    decimal OtherDeductions,
    decimal AlreadyPaid,
    decimal UnitsDelivered,
    decimal FinalPayable,
    int LeavesToCarryForward);

/// <summary>
/// Pure, dependency-free settlement engine shared by the MAUI client (offline instant totals),
/// the API (server-side verification) and the PDF reports — single source of truth (DRY).
///
/// Core formula: (Monthly Wage) − (Unpaid Absences × Daily Rate) − (Advances) = Final Payable.
/// </summary>
public static class SalaryCalculator
{
    /// <param name="helper">The helper whose wage model applies.</param>
    /// <param name="year">Settlement year.</param>
    /// <param name="month">Settlement month (1–12).</param>
    /// <param name="attendance">All attendance rows for the helper within the month.</param>
    /// <param name="ledger">All ledger rows for the helper within the settlement period.</param>
    public static SettlementBreakdown Compute(
        HelperDto helper,
        int year,
        int month,
        IReadOnlyCollection<AttendanceDto> attendance,
        IReadOnlyCollection<LedgerEntryDto> ledger)
    {
        var period = $"{year:D4}-{month:D2}";
        int daysInMonth = DateTime.DaysInMonth(year, month);

        var live = attendance.Where(a => !a.IsDeleted).ToList();
        int absentDays = live.Count(a => a.Status == AttendanceStatus.Absent);
        int halfDayCount = live.Count(a => a.Status == AttendanceStatus.HalfDay);
        decimal halfDays = halfDayCount * 0.5m;
        decimal unitsDelivered = live.Sum(a => a.UnitsDelivered);

        var liveLedger = ledger.Where(l => !l.IsDeleted).ToList();
        decimal advances = liveLedger.Where(l => l.Type == LedgerEntryType.Advance).Sum(l => l.Amount);
        decimal bonuses = liveLedger.Where(l => l.Type == LedgerEntryType.Bonus).Sum(l => l.Amount);
        decimal otherDeductions = liveLedger.Where(l => l.Type == LedgerEntryType.Deduction).Sum(l => l.Amount);
        decimal alreadyPaid = liveLedger.Where(l => l.Type == LedgerEntryType.SalaryPayment).Sum(l => l.Amount);

        decimal grossWage, absenceDeduction;
        decimal effectiveAbsences = absentDays + halfDays;
        int allowance = helper.MonthlyAllowedAbsences
                        + (helper.CarryOverLeaveAllowed ? helper.CarriedOverLeaves : 0);
        decimal unpaidAbsences = Math.Max(0, effectiveAbsences - allowance);
        int leavesToCarryForward = helper.CarryOverLeaveAllowed
            ? (int)Math.Max(0, allowance - effectiveAbsences)
            : 0;

        if (helper.WageType == WageType.PerUnitDelivery)
        {
            grossWage = decimal.Round(unitsDelivered * helper.RatePerUnit, 2);
            absenceDeduction = 0; // per-unit helpers are paid for what they delivered
        }
        else
        {
            grossWage = helper.MonthlyWage;
            decimal dailyRate = decimal.Round(helper.MonthlyWage / daysInMonth, 2);
            absenceDeduction = decimal.Round(unpaidAbsences * dailyRate, 2);
        }

        decimal finalPayable = decimal.Round(
            grossWage - absenceDeduction - advances - otherDeductions + bonuses - alreadyPaid, 2);

        return new SettlementBreakdown(
            Period: period,
            GrossWage: grossWage,
            WorkingDaysInMonth: daysInMonth,
            AbsentDays: absentDays,
            HalfDays: halfDays,
            AllowedAbsences: helper.MonthlyAllowedAbsences,
            CarriedOverLeaves: helper.CarryOverLeaveAllowed ? helper.CarriedOverLeaves : 0,
            UnpaidAbsenceDays: unpaidAbsences,
            AbsenceDeduction: absenceDeduction,
            Advances: advances,
            Bonuses: bonuses,
            OtherDeductions: otherDeductions,
            AlreadyPaid: alreadyPaid,
            UnitsDelivered: unitsDelivered,
            FinalPayable: finalPayable,
            LeavesToCarryForward: leavesToCarryForward);
    }
}
