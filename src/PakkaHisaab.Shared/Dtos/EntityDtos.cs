using PakkaHisaab.Shared.Enums;
using PakkaHisaab.Shared.Sync;

namespace PakkaHisaab.Shared.Dtos;

/// <summary>A registered household helper. Shared verbatim between SQLite rows and API payloads.</summary>
public class HelperDto : ISyncEntity
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string WhatsAppNumber { get; set; } = string.Empty;
    public string? UpiId { get; set; }
    public HelperCategory Category { get; set; }
    public WageType WageType { get; set; }
    public decimal MonthlyWage { get; set; }
    /// <summary>Rate per delivered unit (e.g., ₹ per litre) when <see cref="WageType.PerUnitDelivery"/>.</summary>
    public decimal RatePerUnit { get; set; }
    public string UnitLabel { get; set; } = "L";
    public int MonthlyAllowedAbsences { get; set; }
    public bool CarryOverLeaveAllowed { get; set; }
    public int CarriedOverLeaves { get; set; }
    public bool IsActive { get; set; } = true;

    public DateTime ModifiedAtUtc { get; set; }
    public long RowVersion { get; set; }
    public bool IsDeleted { get; set; }
}

/// <summary>One calendar day for one helper: attendance state or delivered units.</summary>
public class AttendanceDto : ISyncEntity
{
    public Guid Id { get; set; }
    public Guid HelperId { get; set; }
    /// <summary>Local calendar date, stored as yyyy-MM-dd (time-zone independent).</summary>
    public string Date { get; set; } = string.Empty;
    public AttendanceStatus Status { get; set; }
    /// <summary>Delivered units for per-unit helpers (litres of milk, papers…).</summary>
    public decimal UnitsDelivered { get; set; }

    public DateTime ModifiedAtUtc { get; set; }
    public long RowVersion { get; set; }
    public bool IsDeleted { get; set; }
}

/// <summary>A money movement: advance, settlement payment, bonus or deduction.</summary>
public class LedgerEntryDto : ISyncEntity
{
    public Guid Id { get; set; }
    public Guid HelperId { get; set; }
    public LedgerEntryType Type { get; set; }
    public decimal Amount { get; set; }
    public PaymentMethod Method { get; set; }
    public string? Note { get; set; }
    /// <summary>Settlement period this entry belongs to, as yyyy-MM.</summary>
    public string Period { get; set; } = string.Empty;
    public DateTime OccurredAtUtc { get; set; }
    public string? UpiTransactionRef { get; set; }

    public DateTime ModifiedAtUtc { get; set; }
    public long RowVersion { get; set; }
    public bool IsDeleted { get; set; }
}

/// <summary>Per-helper, per-month settlement head; drives the salary-alert notifications.</summary>
public class SettlementDto : ISyncEntity
{
    public Guid Id { get; set; }
    public Guid HelperId { get; set; }
    public string Period { get; set; } = string.Empty; // yyyy-MM
    public SettlementStatus Status { get; set; }
    public decimal FinalPayable { get; set; }
    public DateTime? PaidAtUtc { get; set; }

    public DateTime ModifiedAtUtc { get; set; }
    public long RowVersion { get; set; }
    public bool IsDeleted { get; set; }
}
