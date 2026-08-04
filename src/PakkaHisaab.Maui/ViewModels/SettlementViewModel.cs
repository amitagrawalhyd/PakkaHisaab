using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PakkaHisaab.Maui.Services;
using PakkaHisaab.Shared.Domain;
using PakkaHisaab.Shared.Dtos;
using PakkaHisaab.Shared.Enums;

namespace PakkaHisaab.Maui.ViewModels;

/// <summary>Salary settlement: breakdown → UPI app grid / Cash → mark paid → sync + stop alerts.</summary>
[QueryProperty(nameof(HelperIdRaw), "helperId")]
[QueryProperty(nameof(YearRaw), "year")]
[QueryProperty(nameof(MonthRaw), "month")]
public partial class SettlementViewModel : BaseViewModel
{
    readonly IDataService _data;
    readonly IUpiService _upi;
    HelperDto? _helper;
    SettlementBreakdown? _breakdown;
    int _year;
    int _month;

    public SettlementViewModel(IDataService data, IUpiService upi)
    {
        _data = data;
        _upi = upi;
    }

    public string? HelperIdRaw { get; set; }
    /// <summary>Optional — falls back to the current month when absent (e.g. Dashboard's
    /// direct "Settle" link). Set explicitly when arriving from Calendar's month browser so a
    /// past month can be settled instead of always defaulting to today.</summary>
    public string? YearRaw { get; set; }
    public string? MonthRaw { get; set; }

    [ObservableProperty] string helperName = string.Empty;
    [ObservableProperty] string periodLabel = string.Empty;
    [ObservableProperty] string grossLabel = string.Empty;
    [ObservableProperty] string absenceLabel = string.Empty;
    [ObservableProperty] string advanceLabel = string.Empty;
    [ObservableProperty] string payableLabel = string.Empty;
    /// <summary>Manual amount amendment — pre-filled with the computed payable.</summary>
    [ObservableProperty] string amountToPay = string.Empty;
    [ObservableProperty] bool hasUpiId;
    /// <summary>True when settling any month other than the current one — shown as a chip so a
    /// back-dated payment never looks identical to a current-month one.</summary>
    [ObservableProperty] bool isBackdated;
    /// <summary>True once this period already has a recorded payment — Pay actions are hidden
    /// so re-visiting an old settled month can't accidentally double-pay it.</summary>
    [ObservableProperty] bool isAlreadyPaid;
    [ObservableProperty] string alreadyPaidLabel = string.Empty;

    public async Task InitializeAsync()
    {
        if (!Guid.TryParse(HelperIdRaw, out var helperId)) return;
        _helper = await _data.GetHelperAsync(helperId);
        if (_helper is null) return;

        var today = DateTime.Today;
        _year = int.TryParse(YearRaw, out var y) ? y : today.Year;
        _month = int.TryParse(MonthRaw, out var m) ? m : today.Month;
        var period = new DateTime(_year, _month, 1);
        IsBackdated = period.Year != today.Year || period.Month != today.Month;

        _breakdown = await _data.ComputeSettlementAsync(helperId, _year, _month);

        HelperName = _helper.Name;
        PeriodLabel = period.ToString("MMMM yyyy", Loc.CurrentCulture);
        GrossLabel = $"₹ {_breakdown.GrossWage:N2}";
        AbsenceLabel = $"− ₹ {_breakdown.AbsenceDeduction:N2} ({_breakdown.UnpaidAbsenceDays:0.#})";
        AdvanceLabel = $"− ₹ {_breakdown.Advances:N2}";
        PayableLabel = $"₹ {_breakdown.FinalPayable:N2}";
        AmountToPay = Math.Max(0, _breakdown.FinalPayable).ToString("0.##");
        HasUpiId = !string.IsNullOrWhiteSpace(_helper.UpiId);

        var existing = await _data.GetSettlementAsync(helperId, $"{_year:D4}-{_month:D2}");
        IsAlreadyPaid = existing?.Status == SettlementStatus.Paid;
        AlreadyPaidLabel = IsAlreadyPaid
            ? Loc.Get("Settle_AlreadyPaid", existing!.PaidAtUtc?.ToLocalTime().ToString("dd MMM yyyy") ?? "-", existing.FinalPayable)
            : string.Empty;
    }

    /// <summary>Hands off to the OS's native UPI app chooser — it shows the real installed apps'
    /// own icons directly from Android, so this app never needs to embed provider logos itself.</summary>
    [RelayCommand]
    async Task PayWithUpiAsync()
    {
        if (IsAlreadyPaid || _helper is null || !decimal.TryParse(AmountToPay, out var amount) || amount <= 0)
        {
            await Toast(Loc["Settle_InvalidAmount"]);
            return;
        }

        var note = Loc.Get("Settle_UpiNote", PeriodLabel);
        bool launched = await _upi.LaunchAsync(_helper, amount, note);
        if (!launched)
        {
            await Toast(Loc["Settle_NoUpiApp"]);
            return;
        }

        // The OS returns after the UPI flow; confirm before writing the ledger.
        var page = Shell.Current.CurrentPage;
        bool done = await page.DisplayAlert(Loc["Settle_ConfirmTitle"],
            Loc.Get("Settle_ConfirmBody", HelperName, amount),
            Loc["Settle_ConfirmYes"], Loc["Common_Cancel"]);
        if (done)
            await CompleteAsync(amount, PaymentMethod.Upi);
    }

    /// <summary>"Cash" logging option — no deep link, just record it.</summary>
    [RelayCommand]
    async Task PayCashAsync()
    {
        if (IsAlreadyPaid || _helper is null || !decimal.TryParse(AmountToPay, out var amount) || amount <= 0)
        {
            await Toast(Loc["Settle_InvalidAmount"]);
            return;
        }
        await CompleteAsync(amount, PaymentMethod.Cash);
    }

    async Task CompleteAsync(decimal amount, PaymentMethod method)
    {
        var period = $"{_year:D4}-{_month:D2}";
        try
        {
            // Updates SQLite, triggers the Shiny sync job and stops the salary notifications.
            await _data.MarkPaidAsync(_helper!.Id, period, amount, method, null);
        }
        catch (Exception)
        {
            // A failure here must never leave the user staring at an unresponsive screen with
            // no feedback — [RelayCommand]'s async void-like execution swallows unhandled
            // exceptions silently otherwise, which looks exactly like "nothing happened".
            await Toast(Loc["Settle_Failed"]);
            return;
        }

        await Toast(Loc["Settle_Recorded"]);
        await GoHomeAsync();
    }

    /// <summary>Bound to Shell.BackButtonBehavior so both the nav-bar back arrow and the
    /// Android hardware back button return straight to the Dashboard's helper list, instead of
    /// stepping back through intermediate screens (e.g. Calendar) that led here.
    /// PopToRootAsync (not an absolute "//main/dashboard" GoToAsync) because Shell treats the
    /// target tab as already "current" while this page is pushed on top of it and no-ops instead
    /// of popping the stack — PopToRootAsync pops back to the tab root unconditionally.</summary>
    [RelayCommand]
    Task GoHomeAsync() => Shell.Current.Navigation.PopToRootAsync();
}
