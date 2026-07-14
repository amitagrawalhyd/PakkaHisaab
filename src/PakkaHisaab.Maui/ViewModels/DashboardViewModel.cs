using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PakkaHisaab.Maui.Helpers;
using PakkaHisaab.Maui.Services;
using PakkaHisaab.Shared.Dtos;
using PakkaHisaab.Shared.Enums;

namespace PakkaHisaab.Maui.ViewModels;

/// <summary>Row model for a helper card on the dashboard.</summary>
public partial class HelperCardViewModel : ObservableObject
{
    public HelperDto Helper { get; init; } = null!;
    public string Name => Helper.Name;
    public string CategoryIcon => Helper.Category switch
    {
        HelperCategory.MilkMan => IconFont.WaterDrop,
        HelperCategory.Driver => IconFont.Person,
        _ => IconFont.Person
    };
    public string CategoryKey => $"Category_{Helper.Category}";

    [ObservableProperty] string payableLabel = string.Empty;
    [ObservableProperty] string todayStatusIcon = IconFont.CheckCircle;
    [ObservableProperty] Color todayStatusColor = Colors.Gray;
    [ObservableProperty] string? forecastLabel;
}

public partial class DashboardViewModel : BaseViewModel
{
    readonly IDataService _data;
    readonly ISessionService _session;
    readonly IVoiceLedgerService _voice;
    readonly IForecastService _forecast;
    readonly INotificationService _notifications;

    public DashboardViewModel(IDataService data, ISessionService session,
        IVoiceLedgerService voice, IForecastService forecast, INotificationService notifications)
    {
        _data = data;
        _session = session;
        _voice = voice;
        _forecast = forecast;
        _notifications = notifications;
    }

    public ObservableCollection<HelperCardViewModel> Helpers { get; } = new();

    [ObservableProperty] bool isDemoBannerVisible;
    [ObservableProperty] string totalPayable = "₹ 0";
    [ObservableProperty] bool isEmpty;

    [RelayCommand]
    public async Task LoadAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            IsDemoBannerVisible = _session.IsDemo;
            var today = DateTime.Today;
            var helpers = await _data.GetHelpersAsync();
            IsEmpty = helpers.Count == 0;

            Helpers.Clear();
            decimal total = 0;

            foreach (var h in helpers)
            {
                var breakdown = await _data.ComputeSettlementAsync(h.Id, today.Year, today.Month);
                total += Math.Max(0, breakdown.FinalPayable);

                var att = await _data.GetAttendanceAsync(h.Id, today.Year, today.Month);
                var todayRow = att.FirstOrDefault(a => a.Date == today.ToString("yyyy-MM-dd"));
                var (icon, color) = todayRow?.Status switch
                {
                    AttendanceStatus.Absent => (IconFont.Cancel, Color.FromArgb("#EF4444")),
                    AttendanceStatus.HalfDay => (IconFont.Timelapse, Color.FromArgb("#F59E0B")),
                    AttendanceStatus.Present => (IconFont.CheckCircle, Color.FromArgb("#10B981")),
                    _ => (IconFont.CheckCircle, Color.FromArgb("#CBD5E1"))
                };

                Helpers.Add(new HelperCardViewModel
                {
                    Helper = h,
                    PayableLabel = $"₹ {breakdown.FinalPayable:N0}",
                    TodayStatusIcon = icon,
                    TodayStatusColor = color,
                    ForecastLabel = await _forecast.GetForecastLabelAsync(h.Id)
                });

                await _notifications.ScheduleSalaryAlertsAsync(h);
            }

            TotalPayable = $"₹ {total:N0}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    Task AddHelperAsync() => Shell.Current.GoToAsync("helperform");

    /// <summary>Opens the same form used to add a helper, pre-filled for editing. The form
    /// already has a Delete action, so this also covers deleting a helper from the Dashboard.</summary>
    [RelayCommand]
    Task OpenEditAsync(HelperCardViewModel card) =>
        Shell.Current.GoToAsync($"helperform?helperId={card.Helper.Id}");

    [RelayCommand]
    Task OpenCalendarAsync(HelperCardViewModel card) =>
        Shell.Current.GoToAsync($"calendar?helperId={card.Helper.Id}");

    [RelayCommand]
    Task OpenSettlementAsync(HelperCardViewModel card) =>
        Shell.Current.GoToAsync($"settlement?helperId={card.Helper.Id}");

    /// <summary>Voice-to-Ledger from the dashboard mic button. Attendance/delivery commands are
    /// the recordings that live on the Calendar screen, so for those we jump straight there to
    /// show what was just logged; ledger money entries (advance/deduction/bonus/payment) stay on
    /// the Dashboard, same as before.</summary>
    [RelayCommand]
    async Task VoiceEntryAsync()
    {
        var result = await _voice.CaptureAndApplyAsync();
        await Toast(result?.Confirmation ?? Loc["Voice_NotUnderstood"]);
        if (result is null) return;

        await LoadAsync();
        if (result.ShowOnCalendar)
            await Shell.Current.GoToAsync($"calendar?helperId={result.HelperId}");
    }
}
