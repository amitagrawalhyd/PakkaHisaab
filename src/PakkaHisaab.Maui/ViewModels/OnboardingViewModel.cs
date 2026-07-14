using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PakkaHisaab.Maui.Helpers;

namespace PakkaHisaab.Maui.ViewModels;

public record OnboardingSlide(string Icon, string TitleKey, string BodyKey);

/// <summary>3-slide intro carousel: 2-tap UI · 5 PM notifications · UPI settlements.</summary>
public partial class OnboardingViewModel : BaseViewModel
{
    public IReadOnlyList<OnboardingSlide> Slides { get; } = new[]
    {
        new OnboardingSlide(IconFont.CalendarMonth, "Onboard_Slide1_Title", "Onboard_Slide1_Body"),
        new OnboardingSlide(IconFont.CheckCircle,   "Onboard_Slide2_Title", "Onboard_Slide2_Body"),
        new OnboardingSlide(IconFont.Payments,      "Onboard_Slide3_Title", "Onboard_Slide3_Body")
    };

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLastSlide))]
    int position;

    public bool IsLastSlide => Position == Slides.Count - 1;

    [RelayCommand]
    void Next()
    {
        if (!IsLastSlide) Position++;
    }

    [RelayCommand]
    async Task FinishAsync()
    {
        Preferences.Default.Set(Constants.KeyOnboarded, true);
        await Shell.Current.GoToAsync("//login");
    }
}
