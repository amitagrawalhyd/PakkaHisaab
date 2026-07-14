using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PakkaHisaab.Maui.Helpers;
using PakkaHisaab.Maui.Services;

namespace PakkaHisaab.Maui.ViewModels;

public record LanguageOption(string Code, string NativeName);

public partial class SettingsViewModel : BaseViewModel
{
    readonly IAuthService _auth;
    readonly ISessionService _session;

    public SettingsViewModel(IAuthService auth, ISessionService session)
    {
        _auth = auth;
        _session = session;
    }

    public IReadOnlyList<LanguageOption> Languages { get; } =
        LocalizationResourceManager.SupportedLanguages
            .Select(l => new LanguageOption(l.Code, l.NativeName)).ToList();

    [ObservableProperty] LanguageOption? selectedLanguage;
    [ObservableProperty] bool isDemo;

    public void Initialize()
    {
        IsDemo = _session.IsDemo;
        var current = LocalizationResourceManager.Instance.CurrentCulture.Name;
        SelectedLanguage = Languages.FirstOrDefault(l =>
            current.StartsWith(l.Code, StringComparison.OrdinalIgnoreCase)) ?? Languages[0];
    }

    /// <summary>Runtime language switch — every visible string re-binds instantly.</summary>
    partial void OnSelectedLanguageChanged(LanguageOption? value)
    {
        if (value is null) return;
        LocalizationResourceManager.Instance.SetCulture(new CultureInfo(value.Code));
    }

    [RelayCommand]
    Task OpenPrivacyAsync() =>
        Shell.Current.GoToAsync($"legal?url={Uri.EscapeDataString(Constants.PrivacyPolicyUrl)}");

    [RelayCommand]
    Task OpenTermsAsync() =>
        Shell.Current.GoToAsync($"legal?url={Uri.EscapeDataString(Constants.TermsUrl)}");

    [RelayCommand]
    async Task LogoutAsync()
    {
        await _auth.LogoutAsync();
        await Shell.Current.GoToAsync("//login");
    }

    /// <summary>App Store compliance: full account + data erasure, server and device.</summary>
    [RelayCommand]
    async Task DeleteAccountAsync()
    {
        var page = Shell.Current.CurrentPage;

        bool confirm = await page.DisplayAlert(
            Loc["Settings_DeleteTitle"], Loc["Settings_DeleteWarning"],
            Loc["Common_Delete"], Loc["Common_Cancel"]);
        if (!confirm) return;

        string? password = IsDemo ? string.Empty : await page.DisplayPromptAsync(
            Loc["Settings_DeleteTitle"], Loc["Settings_DeletePasswordPrompt"]);
        if (password is null) return;

        IsBusy = true;
        try
        {
            bool ok = await _auth.DeleteAccountAsync(password);
            if (!ok)
            {
                await Toast(Loc["Settings_DeleteFailed"]);
                return;
            }
            await Shell.Current.GoToAsync("//login");
        }
        finally
        {
            IsBusy = false;
        }
    }
}
