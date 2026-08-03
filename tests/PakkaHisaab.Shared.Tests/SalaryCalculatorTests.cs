using PakkaHisaab.Shared.Domain;
using PakkaHisaab.Shared.Dtos;
using PakkaHisaab.Shared.Enums;

namespace PakkaHisaab.Shared.Tests;

public class SalaryCalculatorTests
{
    static HelperDto MonthlyHelper(decimal wage = 10000m, int allowedAbsences = 0,
        bool carryOver = false, int carriedOverLeaves = 0) => new()
    {
        Id = Guid.NewGuid(), Name = "Geeta", WageType = WageType.MonthlySalary,
        MonthlyWage = wage, MonthlyAllowedAbsences = allowedAbsences,
        CarryOverLeaveAllowed = carryOver, CarriedOverLeaves = carriedOverLeaves
    };

    static AttendanceDto Att(Guid helperId, string date, AttendanceStatus status, decimal units = 0) => new()
    {
        Id = Guid.NewGuid(), HelperId = helperId, Date = date, Status = status, UnitsDelivered = units
    };

    static LedgerEntryDto Ledger(Guid helperId, LedgerEntryType type, decimal amount, string period) => new()
    {
        Id = Guid.NewGuid(), HelperId = helperId, Type = type, Amount = amount, Period = period,
        Method = PaymentMethod.Cash
    };

    [Fact]
    public void FullyPresentMonth_PaysExactMonthlyWage()
    {
        var helper = MonthlyHelper(wage: 10000m);
        var breakdown = SalaryCalculator.Compute(helper, 2026, 8, [], []);

        Assert.Equal(10000m, breakdown.GrossWage);
        Assert.Equal(0m, breakdown.AbsenceDeduction);
        Assert.Equal(10000m, breakdown.FinalPayable);
    }

    [Fact]
    public void UnpaidAbsences_DeductAtDailyRate()
    {
        var helper = MonthlyHelper(wage: 3100m); // 100/day over 31-day month
        var attendance = new[] { Att(helper.Id, "2026-08-01", AttendanceStatus.Absent) };

        var breakdown = SalaryCalculator.Compute(helper, 2026, 8, attendance, []);

        Assert.Equal(1, breakdown.AbsentDays);
        Assert.Equal(100m, breakdown.AbsenceDeduction);
        Assert.Equal(3000m, breakdown.FinalPayable);
    }

    [Fact]
    public void AdvancesAndBonuses_NetAgainstFinalPayable()
    {
        var helper = MonthlyHelper(wage: 5000m);
        var ledger = new[]
        {
            Ledger(helper.Id, LedgerEntryType.Advance, 500m, "2026-08"),
            Ledger(helper.Id, LedgerEntryType.Bonus, 200m, "2026-08"),
        };

        var breakdown = SalaryCalculator.Compute(helper, 2026, 8, [], ledger);

        Assert.Equal(500m, breakdown.Advances);
        Assert.Equal(200m, breakdown.Bonuses);
        Assert.Equal(4700m, breakdown.FinalPayable); // 5000 - 500 + 200
    }

    [Fact]
    public void AlreadyPaid_ReducesFinalPayable_ForThatPeriodOnly()
    {
        var helper = MonthlyHelper(wage: 5000m);
        var ledger = new[] { Ledger(helper.Id, LedgerEntryType.SalaryPayment, 2000m, "2026-08") };

        var breakdown = SalaryCalculator.Compute(helper, 2026, 8, [], ledger);

        Assert.Equal(2000m, breakdown.AlreadyPaid);
        Assert.Equal(3000m, breakdown.FinalPayable);
    }

    [Fact]
    public void PerUnitHelper_PaysRatePerUnit_WithNoAbsenceDeduction()
    {
        var helper = new HelperDto
        {
            Id = Guid.NewGuid(), Name = "Raju", WageType = WageType.PerUnitDelivery,
            RatePerUnit = 60m, UnitLabel = "L"
        };
        var attendance = new[]
        {
            Att(helper.Id, "2026-08-01", AttendanceStatus.Present, units: 2m),
            Att(helper.Id, "2026-08-02", AttendanceStatus.Absent), // absence irrelevant for per-unit pay
        };

        var breakdown = SalaryCalculator.Compute(helper, 2026, 8, attendance, []);

        Assert.Equal(2m, breakdown.UnitsDelivered);
        Assert.Equal(120m, breakdown.GrossWage);
        Assert.Equal(0m, breakdown.AbsenceDeduction);
        Assert.Equal(120m, breakdown.FinalPayable);
    }

    [Fact]
    public void HalfDay_CountsAsHalfAnAbsence()
    {
        var helper = MonthlyHelper(wage: 3000m); // 100/day over 30-day month
        var attendance = new[] { Att(helper.Id, "2026-04-01", AttendanceStatus.HalfDay) };

        var breakdown = SalaryCalculator.Compute(helper, 2026, 4, attendance, []);

        Assert.Equal(0.5m, breakdown.HalfDays);
        Assert.Equal(50m, breakdown.AbsenceDeduction);
    }

    [Fact]
    public void LeaveCarryForward_RoundsHalfDayRemainderAwayFromZero()
    {
        // allowance 2, one half-day taken -> 1.5 leaves left; must round to 2, not truncate to 1.
        var helper = MonthlyHelper(wage: 3000m, allowedAbsences: 2, carryOver: true);
        var attendance = new[] { Att(helper.Id, "2026-04-01", AttendanceStatus.HalfDay) };

        var breakdown = SalaryCalculator.Compute(helper, 2026, 4, attendance, []);

        Assert.Equal(2, breakdown.LeavesToCarryForward);
    }

    [Fact]
    public void LeaveCarryForward_ComposesAcrossConsecutiveMonths()
    {
        // Month 1: allowance 2, no absences -> 2 leaves carried forward.
        var helper = MonthlyHelper(wage: 3000m, allowedAbsences: 2, carryOver: true);
        var month1 = SalaryCalculator.Compute(helper, 2026, 3, [], []);
        Assert.Equal(2, month1.LeavesToCarryForward);

        // Simulate DataService.MarkPaidAsync persisting the carried-forward balance.
        helper.CarriedOverLeaves = month1.LeavesToCarryForward;

        // Month 2: allowance 2 + 2 carried = 4; take 3 absences -> 1 leave left over.
        var attendance = new[]
        {
            Att(helper.Id, "2026-04-01", AttendanceStatus.Absent),
            Att(helper.Id, "2026-04-02", AttendanceStatus.Absent),
            Att(helper.Id, "2026-04-03", AttendanceStatus.Absent),
        };
        var month2 = SalaryCalculator.Compute(helper, 2026, 4, attendance, []);

        Assert.Equal(0m, month2.UnpaidAbsenceDays); // fully covered by allowance + carry-forward
        Assert.Equal(1, month2.LeavesToCarryForward);
    }
}
