using System.ComponentModel;
using System.Globalization;
using System.Resources;
using PakkaHisaab.Maui.Resources.Strings;

namespace PakkaHisaab.Maui.Helpers;

/// <summary>
/// Offline-first runtime localization. Wraps the .resx <see cref="ResourceManager"/> and raises
/// <see cref="INotifyPropertyChanged"/> with an indexer change so every {helpers:Translate} /
/// [LocalizationResourceManager[key]] binding refreshes INSTANTLY when the user switches language —
/// no restart, no network.
/// </summary>
public sealed class LocalizationResourceManager : INotifyPropertyChanged
{
    public static LocalizationResourceManager Instance { get; } = new();

    readonly ResourceManager _resources = AppStrings.ResourceManager;

    LocalizationResourceManager()
    {
        var saved = Preferences.Default.Get(Constants.KeyLanguage, string.Empty);
        if (!string.IsNullOrEmpty(saved))
            SetCulture(new CultureInfo(saved), persist: false);
    }

    public CultureInfo CurrentCulture { get; private set; } = CultureInfo.CurrentUICulture;

    /// <summary>Indexer used by XAML bindings: Text="{Binding [Dashboard_Title], Source={x:Static ...Instance}}".</summary>
    public string this[string key] =>
        _resources.GetString(key, CurrentCulture) ?? $"!{key}!";

    public string Get(string key) => this[key];

    public string Get(string key, params object[] args) =>
        string.Format(CurrentCulture, this[key], args);

    public void SetCulture(CultureInfo culture, bool persist = true)
    {
        CurrentCulture = culture;
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;

        if (persist)
            Preferences.Default.Set(Constants.KeyLanguage, culture.Name);

        // "Item" is the WPF/MAUI convention for "every indexer value changed".
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item"));

        // Flip layout direction for RTL languages (Arabic, Urdu).
        if (Application.Current is not null)
            Application.Current.Windows.FirstOrDefault()?.Page?.Dispatcher.Dispatch(() =>
            {
                var rtl = culture.TextInfo.IsRightToLeft;
                if (Application.Current.Windows.FirstOrDefault()?.Page is Page p)
                    p.FlowDirection = rtl ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
            });
    }

    /// <summary>All languages shipped with the app (Tier 1 + Tier 2/3 regional).</summary>
    public static IReadOnlyList<(string Code, string NativeName)> SupportedLanguages { get; } = new[]
    {
        ("en", "English"), ("hi", "हिन्दी"), ("zh-Hans", "中文"), ("es", "Español"),
        ("fr", "Français"), ("ar", "العربية"), ("pt", "Português"), ("de", "Deutsch"),
        ("ja", "日本語"), ("ru", "Русский"), ("id", "Bahasa Indonesia"), ("bn", "বাংলা"),
        ("sw", "Kiswahili"), ("ko", "한국어"), ("te", "తెలుగు"), ("mr", "मराठी"),
        ("ta", "தமிழ்"), ("gu", "ગુજરાતી"), ("ur", "اردو"), ("kn", "ಕನ್ನಡ"),
        ("or", "ଓଡ଼ିଆ"), ("ml", "മലയാളം"), ("pa", "ਪੰਜਾਬੀ")
    };

    public event PropertyChangedEventHandler? PropertyChanged;
}
