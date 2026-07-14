using Microsoft.AppCenter;
using Microsoft.AppCenter.Analytics;
using Microsoft.AppCenter.Crashes;
using PakkaHisaab.Maui.Helpers;
using PakkaHisaab.Maui.Services;

namespace PakkaHisaab.Maui;

public partial class App : Application
{
    readonly IDeviceIntegrityService _integrity;
    readonly ITelemetryService _telemetry;

    public App(IDeviceIntegrityService integrity, ITelemetryService telemetry)
    {
        InitializeComponent();
        _integrity = integrity;
        _telemetry = telemetry;

        // Telemetry + crash reporting (App Store compliance: disclosed in privacy policy).
        // NOTE: Visual Studio App Center was retired by Microsoft (Mar 2025). The SDK still
        // works offline-safe, but for a live free backend use Sentry (sentry.io free tier,
        // package "Sentry.Maui") or Firebase Crashlytics instead — see docs/FREE_DEPLOYMENT.md.
        // Guard: never start with placeholder secrets.
        if (!Constants.AppCenterAndroidSecret.Contains("YOUR-"))
            AppCenter.Start($"{Constants.AppCenterAndroidSecret};{Constants.AppCenterIosSecret}",
                typeof(Analytics), typeof(Crashes));
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        var window = new Window(new AppShell());
        window.Created += async (_, _) => await OnFirstShownAsync();
        return window;
    }

    async Task OnFirstShownAsync()
    {
        // Root/Jailbreak advisory — warn, don't block (store guidelines allow warning UX).
        if (_integrity.IsCompromised())
        {
            _telemetry.Track("integrity_warning_shown");
            var page = Windows.FirstOrDefault()?.Page;
            if (page is not null)
                await page.DisplayAlert(
                    LocalizationResourceManager.Instance["Security_RootedTitle"],
                    LocalizationResourceManager.Instance["Security_RootedMessage"],
                    LocalizationResourceManager.Instance["Common_OK"]);
        }
    }
}
