using Foundation;

namespace PakkaHisaab.Maui;

[Register("AppDelegate")]
public class AppDelegate : MauiUIApplicationDelegate
{
    protected override MauiApp CreateMauiApp()
    {
        var app = MauiProgram.CreateMauiApp();
        Helpers.ServiceHelper.GetRequiredService<Services.INotificationService>()
            .WireActionHandlers();
        return app;
    }
}
