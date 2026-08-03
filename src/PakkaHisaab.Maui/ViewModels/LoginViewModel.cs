using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PakkaHisaab.Maui.Services;

namespace PakkaHisaab.Maui.ViewModels;

public partial class LoginViewModel : BaseViewModel
{
    readonly IAuthService _auth;

    public LoginViewModel(IAuthService auth) => _auth = auth;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSubmit))]
    string email = string.Empty;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSubmit))]
    string password = string.Empty;
    [ObservableProperty] string displayName = string.Empty;
    [ObservableProperty] bool isRegisterMode;

    /// <summary>Drives the Sign-in/Create-account button's IsEnabled — a disabled button with
    /// empty fields beats a tappable one that only complains after the fact (TestReport M-02).</summary>
    public bool CanSubmit => IsNotBusy && !string.IsNullOrWhiteSpace(Email) && !string.IsNullOrWhiteSpace(Password);

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
        OnPropertyChanged(nameof(CanSubmit));
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
            OnPropertyChanged(nameof(CanSubmit));
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
