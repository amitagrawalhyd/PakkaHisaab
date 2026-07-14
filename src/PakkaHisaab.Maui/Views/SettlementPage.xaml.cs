using PakkaHisaab.Maui.ViewModels;

namespace PakkaHisaab.Maui.Views;

public partial class SettlementPage : ContentPage
{
    readonly SettlementViewModel _viewModel;

    public SettlementPage(SettlementViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.InitializeAsync();
    }
}
