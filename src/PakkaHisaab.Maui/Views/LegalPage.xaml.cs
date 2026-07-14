namespace PakkaHisaab.Maui.Views;

[QueryProperty(nameof(Url), "url")]
public partial class LegalPage : ContentPage
{
    public string? Url { get; set; }

    public LegalPage() => InitializeComponent();

    protected override void OnAppearing()
    {
        base.OnAppearing();
        if (!string.IsNullOrEmpty(Url))
            webView.Source = Uri.UnescapeDataString(Url);
    }
}
