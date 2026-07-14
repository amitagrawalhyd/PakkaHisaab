using Microsoft.AppCenter.Analytics;
using Microsoft.AppCenter.Crashes;

namespace PakkaHisaab.Maui.Services;

public interface ITelemetryService
{
    void Track(string eventName, params (string Key, string Value)[] properties);
    void TrackError(Exception ex, string context);
}

/// <summary>AppCenter Analytics + Crashes wrapper. No PII is ever attached to events.</summary>
public sealed class TelemetryService : ITelemetryService
{
    public void Track(string eventName, params (string Key, string Value)[] properties) =>
        Analytics.TrackEvent(eventName,
            properties.Length == 0 ? null : properties.ToDictionary(p => p.Key, p => p.Value));

    public void TrackError(Exception ex, string context) =>
        Crashes.TrackError(ex, new Dictionary<string, string> { ["context"] = context });
}
