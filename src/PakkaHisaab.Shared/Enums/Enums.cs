namespace PakkaHisaab.Shared.Enums;

/// <summary>Category of the household helper.</summary>
public enum HelperCategory
{
    HouseHelp = 0,
    Driver = 1,
    Cleaner = 2,
    MilkMan = 3,
    Cook = 4,
    Gardener = 5,
    Newspaper = 6,
    Tutor = 7,
    Other = 99
}

/// <summary>Attendance state cycled by the 2-tap calendar (Present → Absent → Half-Day → Present).</summary>
public enum AttendanceStatus
{
    Present = 0,
    Absent = 1,
    HalfDay = 2
}

/// <summary>Ledger money movement types.</summary>
public enum LedgerEntryType
{
    Advance = 0,        // cash given ahead of salary (deducted at settlement)
    SalaryPayment = 1,  // full/partial monthly settlement
    Bonus = 2,          // festival bonus etc. (added)
    Deduction = 3,      // damage/other deduction
    DeliveryCharge = 4  // computed charge for delivered units (e.g., milk)
}

public enum PaymentMethod
{
    Upi = 0,
    Cash = 1,
    BankTransfer = 2
}

/// <summary>Wage models supported by the settlement engine.</summary>
public enum WageType
{
    MonthlySalary = 0,  // house help, driver, cook…
    PerUnitDelivery = 1 // milkman, newspaper… (RatePerUnit × units)
}

public enum SettlementStatus
{
    Pending = 0,
    Paid = 1
}
