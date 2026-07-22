using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Plugin.Firebase.CloudMessaging;
using Plugin.LocalNotification;

namespace PakkaHisaab.Maui;

[Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true,
    LaunchMode = LaunchMode.SingleTop,
    ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation |
                           ConfigChanges.UiMode | ConfigChanges.ScreenLayout |
                           ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
// Android App Links: verified via web/.well-known/assetlinks.json hosted at pakkahisaab.app
// (see docs/DEPLOYMENT.md). No in-app route parsing yet, so any link under the domain just
// opens the app to its normal start screen — refine with DataPathPrefix once there's
// shareable in-app content to deep-link to.
[IntentFilter(new[] { Intent.ActionView },
    Categories = new[] { Intent.CategoryDefault, Intent.CategoryBrowsable },
    DataScheme = "https", DataHost = "pakkahisaab.app", AutoVerify = true)]
public class MainActivity : MauiAppCompatActivity
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        // Route notification taps/actions raised while the app was dead.
        LocalNotificationCenter.NotifyNotificationTapped(Intent);
        FirebaseCloudMessagingImplementation.OnNewIntent(Intent);
        Helpers.ServiceHelper.GetRequiredService<Services.INotificationService>()
            .WireActionHandlers();
    }

    protected override void OnNewIntent(Android.Content.Intent? intent)
    {
        base.OnNewIntent(intent);
        LocalNotificationCenter.NotifyNotificationTapped(intent);
        FirebaseCloudMessagingImplementation.OnNewIntent(intent);
    }
}
