using PakkaHisaab.Shared.Domain;

namespace PakkaHisaab.Shared.Tests;

public class SettlementPeriodPlannerTests
{
    [Fact]
    public void NoActivity_ReturnsEmpty()
    {
        var result = SettlementPeriodPlanner.EnumerateBackPeriods(2026, 8, null, 24);
        Assert.Empty(result);
    }

    [Fact]
    public void ActivityWithinLookback_EnumeratesFromEarliestActivityToJustBeforeCurrentMonth()
    {
        var earliest = new DateTime(2026, 5, 17); // mid-month timestamps should still floor to the 1st
        var result = SettlementPeriodPlanner.EnumerateBackPeriods(2026, 8, earliest, 24);

        Assert.Equal(new[] { (2026, 5), (2026, 6), (2026, 7) }, result);
    }

    [Fact]
    public void ActivityOlderThanLookback_ClampsToLookbackFloor()
    {
        var earliest = new DateTime(2020, 1, 1); // far older than the 3-month cap below
        var result = SettlementPeriodPlanner.EnumerateBackPeriods(2026, 8, earliest, 3);

        Assert.Equal(new[] { (2026, 5), (2026, 6), (2026, 7) }, result);
    }

    [Fact]
    public void CurrentMonthIsNeverIncluded()
    {
        var earliest = new DateTime(2026, 8, 1);
        var result = SettlementPeriodPlanner.EnumerateBackPeriods(2026, 8, earliest, 24);

        Assert.Empty(result);
    }

    [Fact]
    public void SpansYearRollover_WhenLookbackCrossesJanuary()
    {
        var earliest = new DateTime(2025, 11, 1);
        var result = SettlementPeriodPlanner.EnumerateBackPeriods(2026, 2, earliest, 24);

        Assert.Equal(new[] { (2025, 11), (2025, 12), (2026, 1) }, result);
    }
}
