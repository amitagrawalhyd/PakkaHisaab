using PakkaHisaab.Shared.Dtos;
using PakkaHisaab.Shared.Enums;
using SQLite;

namespace PakkaHisaab.Maui.Models;

/// <summary>
/// SQLite rows mirror the shared DTOs 1:1 plus one client-only flag: <see cref="IsDirty"/>,
/// the outbox marker the Shiny sync job uses to find unpushed changes.
/// Mapping is mechanical (see <see cref="EntityMapper"/>) so business logic always runs on DTOs
/// through the shared <c>SalaryCalculator</c> — DRY across client and server.
/// </summary>
public abstract class LocalEntityBase
{
    [PrimaryKey] public Guid Id { get; set; }
    [Indexed] public DateTime ModifiedAtUtc { get; set; }
    public long RowVersion { get; set; }
    public bool IsDeleted { get; set; }
    [Indexed] public bool IsDirty { get; set; }
}

[Table("Helpers")]
public class LocalHelper : LocalEntityBase
{
    public string Name { get; set; } = string.Empty;
    public string WhatsAppNumber { get; set; } = string.Empty;
    public string? UpiId { get; set; }
    public HelperCategory Category { get; set; }
    public WageType WageType { get; set; }
    public decimal MonthlyWage { get; set; }
    public decimal RatePerUnit { get; set; }
    public string UnitLabel { get; set; } = "L";
    public int MonthlyAllowedAbsences { get; set; }
    public bool CarryOverLeaveAllowed { get; set; }
    public int CarriedOverLeaves { get; set; }
    public bool IsActive { get; set; } = true;
}

[Table("Attendance")]
public class LocalAttendance : LocalEntityBase
{
    [Indexed] public Guid HelperId { get; set; }
    [Indexed] public string Date { get; set; } = string.Empty; // yyyy-MM-dd
    public AttendanceStatus Status { get; set; }
    public decimal UnitsDelivered { get; set; }
}

[Table("LedgerEntries")]
public class LocalLedgerEntry : LocalEntityBase
{
    [Indexed] public Guid HelperId { get; set; }
    public LedgerEntryType Type { get; set; }
    public decimal Amount { get; set; }
    public PaymentMethod Method { get; set; }
    public string? Note { get; set; }
    [Indexed] public string Period { get; set; } = string.Empty; // yyyy-MM
    public DateTime OccurredAtUtc { get; set; }
    public string? UpiTransactionRef { get; set; }
}

[Table("Settlements")]
public class LocalSettlement : LocalEntityBase
{
    [Indexed] public Guid HelperId { get; set; }
    [Indexed] public string Period { get; set; } = string.Empty;
    public SettlementStatus Status { get; set; }
    public decimal FinalPayable { get; set; }
    public DateTime? PaidAtUtc { get; set; }
}

public static class EntityMapper
{
    public static HelperDto ToDto(this LocalHelper e) => new()
    {
        Id = e.Id, Name = e.Name, WhatsAppNumber = e.WhatsAppNumber, UpiId = e.UpiId,
        Category = e.Category, WageType = e.WageType, MonthlyWage = e.MonthlyWage,
        RatePerUnit = e.RatePerUnit, UnitLabel = e.UnitLabel,
        MonthlyAllowedAbsences = e.MonthlyAllowedAbsences,
        CarryOverLeaveAllowed = e.CarryOverLeaveAllowed, CarriedOverLeaves = e.CarriedOverLeaves,
        IsActive = e.IsActive, ModifiedAtUtc = e.ModifiedAtUtc, RowVersion = e.RowVersion,
        IsDeleted = e.IsDeleted
    };

    public static LocalHelper ToLocal(this HelperDto d, bool dirty = false) => new()
    {
        Id = d.Id, Name = d.Name, WhatsAppNumber = d.WhatsAppNumber, UpiId = d.UpiId,
        Category = d.Category, WageType = d.WageType, MonthlyWage = d.MonthlyWage,
        RatePerUnit = d.RatePerUnit, UnitLabel = d.UnitLabel,
        MonthlyAllowedAbsences = d.MonthlyAllowedAbsences,
        CarryOverLeaveAllowed = d.CarryOverLeaveAllowed, CarriedOverLeaves = d.CarriedOverLeaves,
        IsActive = d.IsActive, ModifiedAtUtc = d.ModifiedAtUtc, RowVersion = d.RowVersion,
        IsDeleted = d.IsDeleted, IsDirty = dirty
    };

    public static AttendanceDto ToDto(this LocalAttendance e) => new()
    {
        Id = e.Id, HelperId = e.HelperId, Date = e.Date, Status = e.Status,
        UnitsDelivered = e.UnitsDelivered, ModifiedAtUtc = e.ModifiedAtUtc,
        RowVersion = e.RowVersion, IsDeleted = e.IsDeleted
    };

    public static LocalAttendance ToLocal(this AttendanceDto d, bool dirty = false) => new()
    {
        Id = d.Id, HelperId = d.HelperId, Date = d.Date, Status = d.Status,
        UnitsDelivered = d.UnitsDelivered, ModifiedAtUtc = d.ModifiedAtUtc,
        RowVersion = d.RowVersion, IsDeleted = d.IsDeleted, IsDirty = dirty
    };

    public static LedgerEntryDto ToDto(this LocalLedgerEntry e) => new()
    {
        Id = e.Id, HelperId = e.HelperId, Type = e.Type, Amount = e.Amount, Method = e.Method,
        Note = e.Note, Period = e.Period, OccurredAtUtc = e.OccurredAtUtc,
        UpiTransactionRef = e.UpiTransactionRef, ModifiedAtUtc = e.ModifiedAtUtc,
        RowVersion = e.RowVersion, IsDeleted = e.IsDeleted
    };

    public static LocalLedgerEntry ToLocal(this LedgerEntryDto d, bool dirty = false) => new()
    {
        Id = d.Id, HelperId = d.HelperId, Type = d.Type, Amount = d.Amount, Method = d.Method,
        Note = d.Note, Period = d.Period, OccurredAtUtc = d.OccurredAtUtc,
        UpiTransactionRef = d.UpiTransactionRef, ModifiedAtUtc = d.ModifiedAtUtc,
        RowVersion = d.RowVersion, IsDeleted = d.IsDeleted, IsDirty = dirty
    };

    public static SettlementDto ToDto(this LocalSettlement e) => new()
    {
        Id = e.Id, HelperId = e.HelperId, Period = e.Period, Status = e.Status,
        FinalPayable = e.FinalPayable, PaidAtUtc = e.PaidAtUtc,
        ModifiedAtUtc = e.ModifiedAtUtc, RowVersion = e.RowVersion, IsDeleted = e.IsDeleted
    };

    public static LocalSettlement ToLocal(this SettlementDto d, bool dirty = false) => new()
    {
        Id = d.Id, HelperId = d.HelperId, Period = d.Period, Status = d.Status,
        FinalPayable = d.FinalPayable, PaidAtUtc = d.PaidAtUtc,
        ModifiedAtUtc = d.ModifiedAtUtc, RowVersion = d.RowVersion, IsDeleted = d.IsDeleted,
        IsDirty = dirty
    };
}
