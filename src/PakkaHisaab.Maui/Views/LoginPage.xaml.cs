using PakkaHisaab.Maui.ViewModels;

namespace PakkaHisaab.Maui.Views;

public partial class LoginPage : ContentPage
{
    public LoginPage(LoginViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
