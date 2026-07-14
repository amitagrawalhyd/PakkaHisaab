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
#if ANDROID
using Microsoft.Maui.LifecycleEvents;
using Plugin.Firebase.Analytics;
using Plugin.Firebase.Auth;
using Plugin.Firebase.CloudMessaging;
using Plugin.Firebase.Core.Platforms.Android;
using Plugin.Firebase.Crashlytics;
#endif

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
                cfg.AddCategory(new Plugin.LocalNotification.NotificationCategory(
                    Plugin.LocalNotification.NotificationCategoryType.Reminder));
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
#if ANDROID
        ConfigureFirebase(builder);
#endif

        // Background sync job — runs even when the UI is not in the foreground.
        builder.Services.AddJob(typeof(SyncJob), requiredNetwork: InternetAccess.Any);

        var app = builder.Build();
        ServiceHelper.Initialize(app.Services);
        return app;
    }

#if ANDROID
    // Firebase: Android-only for now — google-services.json is configured for the Android package only.
    // Auth is initialized but not otherwise wired up; login still goes through IAuthService/the backend API.
    static void ConfigureFirebase(MauiAppBuilder builder)
    {
        builder.ConfigureLifecycleEvents(events => events.AddAndroid(android => android.OnCreate((activity, _) =>
        {
            // MAUI is single-Activity, so the same instance can always answer the locator.
            CrossFirebase.Initialize(activity, () => activity);
            FirebaseAnalyticsImplementation.Initialize(activity);
            CrossFirebaseCrashlytics.Current.SetCrashlyticsCollectionEnabled(true);

            // Notification channel FCM shows local notifications through when the app is backgrounded.
            var channelId = $"{activity.PackageName}.general";
            var notificationManager = (Android.App.NotificationManager)activity
                .GetSystemService(Android.Content.Context.NotificationService)!;
            notificationManager.CreateNotificationChannel(
                new Android.App.NotificationChannel(channelId, "General", Android.App.NotificationImportance.Default));
            FirebaseCloudMessagingImplementation.ChannelId = channelId;
        })));

        builder.Services.AddSingleton(_ => CrossFirebaseAuth.Current);
        builder.Services.AddSingleton(_ => CrossFirebaseCloudMessaging.Current);
    }
#endif

    static void RegisterServices(IServiceCollection s)
    {
        s.AddSingleton<ISessionService, SessionService>();
        s.AddSingleton<ILocalDatabase, LocalDatabase>();
        s.AddSingleton<IDataService, DataService>();
        s.AddSingleton<ISyncEngine, SyncEngine>();
        s.AddSingleton<IApiClient, ApiClient>();
        s.AddSingleton<IAuthService, AuthService>();
        s.AddSingleton<Services.INotificationService, NotificationService>();
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
