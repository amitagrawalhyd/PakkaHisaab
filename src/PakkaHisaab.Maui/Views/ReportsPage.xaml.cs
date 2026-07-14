using PakkaHisaab.Maui.ViewModels;

namespace PakkaHisaab.Maui.Views;

public partial class ReportsPage : ContentPage
{
    readonly ReportsViewModel _viewModel;

    public ReportsPage(ReportsViewModel viewModel)
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
