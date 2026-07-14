using PakkaHisaab.Maui.Helpers;
using PakkaHisaab.Maui.Views;

namespace PakkaHisaab.Maui;

public partial class AppShell : Shell
{
    public AppShell()
    {
        InitializeComponent();

        // Detail routes (pushed, not tabs)
        Routing.RegisterRoute("helperform", typeof(HelperFormPage));
        Routing.RegisterRoute("calendar", typeof(CalendarPage));
        Routing.RegisterRoute("ledger", typeof(LedgerPage));
        Routing.RegisterRoute("settlement", typeof(SettlementPage));
        Routing.RegisterRoute("legal", typeof(LegalPage));

        Loaded += async (_, _) =>
        {
            bool onboarded = Preferences.Default.Get(Constants.KeyOnboarded, false);
            bool hasSession =
                Preferences.Default.Get(Constants.KeyIsDemo, false) ||
                await SecureStorage.Default.GetAsync(Constants.KeyAccessToken) is not null;

            if (!onboarded)
                await GoToAsync("//onboarding");
            else if (!hasSession)
                await GoToAsync("//login");
            else
                await GoToAsync("//main/dashboard");
        };
    }
}
