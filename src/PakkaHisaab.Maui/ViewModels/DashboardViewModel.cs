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
    /// <summary>e.g. "⚠ 2 months pending" — set when past (pre-current-month) settlements are
    /// still unpaid. Drives whether "Settle" jumps straight to this month or to Calendar so the
    /// user can pick which back month to pay first.</summary>
    [ObservableProperty] string? arrearsLabel;
    public bool HasArrears => !string.IsNullOrEmpty(ArrearsLabel);
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
    [ObservableProperty] string totalPayableSubtitle = string.Empty;
    [ObservableProperty] bool isEmpty;
    [ObservableProperty] bool isListening;

    // Small minimum visible duration so a very fast local-only load (this app's dashboard is
    // almost all SQLite reads) still gives the native pull-to-refresh spinner a full animation
    // cycle rather than flashing instantly.
    static readonly TimeSpan MinRefreshDuration = TimeSpan.FromMilliseconds(400);

    [RelayCommand]
    public async Task LoadAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        var startedUtc = DateTime.UtcNow;
        try
        {
            IsDemoBannerVisible = _session.IsDemo;
            var today = DateTime.Today;
            var helpers = await _data.GetHelpersAsync();
            IsEmpty = helpers.Count == 0;

            Helpers.Clear();
            decimal total = 0;

            int helperCount = 0;
            foreach (var h in helpers)
            {
                var breakdown = await _data.ComputeSettlementAsync(h.Id, today.Year, today.Month);
                total += Math.Max(0, breakdown.FinalPayable);
                helperCount++;

                var att = await _data.GetAttendanceAsync(h.Id, today.Year, today.Month);
                var todayRow = att.FirstOrDefault(a => a.Date == today.ToString("yyyy-MM-dd"));
                var (icon, color) = todayRow?.Status switch
                {
                    AttendanceStatus.Absent => (IconFont.Cancel, Color.FromArgb("#EF4444")),
                    AttendanceStatus.HalfDay => (IconFont.Timelapse, Color.FromArgb("#F59E0B")),
                    AttendanceStatus.Present => (IconFont.CheckCircle, Color.FromArgb("#10B981")),
                    _ => (IconFont.CheckCircle, Color.FromArgb("#CBD5E1"))
                };

                var arrears = await _data.GetUnpaidPeriodsAsync(h.Id);

                Helpers.Add(new HelperCardViewModel
                {
                    Helper = h,
                    PayableLabel = $"₹ {breakdown.FinalPayable:N0}",
                    TodayStatusIcon = icon,
                    TodayStatusColor = color,
                    ForecastLabel = await _forecast.GetForecastLabelAsync(h.Id),
                    ArrearsLabel = arrears.Count > 0 ? Loc.Get("Dash_ArrearsPending", arrears.Count) : null
                });

                // Only keep nagging while this month is actually still owed — otherwise a paid
                // month gets its 1st-10th reminder wrongly re-armed on every dashboard refresh.
                if (breakdown.FinalPayable > 0)
                    await _notifications.ScheduleSalaryAlertsAsync(h);
                else
                    await _notifications.CancelSalaryAlertAsync(h.Id);
            }

            TotalPayable = $"₹ {total:N0}";
            TotalPayableSubtitle = helperCount > 0 ? Loc.Get("Dash_TotalAcrossHelpers", helperCount) : string.Empty;
        }
        finally
        {
            var elapsed = DateTime.UtcNow - startedUtc;
            if (elapsed < MinRefreshDuration)
                await Task.Delay(MinRefreshDuration - elapsed);
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

    /// <summary>Straight to this month's Settlement when nothing older is owed (unchanged,
    /// zero-friction path). When back months are still unpaid, routes to Calendar instead —
    /// it already has month-browsing and its own Settle button, reused here as the "pick which
    /// month" UI rather than building a separate picker.</summary>
    [RelayCommand]
    Task OpenSettlementAsync(HelperCardViewModel card) => card.HasArrears
        ? Shell.Current.GoToAsync($"calendar?helperId={card.Helper.Id}")
        : Shell.Current.GoToAsync($"settlement?helperId={card.Helper.Id}");

    /// <summary>Voice-to-Ledger from the dashboard mic button. Attendance/delivery commands are
    /// the recordings that live on the Calendar screen, so for those we jump straight there to
    /// show what was just logged; ledger money entries (advance/deduction/bonus/payment) stay on
    /// the Dashboard, same as before.</summary>
    [RelayCommand]
    async Task VoiceEntryAsync()
    {
        if (IsListening) return; // avoid overlapping mic sessions from a double-tap
        IsListening = true;
        try
        {
            var result = await _voice.CaptureAndApplyAsync();
            await Toast(VoiceMessageFor(result));
            if (result.Outcome != VoiceOutcome.Success) return;

            await LoadAsync();
            if (result.ShowOnCalendar)
                await Shell.Current.GoToAsync($"calendar?helperId={result.HelperId}");
        }
        finally
        {
            IsListening = false;
        }
    }

    string VoiceMessageFor(VoiceLedgerResult result) => result.Outcome switch
    {
        VoiceOutcome.Success => result.Confirmation!,
        VoiceOutcome.PermissionDenied => Loc["Voice_PermissionDenied"],
        VoiceOutcome.NoSpeechDetected => Loc["Voice_NoSpeech"],
        VoiceOutcome.HelperNotRecognized => Loc["Voice_HelperNotRecognized"],
        VoiceOutcome.IntentNotRecognized => Loc["Voice_NotUnderstood"],
        _ => Loc["Voice_Error"]
    };
}
