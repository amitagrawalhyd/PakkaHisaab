using PakkaHisaab.Maui.ViewModels;

namespace PakkaHisaab.Maui.Views;

public partial class HelperFormPage : ContentPage
{
    readonly HelperFormViewModel _viewModel;

    public HelperFormPage(HelperFormViewModel viewModel)
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
