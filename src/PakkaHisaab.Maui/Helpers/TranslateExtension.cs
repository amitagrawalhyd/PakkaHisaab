namespace PakkaHisaab.Maui.Helpers;

/// <summary>
/// XAML markup extension: Text="{helpers:Translate Dashboard_Title}".
/// Produces a live binding into <see cref="LocalizationResourceManager"/>'s indexer, so text
/// re-renders the moment the culture changes (backed by INotifyPropertyChanged).
/// </summary>
[ContentProperty(nameof(Key))]
public class TranslateExtension : IMarkupExtension<BindingBase>
{
    public string Key { get; set; } = string.Empty;
    public string? StringFormat { get; set; }

    public BindingBase ProvideValue(IServiceProvider serviceProvider) => new Binding
    {
        Mode = BindingMode.OneWay,
        Path = $"[{Key}]",
        Source = LocalizationResourceManager.Instance,
        StringFormat = StringFormat
    };

    object IMarkupExtension.ProvideValue(IServiceProvider serviceProvider) =>
        ProvideValue(serviceProvider);
}
