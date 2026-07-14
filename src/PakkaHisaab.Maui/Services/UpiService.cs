using System.Globalization;
using PakkaHisaab.Shared.Dtos;

namespace PakkaHisaab.Maui.Services;

public record UpiApp(string Name, string AndroidPackage, string IosScheme);

public interface IUpiService
{
    IReadOnlyList<UpiApp> KnownApps { get; }
    /// <summary>Builds the NPCI deep link: upi://pay?pa=...&amp;pn=...&amp;am=...&amp;cu=INR&amp;tn=...</summary>
    string BuildPayLink(HelperDto helper, decimal amount, string note);
    /// <summary>Launches the OS chooser (or a specific app) via Launcher.Default.OpenAsync.</summary>
    Task<bool> LaunchAsync(HelperDto helper, decimal amount, string note, UpiApp? preferred = null);
}

public sealed class UpiService : IUpiService
{
    public IReadOnlyList<UpiApp> KnownApps { get; } = new List<UpiApp>
    {
        new(Name: "Google Pay", AndroidPackage: "com.google.android.apps.nbu.paisa.user", IosScheme: "gpay://upi/pay"),
        new(Name: "PhonePe", AndroidPackage: "com.phonepe.app", IosScheme: "phonepe://pay"),
        new(Name: "Paytm", AndroidPackage: "net.one97.paytm", IosScheme: "paytmmp://pay"),
        new(Name: "BHIM", AndroidPackage: "in.org.npci.upiapp", IosScheme: "bhim://upi/pay"),
        new(Name: "Amazon Pay", AndroidPackage: "in.amazon.mShop.android.shopping", IosScheme: "amazonpay://pay")
    };

    public string BuildPayLink(HelperDto helper, decimal amount, string note)
    {
        var pa = Uri.EscapeDataString(helper.UpiId ?? string.Empty);
        var pn = Uri.EscapeDataString(helper.Name);
        var am = amount.ToString("0.##", CultureInfo.InvariantCulture);
        var tn = Uri.EscapeDataString(note);
        var tr = Uri.EscapeDataString($"PH{DateTime.UtcNow:yyyyMMddHHmmss}");
        return $"upi://pay?pa={pa}&pn={pn}&am={am}&cu=INR&tn={tn}&tr={tr}";
    }

    public async Task<bool> LaunchAsync(HelperDto helper, decimal amount, string note, UpiApp? preferred = null)
    {
        if (string.IsNullOrWhiteSpace(helper.UpiId))
            return false;

        var link = BuildPayLink(helper, amount, note);

#if IOS
        // iOS has no generic upi:// handler chooser; try the preferred app scheme first.
        if (preferred is not null)
        {
            var appUri = new Uri($"{preferred.IosScheme}://upi/pay{link["upi://pay".Length..]}");
            if (await Launcher.Default.CanOpenAsync(appUri))
                return await Launcher.Default.OpenAsync(appUri);
        }
#endif
        // CanOpenAsync is unreliable for the upi:// scheme on Android: PackageManager resolves
        // multiple installed UPI apps (verified via dumpsys) but CanOpenAsync still reports false,
        // a known gap in MAUI Essentials' Launcher for custom schemes with several matching
        // handlers. Firing OpenAsync directly and treating a thrown/false result as "not found"
        // is the reliable path — it's what actually shows the native chooser.
        var uri = new Uri(link);
        try
        {
            return await Launcher.Default.OpenAsync(uri); // Android shows the native app chooser
        }
        catch
        {
            return false;
        }
    }
}
