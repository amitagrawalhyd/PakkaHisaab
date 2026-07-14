using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PakkaHisaab.Maui.Services;

namespace PakkaHisaab.Maui.ViewModels;

public partial class LoginViewModel : BaseViewModel
{
    readonly IAuthService _auth;

    public LoginViewModel(IAuthService auth) => _auth = auth;

    [ObservableProperty] string email = string.Empty;
    [ObservableProperty] string password = string.Empty;
    [ObservableProperty] string displayName = string.Empty;
    [ObservableProperty] bool isRegisterMode;

    [RelayCommand]
    async Task SubmitAsync()
    {
        if (IsBusy) return;
        if (string.IsNullOrWhiteSpace(Email) || string.IsNullOrWhiteSpace(Password))
        {
            await Toast(Loc["Login_MissingFields"]);
            return;
        }

        IsBusy = true;
        try
        {
            bool ok = IsRegisterMode
                ? await _auth.RegisterAsync(Email.Trim(), Password, DisplayName.Trim())
                : await _auth.LoginAsync(Email.Trim(), Password);

            if (ok)
                await Shell.Current.GoToAsync("//main/dashboard");
            else
                await Toast(Loc["Login_Failed"]);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>“Try Demo” — the zero-login reviewer track. Instant, offline, isolated.</summary>
    [RelayCommand]
    async Task TryDemoAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            await _auth.StartDemoAsync();
            await Shell.Current.GoToAsync("//main/dashboard");
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    void ToggleMode() => IsRegisterMode = !IsRegisterMode;
}
