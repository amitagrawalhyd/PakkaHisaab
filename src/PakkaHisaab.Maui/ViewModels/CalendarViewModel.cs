using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PakkaHisaab.Maui.Helpers;
using PakkaHisaab.Maui.Services;
using PakkaHisaab.Shared.Dtos;
using PakkaHisaab.Shared.Enums;

namespace PakkaHisaab.Maui.ViewModels;

/// <summary>A single tappable day cell in the attendance calendar.</summary>
public partial class DayCellViewModel : ObservableObject
{
    public DateOnly? Date { get; init; }          // null ⇒ leading blank cell
    public string DayNumber => Date?.Day.ToString() ?? string.Empty;
    public bool IsToday => Date == DateOnly.FromDateTime(DateTime.Today);
    public bool IsFuture => Date > DateOnly.FromDateTime(DateTime.Today);
    public bool IsBlank => Date is null;

    [ObservableProperty] Color cellColor = Colors.Transparent;
    [ObservableProperty] Color textColor = Color.FromArgb("#1E293B");
    [ObservableProperty] string unitsLabel = string.Empty;

    public void Apply(AttendanceDto? attendance)
    {
        if (Date is null || IsFuture)
        {
            CellColor = Colors.Transparent;
            return;
        }

        (CellColor, TextColor) = attendance?.Status switch
        {
            AttendanceStatus.Absent => (Color.FromArgb("#EF4444"), Colors.White),
            AttendanceStatus.HalfDay => (Color.FromArgb("#F59E0B"), Colors.White),
            AttendanceStatus.Present => (Color.FromArgb("#10B981"), Colors.White),
            _ => (Color.FromArgb("#F1F5F9"), Color.FromArgb("#1E293B")) // unmarked
        };
        UnitsLabel = attendance is { UnitsDelivered: > 0 }
            ? attendance.UnitsDelivered.ToString("0.#")
            : string.Empty;
    }
}

[QueryProperty(nameof(HelperIdRaw), "helperId")]
public partial class CalendarViewModel : BaseViewModel
{
    readonly IDataService _data;
    Guid _helperId;
    HelperDto? _helper;

    public CalendarViewModel(IDataService data) => _data = data;

    public string? HelperIdRaw { get; set; }

    public ObservableCollection<DayCellViewModel> Days { get; } = new();

    [ObservableProperty] string helperName = string.Empty;
    [ObservableProperty] string categoryKey = string.Empty;
    [ObservableProperty] string categoryIcon = IconFont.Person;
    [ObservableProperty] string monthLabel = string.Empty;
    [ObservableProperty] bool isPerUnitHelper;
    [ObservableProperty] string summaryLabel = string.Empty;
    [ObservableProperty] DateTime currentMonth = new(DateTime.Today.Year, DateTime.Today.Month, 1);

    public async Task InitializeAsync()
    {
        if (!Guid.TryParse(HelperIdRaw, out _helperId)) return;
        _helper = await _data.GetHelperAsync(_helperId);
        if (_helper is null) return;

        HelperName = _helper.Name;
        CategoryKey = $"Category_{_helper.Category}";
        CategoryIcon = _helper.Category switch
        {
            HelperCategory.MilkMan => IconFont.WaterDrop,
            HelperCategory.Driver => IconFont.Person,
            _ => IconFont.Person
        };
        IsPerUnitHelper = _helper.WageType == WageType.PerUnitDelivery;
        await BuildMonthAsync();
    }

    [RelayCommand]
    async Task PreviousMonthAsync()
    {
        CurrentMonth = CurrentMonth.AddMonths(-1);
        await BuildMonthAsync();
    }

    [RelayCommand]
    async Task NextMonthAsync()
    {
        CurrentMonth = CurrentMonth.AddMonths(1);
        await BuildMonthAsync();
    }

    /// <summary>The 2-tap interaction: tap a day to cycle Present → Absent → Half-Day.</summary>
    [RelayCommand]
    async Task DayTappedAsync(DayCellViewModel cell)
    {
        if (cell.IsBlank || cell.IsFuture || _helper is null) return;

        if (IsPerUnitHelper)
        {
            // Per-unit helpers: prompt for delivered units instead of cycling states.
            var page = Shell.Current.CurrentPage;
            string result = await page.DisplayPromptAsync(
                Loc["Calendar_UnitsTitle"],
                Loc.Get("Calendar_UnitsPrompt", _helper.UnitLabel),
                accept: Loc["Common_Save"], cancel: Loc["Common_Cancel"],
                keyboard: Keyboard.Numeric,
                initialValue: cell.UnitsLabel);
            if (result is null || !decimal.TryParse(result, out var units)) return;
            await _data.SetUnitsAsync(_helperId, cell.Date!.Value, units);
        }
        else
        {
            await _data.ToggleAttendanceAsync(_helperId, cell.Date!.Value);
        }

        await BuildMonthAsync(); // zero-latency: reads straight from SQLite
    }

    [RelayCommand]
    Task OpenLedgerAsync() =>
        Shell.Current.GoToAsync($"ledger?helperId={_helperId}");

    [RelayCommand]
    Task OpenSettlementAsync() =>
        Shell.Current.GoToAsync($"settlement?helperId={_helperId}");

    /// <summary>Bound to Shell.BackButtonBehavior so both the nav-bar back arrow and the
    /// Android hardware back button return straight to the Dashboard's helper list.
    /// PopToRootAsync (not an absolute "//main/dashboard" GoToAsync) because Shell treats the
    /// target tab as already "current" while this page is pushed on top of it and no-ops instead
    /// of popping the stack — PopToRootAsync pops back to the tab root unconditionally.</summary>
    [RelayCommand]
    Task GoHomeAsync() => Shell.Current.Navigation.PopToRootAsync();

    async Task BuildMonthAsync()
    {
        if (_helper is null) return;

        MonthLabel = CurrentMonth.ToString("MMMM yyyy", Loc.CurrentCulture);
        var attendance = await _data.GetAttendanceAsync(_helperId, CurrentMonth.Year, CurrentMonth.Month);
        var byDate = attendance.ToDictionary(a => a.Date);

        Days.Clear();

        // Leading blanks so day 1 lands on its weekday column (weeks start Monday).
        int lead = ((int)CurrentMonth.DayOfWeek + 6) % 7;
        for (int i = 0; i < lead; i++)
            Days.Add(new DayCellViewModel());

        int daysInMonth = DateTime.DaysInMonth(CurrentMonth.Year, CurrentMonth.Month);
        for (int day = 1; day <= daysInMonth; day++)
        {
            var date = new DateOnly(CurrentMonth.Year, CurrentMonth.Month, day);
            var cell = new DayCellViewModel { Date = date };
            byDate.TryGetValue(date.ToString("yyyy-MM-dd"), out var att);
            cell.Apply(att);
            Days.Add(cell);
        }

        var breakdown = await _data.ComputeSettlementAsync(_helperId, CurrentMonth.Year, CurrentMonth.Month);
        SummaryLabel = IsPerUnitHelper
            ? Loc.Get("Calendar_UnitsSummary", breakdown.UnitsDelivered, _helper.UnitLabel, breakdown.FinalPayable)
            : Loc.Get("Calendar_Summary", breakdown.AbsentDays, breakdown.FinalPayable);
    }
}
