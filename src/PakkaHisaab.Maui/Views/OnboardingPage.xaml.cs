using PakkaHisaab.Maui.ViewModels;

namespace PakkaHisaab.Maui.Views;

public partial class OnboardingPage : ContentPage
{
    public OnboardingPage(OnboardingViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
