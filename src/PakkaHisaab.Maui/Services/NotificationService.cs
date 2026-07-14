using PakkaHisaab.Maui.Helpers;
using PakkaHisaab.Shared.Dtos;
using PakkaHisaab.Shared.Enums;
using Plugin.LocalNotification;
using Plugin.LocalNotification.AndroidOption;
using Plugin.LocalNotification.EventArgs;

namespace PakkaHisaab.Maui.Services;

public interface INotificationService
{
    Task<bool> EnsurePermissionAsync();
    /// <summary>Daily 5 PM: “Mark today's attendance for {helper}” with an inline “Absent” action.</summary>
    Task ScheduleDailyAttendanceReminderAsync(HelperDto helper);
    /// <summary>1st–10th of each month: “Salary pending for {helper}”. Cancelled once paid.</summary>
    Task ScheduleSalaryAlertsAsync(HelperDto helper);
    Task CancelSalaryAlertAsync(Guid helperId);
    Task CancelForHelperAsync(Guid helperId);
    Task CancelAllAsync();
    void WireActionHandlers();
}

public sealed class NotificationService : INotificationService
{
    readonly LocalizationResourceManager _loc = LocalizationResourceManager.Instance;

    // Guid → stable small int for notification ids.
    static int Stable(Guid id) => Math.Abs(id.GetHashCode() % 9_000);

    public async Task<bool> EnsurePermissionAsync()
    {
        if (await LocalNotificationCenter.Current.AreNotificationsEnabled())
            return true;
        return await LocalNotificationCenter.Current.RequestNotificationPermission();
    }

    public async Task ScheduleDailyAttendanceReminderAsync(HelperDto helper)
    {
        if (!await EnsurePermissionAsync()) return;

        int id = Constants.DailyAttendanceNotificationBase + Stable(helper.Id);
        LocalNotificationCenter.Current.Cancel(id);

        var todayAt5 = DateTime.Today.AddHours(17);
        var first = DateTime.Now < todayAt5 ? todayAt5 : todayAt5.AddDays(1);

        var request = new NotificationRequest
        {
            NotificationId = id,
            Title = _loc.Get("Notify_AttendanceTitle"),
            Description = _loc.Get("Notify_AttendanceBody", helper.Name),
            CategoryType = NotificationCategoryType.Reminder,
            ReturningData = $"attendance|{helper.Id}",
            Schedule = new NotificationRequestSchedule
            {
                NotifyTime = first,
                RepeatType = NotificationRepeat.Daily // fires at 17:00 every day
            },
            Android = new AndroidOptions
            {
                ChannelId = "attendance",
                Priority = AndroidPriority.High
            }
        };

        // Inline OS-shade action: default is Present (no tap needed); one tap marks Absent.
        request.Android.IconSmallName = new AndroidIcon("notification_icon");
        await LocalNotificationCenter.Current.Show(request);
    }

    public async Task ScheduleSalaryAlertsAsync(HelperDto helper)
    {
        if (!await EnsurePermissionAsync()) return;
        if (helper.WageType != WageType.MonthlySalary) return;

        int baseId = Constants.SalaryAlertNotificationBase + Stable(helper.Id);
        var now = DateTime.Now;
        var next = now.Day <= 10
            ? new DateTime(now.Year, now.Month, Math.Max(1, now.Day), 9, 0, 0)
            : new DateTime(now.Year, now.Month, 1, 9, 0, 0).AddMonths(1);

        // One alert per day, 1st through 10th, 9 AM. Each is cancelled by CancelSalaryAlertAsync.
        for (int day = next.Day; day <= 10; day++)
        {
            var when = new DateTime(next.Year, next.Month, day, 9, 0, 0);
            if (when < DateTime.Now) continue;

            await LocalNotificationCenter.Current.Show(new NotificationRequest
            {
                NotificationId = baseId + day,
                Title = _loc.Get("Notify_SalaryTitle"),
                Description = _loc.Get("Notify_SalaryBody", helper.Name),
                ReturningData = $"salary|{helper.Id}",
                Schedule = new NotificationRequestSchedule
                {
                    NotifyTime = when,
                    RepeatType = NotificationRepeat.No
                },
                Android = new AndroidOptions { ChannelId = "salary", Priority = AndroidPriority.High }
            });
        }
    }

    public Task CancelSalaryAlertAsync(Guid helperId)
    {
        int baseId = Constants.SalaryAlertNotificationBase + Stable(helperId);
        for (int day = 1; day <= 10; day++)
            LocalNotificationCenter.Current.Cancel(baseId + day);
        return Task.CompletedTask;
    }

    public Task CancelForHelperAsync(Guid helperId)
    {
        LocalNotificationCenter.Current.Cancel(
            Constants.DailyAttendanceNotificationBase + Stable(helperId));
        return CancelSalaryAlertAsync(helperId);
    }

    public Task CancelAllAsync()
    {
        LocalNotificationCenter.Current.CancelAll();
        return Task.CompletedTask;
    }

    /// <summary>
    /// Handles taps/actions raised from the OS shade — including the “Absent” action button —
    /// possibly before any UI exists, hence ServiceHelper instead of constructor injection.
    /// </summary>
    public void WireActionHandlers()
    {
        LocalNotificationCenter.Current.NotificationActionTapped += OnActionTapped;
    }

    static async void OnActionTapped(NotificationActionEventArgs e)
    {
        var data = e.Request?.ReturningData;
        if (string.IsNullOrEmpty(data)) return;

        var parts = data.Split('|');
        if (parts.Length != 2 || !Guid.TryParse(parts[1], out var helperId)) return;

        if (parts[0] == "attendance" && e.ActionId == 100) // 100 = "Mark Absent" action
        {
            var dataService = ServiceHelper.GetRequiredService<IDataService>();
            await dataService.SetAttendanceAsync(helperId,
                DateOnly.FromDateTime(DateTime.Today), AttendanceStatus.Absent);
        }
        else if (e.IsTapped)
        {
            // Deep link into the helper's calendar.
            await MainThread.InvokeOnMainThreadAsync(() =>
                Shell.Current.GoToAsync($"calendar?helperId={helperId}"));
        }
    }
}
