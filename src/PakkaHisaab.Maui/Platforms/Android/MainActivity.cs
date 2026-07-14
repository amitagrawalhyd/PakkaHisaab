using Android.App;
using Android.Content.PM;
using Android.OS;
using Plugin.LocalNotification;

namespace PakkaHisaab.Maui;

[Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true,
    LaunchMode = LaunchMode.SingleTop,
    ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation |
                           ConfigChanges.UiMode | ConfigChanges.ScreenLayout |
                           ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        // Route notification taps/actions raised while the app was dead.
        LocalNotificationCenter.NotifyNotificationTapped(Intent);
        Helpers.ServiceHelper.GetRequiredService<Services.INotificationService>()
            .WireActionHandlers();
    }

    protected override void OnNewIntent(Android.Content.Intent? intent)
    {
        base.OnNewIntent(intent);
        LocalNotificationCenter.NotifyNotificationTapped(intent);
    }
}
