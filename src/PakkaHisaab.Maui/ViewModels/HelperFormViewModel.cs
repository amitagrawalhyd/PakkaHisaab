using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PakkaHisaab.Maui.Services;
using PakkaHisaab.Shared.Dtos;
using PakkaHisaab.Shared.Enums;

namespace PakkaHisaab.Maui.ViewModels;

[QueryProperty(nameof(HelperId), "helperId")]
public partial class HelperFormViewModel : BaseViewModel
{
    readonly IDataService _data;
    HelperDto _editing = new();

    public HelperFormViewModel(IDataService data) => _data = data;

    public string? HelperId { get; set; }

    public IReadOnlyList<HelperCategory> Categories { get; } =
        Enum.GetValues<HelperCategory>();

    [ObservableProperty] string name = string.Empty;
    [ObservableProperty] string whatsAppNumber = string.Empty;
    [ObservableProperty] string upiId = string.Empty;
    [ObservableProperty] HelperCategory selectedCategory = HelperCategory.HouseHelp;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPerUnit))]
    bool perUnitWage;
    [ObservableProperty] string monthlyWage = string.Empty;
    [ObservableProperty] string ratePerUnit = string.Empty;
    [ObservableProperty] string unitLabel = "L";
    [ObservableProperty] string allowedAbsences = "2";
    [ObservableProperty] bool carryOverLeaveAllowed;

    public bool IsPerUnit => PerUnitWage;

    partial void OnSelectedCategoryChanged(HelperCategory value)
    {
        // Milkman/newspaper default to per-unit billing.
        if (value is HelperCategory.MilkMan or HelperCategory.Newspaper)
            PerUnitWage = true;
    }

    public async Task InitializeAsync()
    {
        if (!Guid.TryParse(HelperId, out var id)) return;
        var existing = await _data.GetHelperAsync(id);
        if (existing is null) return;

        _editing = existing;
        Name = existing.Name;
        WhatsAppNumber = existing.WhatsAppNumber;
        UpiId = existing.UpiId ?? string.Empty;
        SelectedCategory = existing.Category;
        PerUnitWage = existing.WageType == WageType.PerUnitDelivery;
        MonthlyWage = existing.MonthlyWage == 0 ? string.Empty : existing.MonthlyWage.ToString("0.##");
        RatePerUnit = existing.RatePerUnit == 0 ? string.Empty : existing.RatePerUnit.ToString("0.##");
        UnitLabel = existing.UnitLabel;
        AllowedAbsences = existing.MonthlyAllowedAbsences.ToString();
        CarryOverLeaveAllowed = existing.CarryOverLeaveAllowed;
    }

    [RelayCommand]
    async Task SaveAsync()
    {
        if (string.IsNullOrWhiteSpace(Name))
        {
            await Toast(Loc["HelperForm_NameRequired"]);
            return;
        }

        _editing.Name = Name.Trim();
        _editing.WhatsAppNumber = WhatsAppNumber.Trim();
        _editing.UpiId = string.IsNullOrWhiteSpace(UpiId) ? null : UpiId.Trim();
        _editing.Category = SelectedCategory;
        _editing.WageType = PerUnitWage ? WageType.PerUnitDelivery : WageType.MonthlySalary;
        _editing.MonthlyWage = decimal.TryParse(MonthlyWage, out var wage) ? wage : 0;
        _editing.RatePerUnit = decimal.TryParse(RatePerUnit, out var rate) ? rate : 0;
        _editing.UnitLabel = string.IsNullOrWhiteSpace(UnitLabel) ? "L" : UnitLabel.Trim();
        _editing.MonthlyAllowedAbsences = int.TryParse(AllowedAbsences, out var abs) ? abs : 0;
        _editing.CarryOverLeaveAllowed = CarryOverLeaveAllowed;

        await _data.SaveHelperAsync(_editing);
        await Toast(Loc["Common_Saved"]);
        await Shell.Current.GoToAsync("..");
    }

    [RelayCommand]
    async Task DeleteAsync()
    {
        if (_editing.Id == Guid.Empty) return;
        var page = Shell.Current.CurrentPage;
        bool confirm = await page.DisplayAlert(Loc["Common_Confirm"],
            Loc["HelperForm_DeleteConfirm"], Loc["Common_Delete"], Loc["Common_Cancel"]);
        if (!confirm) return;

        await _data.DeleteHelperAsync(_editing.Id);
        await Shell.Current.GoToAsync("..");
    }
}
