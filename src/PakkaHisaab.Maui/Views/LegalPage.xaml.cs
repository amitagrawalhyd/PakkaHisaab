using PakkaHisaab.Maui.Helpers;

namespace PakkaHisaab.Maui.Views;

[QueryProperty(nameof(Doc), "doc")]
public partial class LegalPage : ContentPage
{
    static readonly LocalizationResourceManager Loc = LocalizationResourceManager.Instance;

    public string? Doc { get; set; }

    public LegalPage() => InitializeComponent();

    protected override void OnAppearing()
    {
        base.OnAppearing();
        bool isTerms = Doc == "terms";
        Title = isTerms ? Loc["Settings_Terms"] : Loc["Settings_Privacy"];
        titleLabel.Text = Title;
        bodyLabel.Text = isTerms ? Loc["Legal_TermsBody"] : Loc["Legal_PrivacyBody"];
    }
}
