using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PakkaHisaab.Maui.Services;
using PakkaHisaab.Shared.Dtos;

namespace PakkaHisaab.Maui.ViewModels;

public partial class ReportsViewModel : BaseViewModel
{
    readonly IDataService _data;
    readonly IPdfReportService _pdf;

    public ReportsViewModel(IDataService data, IPdfReportService pdf)
    {
        _data = data;
        _pdf = pdf;
    }

    public ObservableCollection<HelperDto> Helpers { get; } = new();

    [ObservableProperty] HelperDto? selectedHelper;
    [ObservableProperty] DateTime selectedMonth = new(DateTime.Today.Year, DateTime.Today.Month, 1);
    public string MonthLabel => SelectedMonth.ToString("MMMM yyyy", Loc.CurrentCulture);

    partial void OnSelectedMonthChanged(DateTime value) => OnPropertyChanged(nameof(MonthLabel));

    public async Task InitializeAsync()
    {
        Helpers.Clear();
        foreach (var h in await _data.GetHelpersAsync())
            Helpers.Add(h);
        SelectedHelper ??= Helpers.FirstOrDefault();
    }

    [RelayCommand] void PreviousMonth() => SelectedMonth = SelectedMonth.AddMonths(-1);
    [RelayCommand] void NextMonth() => SelectedMonth = SelectedMonth.AddMonths(1);

    /// <summary>Report 1: helper monthly ledger with daily breakdown → share sheet (WhatsApp).</summary>
    [RelayCommand]
    async Task GenerateHelperLedgerAsync()
    {
        if (SelectedHelper is null || IsBusy) return;
        IsBusy = true;
        try
        {
            var path = await Task.Run(() =>
                _pdf.GenerateHelperLedgerAsync(SelectedHelper, SelectedMonth.Year, SelectedMonth.Month));
            await _pdf.ShareAsync(path);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Report 2: household master summary across all helpers.</summary>
    [RelayCommand]
    async Task GenerateHouseholdSummaryAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            var path = await Task.Run(() =>
                _pdf.GenerateHouseholdSummaryAsync(SelectedMonth.Year, SelectedMonth.Month));
            await _pdf.ShareAsync(path);
        }
        finally
        {
            IsBusy = false;
        }
    }
}
