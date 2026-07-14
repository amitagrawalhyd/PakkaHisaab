using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PakkaHisaab.Maui.Services;
using PakkaHisaab.Shared.Domain;
using PakkaHisaab.Shared.Dtos;
using PakkaHisaab.Shared.Enums;

namespace PakkaHisaab.Maui.ViewModels;

/// <summary>Salary settlement: breakdown → UPI app grid / Cash → mark paid → sync + stop alerts.</summary>
[QueryProperty(nameof(HelperIdRaw), "helperId")]
public partial class SettlementViewModel : BaseViewModel
{
    readonly IDataService _data;
    readonly IUpiService _upi;
    HelperDto? _helper;
    SettlementBreakdown? _breakdown;

    public SettlementViewModel(IDataService data, IUpiService upi)
    {
        _data = data;
        _upi = upi;
    }

    public string? HelperIdRaw { get; set; }

    [ObservableProperty] string helperName = string.Empty;
    [ObservableProperty] string periodLabel = string.Empty;
    [ObservableProperty] string grossLabel = string.Empty;
    [ObservableProperty] string absenceLabel = string.Empty;
    [ObservableProperty] string advanceLabel = string.Empty;
    [ObservableProperty] string payableLabel = string.Empty;
    /// <summary>Manual amount amendment — pre-filled with the computed payable.</summary>
    [ObservableProperty] string amountToPay = string.Empty;
    [ObservableProperty] bool hasUpiId;

    public async Task InitializeAsync()
    {
        if (!Guid.TryParse(HelperIdRaw, out var helperId)) return;
        _helper = await _data.GetHelperAsync(helperId);
        if (_helper is null) return;

        var today = DateTime.Today;
        _breakdown = await _data.ComputeSettlementAsync(helperId, today.Year, today.Month);

        HelperName = _helper.Name;
        PeriodLabel = today.ToString("MMMM yyyy", Loc.CurrentCulture);
        GrossLabel = $"₹ {_breakdown.GrossWage:N2}";
        AbsenceLabel = $"− ₹ {_breakdown.AbsenceDeduction:N2} ({_breakdown.UnpaidAbsenceDays:0.#})";
        AdvanceLabel = $"− ₹ {_breakdown.Advances:N2}";
        PayableLabel = $"₹ {_breakdown.FinalPayable:N2}";
        AmountToPay = Math.Max(0, _breakdown.FinalPayable).ToString("0.##");
        HasUpiId = !string.IsNullOrWhiteSpace(_helper.UpiId);
    }

    /// <summary>Hands off to the OS's native UPI app chooser — it shows the real installed apps'
    /// own icons directly from Android, so this app never needs to embed provider logos itself.</summary>
    [RelayCommand]
    async Task PayWithUpiAsync()
    {
        if (_helper is null || !decimal.TryParse(AmountToPay, out var amount) || amount <= 0)
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
        if (_helper is null || !decimal.TryParse(AmountToPay, out var amount) || amount <= 0)
        {
            await Toast(Loc["Settle_InvalidAmount"]);
            return;
        }
        await CompleteAsync(amount, PaymentMethod.Cash);
    }

    async Task CompleteAsync(decimal amount, PaymentMethod method)
    {
        var period = DateTime.Today.ToString("yyyy-MM");
        // Updates SQLite, triggers the Shiny sync job and stops the salary notifications.
        await _data.MarkPaidAsync(_helper!.Id, period, amount, method, null);
        await Toast(Loc["Settle_Recorded"]);
        await GoHomeAsync();
    }

    /// <summary>Bound to Shell.BackButtonBehavior so both the nav-bar back arrow and the
    /// Android hardware back button return straight to the Dashboard's helper list, instead of
    /// stepping back through intermediate screens (e.g. Calendar) that led here.</summary>
    [RelayCommand]
    Task GoHomeAsync() => Shell.Current.GoToAsync("//main/dashboard");
}
