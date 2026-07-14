using CommunityToolkit.Mvvm.ComponentModel;
using PakkaHisaab.Maui.Helpers;

namespace PakkaHisaab.Maui.ViewModels;

/// <summary>Common MVVM plumbing: busy state + live localization access for every screen.</summary>
public abstract partial class BaseViewModel : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotBusy))]
    bool isBusy;

    public bool IsNotBusy => !IsBusy;

    public LocalizationResourceManager Loc => LocalizationResourceManager.Instance;

    protected static Task Toast(string message) =>
        MainThread.InvokeOnMainThreadAsync(() =>
            CommunityToolkit.Maui.Alerts.Toast.Make(message).Show());
}
