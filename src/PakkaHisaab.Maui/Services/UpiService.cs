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
        new("Google Pay", "com.google.android.apps.nbu.paisa.user", "gpay"),
        new("PhonePe", "com.phonepe.app", "phonepe"),
        new("Paytm", "net.one97.paytm", "paytmmp"),
        new("BHIM", "in.org.npci.upiapp", "bhim"),
        new("Amazon Pay", "in.amazon.mShop.android.shopping", "amazonpay")
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
        var uri = new Uri(link);
        if (await Launcher.Default.CanOpenAsync(uri))
            return await Launcher.Default.OpenAsync(uri); // Android shows the native app chooser
        return false;
    }
}
