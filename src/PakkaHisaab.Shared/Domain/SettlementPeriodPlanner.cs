namespace PakkaHisaab.Shared.Domain;

/// <summary>
/// Pure period-enumeration logic behind "which past months might still be unpaid" — kept
/// separate from any storage so it can be unit-tested without a database.
/// </summary>
public static class SettlementPeriodPlanner
{
    /// <summary>
    /// Lists calendar months from the later of <paramref name="earliestActivityMonth"/> and
    /// <paramref name="maxLookbackMonths"/> months before the current month, up to but
    /// excluding the current month, oldest first. Returns nothing if there's no activity to
    /// look back from.
    /// </summary>
    public static IReadOnlyList<(int Year, int Month)> EnumerateBackPeriods(
        int currentYear, int currentMonth, DateTime? earliestActivityMonth, int maxLookbackMonths)
    {
        if (earliestActivityMonth is null) return [];

        var current = new DateTime(currentYear, currentMonth, 1);
        var lookbackFloor = current.AddMonths(-maxLookbackMonths);
        var start = earliestActivityMonth.Value > lookbackFloor ? earliestActivityMonth.Value : lookbackFloor;
        start = new DateTime(start.Year, start.Month, 1);

        var periods = new List<(int, int)>();
        for (var cursor = start; cursor < current; cursor = cursor.AddMonths(1))
            periods.Add((cursor.Year, cursor.Month));
        return periods;
    }
}
