using PakkaHisaab.Shared.Dtos;
using PakkaHisaab.Shared.Enums;

namespace PakkaHisaab.Shared.Domain;

public record LeaveForecast(
    DayOfWeek MostLikelyAbsenceDay,
    double Confidence,               // 0..1
    double PredictedAbsencesNextMonth,
    IReadOnlyList<DateTime> LikelyDatesNextMonth);

/// <summary>
/// "Smart Leave Forecasting": a lightweight, fully-offline statistical model.
/// Uses per-weekday absence frequency plus a 3-month exponentially weighted trend —
/// deliberately simple so it runs instantly on-device with zero network calls.
/// </summary>
public static class LeaveForecaster
{
    public static LeaveForecast? Forecast(IReadOnlyCollection<AttendanceDto> history, DateTime nowLocal)
    {
        var absences = history
            .Where(a => !a.IsDeleted && a.Status == AttendanceStatus.Absent)
            .Select(a => DateTime.TryParse(a.Date, out var d) ? d : (DateTime?)null)
            .Where(d => d.HasValue)
            .Select(d => d!.Value)
            .Where(d => d >= nowLocal.AddMonths(-3))
            .ToList();

        if (absences.Count < 2) return null;

        // Weekday frequency, exponentially weighted (recent months count more).
        var weights = new Dictionary<DayOfWeek, double>();
        foreach (var d in absences)
        {
            double ageMonths = (nowLocal - d).TotalDays / 30.0;
            double w = Math.Pow(0.6, ageMonths); // decay
            weights[d.DayOfWeek] = weights.GetValueOrDefault(d.DayOfWeek) + w;
        }

        var top = weights.OrderByDescending(kv => kv.Value).First();
        double total = weights.Values.Sum();
        double confidence = total <= 0 ? 0 : top.Value / total;

        // Average absences per month over the observed window → projection.
        double monthsObserved = Math.Max(1.0,
            (absences.Max() - absences.Min()).TotalDays / 30.0);
        double perMonth = absences.Count / monthsObserved;

        // Project concrete dates: every occurrence of the top weekday next month.
        var firstOfNext = new DateTime(nowLocal.Year, nowLocal.Month, 1).AddMonths(1);
        var likely = new List<DateTime>();
        for (var d = firstOfNext; d.Month == firstOfNext.Month; d = d.AddDays(1))
            if (d.DayOfWeek == top.Key)
                likely.Add(d);

        return new LeaveForecast(top.Key, Math.Round(confidence, 2),
            Math.Round(perMonth, 1), likely.Take((int)Math.Ceiling(perMonth)).ToList());
    }
}
