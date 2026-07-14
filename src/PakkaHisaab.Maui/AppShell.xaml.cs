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
        Routing.RegisterRoute("help", typeof(HelpPage));

        // Re-tapping the TabBar item you're already on (e.g. Home, while Calendar/Ledger/Settlement
        // is pushed on top of the Dashboard tab) reaches Shell's navigation pipeline, but since the
        // target ShellContent is already "current", Shell doesn't pop the section's pushed pages —
        // the tap silently does nothing. Detect that case and pop back to the tab root ourselves.
        Navigating += OnTabReselected;

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

    void OnTabReselected(object? sender, ShellNavigatingEventArgs e)
    {
        var section = CurrentItem?.CurrentItem;
        if (section is null || section.Navigation.NavigationStack.Count <= 1)
            return; // nothing pushed on the current tab — ordinary navigation, nothing to correct

        string target = e.Target.Location.OriginalString.TrimEnd('/');
        if (target.EndsWith("/" + section.Route, StringComparison.Ordinal))
            _ = section.Navigation.PopToRootAsync();
    }
}
