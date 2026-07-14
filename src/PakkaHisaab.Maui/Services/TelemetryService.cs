using Microsoft.AppCenter.Analytics;
using Microsoft.AppCenter.Crashes;
#if ANDROID
using Plugin.Firebase.Analytics;
using Plugin.Firebase.Crashlytics;
#endif

namespace PakkaHisaab.Maui.Services;

public interface ITelemetryService
{
    void Track(string eventName, params (string Key, string Value)[] properties);
    void TrackError(Exception ex, string context);
}

/// <summary>AppCenter + Firebase Analytics/Crashlytics, fanned out to both. No PII is ever attached to events.</summary>
public sealed class TelemetryService : ITelemetryService
{
    public void Track(string eventName, params (string Key, string Value)[] properties)
    {
        Analytics.TrackEvent(eventName,
            properties.Length == 0 ? null : properties.ToDictionary(p => p.Key, p => p.Value));
#if ANDROID
        CrossFirebaseAnalytics.Current.LogEvent(eventName,
            properties.Select(p => (p.Key, (object)p.Value)).ToArray());
#endif
    }

    public void TrackError(Exception ex, string context)
    {
        Crashes.TrackError(ex, new Dictionary<string, string> { ["context"] = context });
#if ANDROID
        CrossFirebaseCrashlytics.Current.Log(context);
        CrossFirebaseCrashlytics.Current.RecordException(ex);
#endif
    }
}
