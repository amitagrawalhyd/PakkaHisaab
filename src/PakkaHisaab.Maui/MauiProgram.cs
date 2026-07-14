using CommunityToolkit.Maui;
using CommunityToolkit.Maui.Media;
using Microsoft.Extensions.Logging;
using PakkaHisaab.Maui.Data;
using PakkaHisaab.Maui.Helpers;
using PakkaHisaab.Maui.Services;
using PakkaHisaab.Maui.ViewModels;
using PakkaHisaab.Maui.Views;
using Plugin.LocalNotification;
using Shiny;
using Shiny.Jobs;

namespace PakkaHisaab.Maui;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .UseMauiCommunityToolkit()
            .UseShiny()                       // Shiny.NET host: background jobs survive app suspension
            .UseLocalNotification(cfg =>
            {
                cfg.AddCategory(new Plugin.LocalNotification.AndroidOption.NotificationCategory(
                    Constants.AttendanceCategory));
            })
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("Poppins-Regular.ttf", "PoppinsRegular");
                fonts.AddFont("Poppins-SemiBold.ttf", "PoppinsSemiBold");
                fonts.AddFont("Poppins-Bold.ttf", "PoppinsBold");
                // Material Symbols shipped as a font — no raster icons anywhere in the app.
                fonts.AddFont("MaterialSymbolsRounded.ttf", "MaterialIcons");
            });

#if DEBUG
        builder.Logging.AddDebug();
#endif

        RegisterServices(builder.Services);
        RegisterViewModels(builder.Services);
        RegisterViews(builder.Services);

        // Background sync job — runs even when the UI is not in the foreground.
        builder.Services.AddJob(typeof(SyncJob), requiredNetwork: InternetAccess.Any);

        var app = builder.Build();
        ServiceHelper.Initialize(app.Services);
        return app;
    }

    static void RegisterServices(IServiceCollection s)
    {
        s.AddSingleton<ISessionService, SessionService>();
        s.AddSingleton<ILocalDatabase, LocalDatabase>();
        s.AddSingleton<IDataService, DataService>();
        s.AddSingleton<ISyncEngine, SyncEngine>();
        s.AddSingleton<IApiClient, ApiClient>();
        s.AddSingleton<IAuthService, AuthService>();
        s.AddSingleton<INotificationService, NotificationService>();
        s.AddSingleton<IUpiService, UpiService>();
        s.AddSingleton<IPdfReportService, PdfReportService>();
        s.AddSingleton<IVoiceLedgerService, VoiceLedgerService>();
        s.AddSingleton<IForecastService, ForecastService>();
        s.AddSingleton<IDeviceIntegrityService, DeviceIntegrityService>();
        s.AddSingleton<ITelemetryService, TelemetryService>();
        s.AddSingleton(SpeechToText.Default);
        s.AddSingleton(LocalizationResourceManager.Instance);
        s.AddHttpClient();
    }

    static void RegisterViewModels(IServiceCollection s)
    {
        s.AddTransient<OnboardingViewModel>();
        s.AddTransient<LoginViewModel>();
        s.AddTransient<DashboardViewModel>();
        s.AddTransient<HelperFormViewModel>();
        s.AddTransient<CalendarViewModel>();
        s.AddTransient<LedgerViewModel>();
        s.AddTransient<SettlementViewModel>();
        s.AddTransient<ReportsViewModel>();
        s.AddTransient<SettingsViewModel>();
    }

    static void RegisterViews(IServiceCollection s)
    {
        s.AddTransient<OnboardingPage>();
        s.AddTransient<LoginPage>();
        s.AddTransient<DashboardPage>();
        s.AddTransient<HelperFormPage>();
        s.AddTransient<CalendarPage>();
        s.AddTransient<LedgerPage>();
        s.AddTransient<SettlementPage>();
        s.AddTransient<ReportsPage>();
        s.AddTransient<SettingsPage>();
        s.AddTransient<LegalPage>();
    }
}
