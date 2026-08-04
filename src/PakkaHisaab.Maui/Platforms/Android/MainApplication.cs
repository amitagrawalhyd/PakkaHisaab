using Android.App;
using Android.Runtime;
using Microsoft.Extensions.DependencyInjection;
using PakkaHisaab.Maui.Services;

namespace PakkaHisaab.Maui;

[Application]
public class MainApplication : MauiApplication
{
    public MainApplication(IntPtr handle, JniHandleOwnership ownership)
        : base(handle, ownership)
    {
    }

    protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();

    public override void OnCreate()
    {
        base.OnCreate();

        // Last-line-of-defense platform safety net: without this, an exception raised on a
        // background thread (e.g. the sync engine's fire-and-forget push/pull kicked off right
        // after a cash/UPI payment is recorded) reaches Android's Java-level uncaught-exception
        // handler and kills the whole process — not just the failing background job. The .NET
        // side (SyncEngine's own try/catch, App.xaml.cs's UnobservedTaskException handler)
        // should already prevent this, but this catches anything that still slips through the
        // Android/Mono bridge instead of letting it take the app down mid-payment.
        AndroidEnvironment.UnhandledExceptionRaiser += (_, args) =>
        {
            IPlatformApplication.Current?.Services.GetService<ITelemetryService>()
                ?.TrackError(args.Exception, "android_unhandled_background");
            args.Handled = true;
        };
    }
}
