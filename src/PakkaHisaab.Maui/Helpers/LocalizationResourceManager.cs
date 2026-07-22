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

    /// <summary>Locale codes whose text is fully covered by the bundled Poppins font (Latin script
    /// plus its usual diacritics). Every other supported language falls back to the platform default
    /// font at runtime — see <see cref="ApplyFontResources"/>.</summary>
    static readonly HashSet<string> LatinScriptLanguages = new(StringComparer.OrdinalIgnoreCase)
    {
        "en", "es", "fr", "pt", "de", "id", "sw"
    };

    LocalizationResourceManager()
    {
        var saved = Preferences.Default.Get(Constants.KeyLanguage, string.Empty);
        var culture = string.IsNullOrEmpty(saved) ? CultureInfo.CurrentUICulture : new CultureInfo(saved);
        SetCulture(culture, persist: false);
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

        if (Application.Current is not null)
        {
            // Resource-dictionary swap only needs the Application to exist (no live Page/Window
            // required), so it applies correctly even on a cold start before the first page renders.
            ApplyFontResources(culture);

            // Flip layout direction for RTL languages (Arabic, Urdu).
            Application.Current.Windows.FirstOrDefault()?.Page?.Dispatcher.Dispatch(() =>
            {
                var rtl = culture.TextInfo.IsRightToLeft;
                if (Application.Current.Windows.FirstOrDefault()?.Page is Page p)
                    p.FlowDirection = rtl ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
            });
        }
    }

    /// <summary>
    /// The bundled Poppins font only ships Latin-script glyphs. Android's text renderer silently
    /// substitutes a system fallback font per-glyph when one is missing from the requested typeface,
    /// but iOS does not do this for a custom AddFont-registered font in MAUI — missing glyphs render
    /// as blank/tofu boxes instead. To keep both platforms looking correct, every Label/Button/Entry
    /// in Styles.xaml (plus a handful of inline uses) binds FontFamily via DynamicResource to
    /// "FontRegular"/"FontSemiBold"/"FontBold"; here we point those at Poppins for Latin-script
    /// languages and clear them (platform default font, which has full script coverage + its own
    /// fallback chain on both OSes) for everything else.
    /// </summary>
    static void ApplyFontResources(CultureInfo culture)
    {
        var resources = Application.Current?.Resources;
        if (resources is null) return;

        bool useCustomFont = LatinScriptLanguages.Contains(culture.Name)
            || LatinScriptLanguages.Contains(culture.TwoLetterISOLanguageName);

        resources["FontRegular"] = useCustomFont ? "PoppinsRegular" : null;
        resources["FontSemiBold"] = useCustomFont ? "PoppinsSemiBold" : null;
        resources["FontBold"] = useCustomFont ? "PoppinsBold" : null;
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
