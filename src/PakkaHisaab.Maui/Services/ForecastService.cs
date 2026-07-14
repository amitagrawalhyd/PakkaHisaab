using PakkaHisaab.Maui.Helpers;
using PakkaHisaab.Shared.Domain;

namespace PakkaHisaab.Maui.Services;

public interface IForecastService
{
    /// <summary>Human-readable absence forecast (e.g., "Usually absent on Mondays · ~2/mo"), or null.</summary>
    Task<string?> GetForecastLabelAsync(Guid helperId);
}

/// <summary>Smart Leave Forecasting — thin adapter over the shared LeaveForecaster model.</summary>
public sealed class ForecastService : IForecastService
{
    readonly IDataService _data;
    readonly LocalizationResourceManager _loc = LocalizationResourceManager.Instance;

    public ForecastService(IDataService data) => _data = data;

    public async Task<string?> GetForecastLabelAsync(Guid helperId)
    {
        var history = await _data.GetAttendanceHistoryAsync(helperId);
        var forecast = LeaveForecaster.Forecast(history, DateTime.Now);
        if (forecast is null || forecast.Confidence < 0.4)
            return null;

        var dayName = _loc.CurrentCulture.DateTimeFormat.GetDayName(forecast.MostLikelyAbsenceDay);
        return _loc.Get("Forecast_Pattern", dayName, forecast.PredictedAbsencesNextMonth);
    }
}
