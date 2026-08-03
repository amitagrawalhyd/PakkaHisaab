using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PakkaHisaab.Maui.Services;
using PakkaHisaab.Shared.Dtos;
using PakkaHisaab.Shared.Enums;

namespace PakkaHisaab.Maui.ViewModels;

public record LedgerRow(Guid Id, string DateLabel, string TypeLabel, string AmountLabel, string? Note, bool IsCredit);

/// <summary>Dedicated cash-advance / ledger screen. Adding an entry opens a bottom sheet.</summary>
[QueryProperty(nameof(HelperIdRaw), "helperId")]
[QueryProperty(nameof(YearRaw), "year")]
[QueryProperty(nameof(MonthRaw), "month")]
public partial class LedgerViewModel : BaseViewModel
{
    readonly IDataService _data;
    Guid _helperId;
    string _period = string.Empty;

    public LedgerViewModel(IDataService data) => _data = data;

    public string? HelperIdRaw { get; set; }
    /// <summary>Optional — falls back to the current month when absent. Set explicitly when
    /// arriving from Calendar's month browser so entries added here (advances, bonuses,
    /// deductions) attribute to the month being reconciled instead of always today.</summary>
    public string? YearRaw { get; set; }
    public string? MonthRaw { get; set; }

    public ObservableCollection<LedgerRow> Entries { get; } = new();

    [ObservableProperty] string helperName = string.Empty;
    [ObservableProperty] string periodLabel = string.Empty;
    [ObservableProperty] string advanceTotal = "₹ 0";

    // Bottom-sheet form state
    [ObservableProperty] bool isSheetOpen;
    [ObservableProperty] string amount = string.Empty;
    [ObservableProperty] string note = string.Empty;
    [ObservableProperty] LedgerEntryType selectedType = LedgerEntryType.Advance;
    public IReadOnlyList<LedgerEntryType> EntryTypes { get; } = new[]
    {
        LedgerEntryType.Advance, LedgerEntryType.Bonus, LedgerEntryType.Deduction
    };

    public async Task InitializeAsync()
    {
        if (!Guid.TryParse(HelperIdRaw, out _helperId)) return;
        var helper = await _data.GetHelperAsync(_helperId);
        HelperName = helper?.Name ?? string.Empty;

        var today = DateTime.Today;
        int year = int.TryParse(YearRaw, out var y) ? y : today.Year;
        int month = int.TryParse(MonthRaw, out var m) ? m : today.Month;
        _period = $"{year:D4}-{month:D2}";
        PeriodLabel = new DateTime(year, month, 1).ToString("MMMM yyyy", Loc.CurrentCulture);

        await LoadAsync();
    }

    async Task LoadAsync()
    {
        var entries = await _data.GetLedgerAsync(_helperId, _period);

        Entries.Clear();
        foreach (var e in entries)
        {
            bool credit = e.Type is LedgerEntryType.Bonus;
            Entries.Add(new LedgerRow(
                e.Id,
                e.OccurredAtUtc.ToLocalTime().ToString("dd MMM, h:mm tt", Loc.CurrentCulture),
                Loc[$"LedgerType_{e.Type}"],
                $"{(credit ? "+" : "−")} ₹ {e.Amount:N0}",
                e.Note,
                credit));
        }

        AdvanceTotal = $"₹ {entries.Where(e => e.Type == LedgerEntryType.Advance).Sum(e => e.Amount):N0}";
    }

    [RelayCommand] void OpenSheet() => IsSheetOpen = true;
    [RelayCommand] void CloseSheet() => IsSheetOpen = false;

    /// <summary>Bound to Shell.BackButtonBehavior so both the nav-bar back arrow and the
    /// Android hardware back button return straight to the Dashboard's helper list.
    /// PopToRootAsync (not an absolute "//main/dashboard" GoToAsync) because Shell treats the
    /// target tab as already "current" while this page is pushed on top of it and no-ops instead
    /// of popping the stack — PopToRootAsync pops back to the tab root unconditionally.</summary>
    [RelayCommand]
    Task GoHomeAsync() => Shell.Current.Navigation.PopToRootAsync();

    [RelayCommand]
    async Task SaveEntryAsync()
    {
        if (!decimal.TryParse(Amount, out var amount) || amount <= 0)
        {
            await Toast(Loc["Ledger_InvalidAmount"]);
            return;
        }

        await _data.AddLedgerEntryAsync(new LedgerEntryDto
        {
            HelperId = _helperId,
            Type = SelectedType,
            Amount = amount,
            Method = PaymentMethod.Cash,
            Period = _period,
            Note = string.IsNullOrWhiteSpace(Note) ? null : Note.Trim()
        });

        Amount = Note = string.Empty;
        IsSheetOpen = false;
        await LoadAsync();
        await Toast(Loc["Common_Saved"]);
    }

    [RelayCommand]
    async Task DeleteEntryAsync(Guid id)
    {
        if (id == Guid.Empty) return;
        var page = Shell.Current.CurrentPage;
        bool confirm = await page.DisplayAlert(Loc["Common_Confirm"],
            Loc["Ledger_DeleteConfirm"], Loc["Common_Delete"], Loc["Common_Cancel"]);
        if (!confirm) return;

        await _data.DeleteLedgerEntryAsync(id);
        await LoadAsync();
        await Toast(Loc["Common_Deleted"]);
    }
}
