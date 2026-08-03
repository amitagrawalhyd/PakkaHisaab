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
        if (!IsValidEmail(Email))
        {
            await Toast(Loc["Login_InvalidEmail"]);
            return;
        }
        if (IsRegisterMode)
        {
            if (string.IsNullOrWhiteSpace(DisplayName))
            {
                await Toast(Loc["Login_NameRequired"]);
                return;
            }
            if (Password.Length < 8)
            {
                await Toast(Loc["Login_PasswordTooShort"]);
                return;
            }
        }

        IsBusy = true;
        try
        {
            var outcome = IsRegisterMode
                ? await _auth.RegisterAsync(Email.Trim(), Password, DisplayName.Trim())
                : await _auth.LoginAsync(Email.Trim(), Password);

            if (outcome.Auth is not null)
                await Shell.Current.GoToAsync("//main/dashboard");
            else
                await Toast(ResolveError(outcome.ErrorCode, outcome.ErrorMessage));
        }
        finally
        {
            IsBusy = false;
        }
    }

    static bool IsValidEmail(string email)
    {
        var trimmed = email.Trim();
        return trimmed.Contains('@') && trimmed.IndexOf('@') > 0 && trimmed.IndexOf('@') < trimmed.LastIndexOf('.');
    }

    string ResolveError(string? code, string? serverMessage) => code switch
    {
        "EMAIL_TAKEN" => Loc["Login_EmailInUse"],
        "INVALID_INPUT" => Loc["Login_InvalidInput"],
        _ => string.IsNullOrWhiteSpace(serverMessage) ? Loc["Login_Failed"] : serverMessage
    };

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
