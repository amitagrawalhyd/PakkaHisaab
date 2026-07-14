using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using PakkaHisaab.Shared.Enums;

namespace PakkaHisaab.Api.Data;

public class User
{
    [Key] public Guid Id { get; set; }
    [MaxLength(256)] public string Email { get; set; } = string.Empty;
    [MaxLength(128)] public string DisplayName { get; set; } = string.Empty;
    [MaxLength(32)] public string? PhoneNumber { get; set; }
    public string PasswordHash { get; set; } = string.Empty; // PBKDF2, see PasswordHasher
    public DateTime CreatedAtUtc { get; set; }
    [MaxLength(512)] public string? RefreshToken { get; set; }
    public DateTime? RefreshTokenExpiresAtUtc { get; set; }
}

/// <summary>Server-side base for replicated entities. RowVersion is a global monotonic
/// change counter (sequence) used as the incremental sync watermark.</summary>
public abstract class SyncEntityBase
{
    [Key] public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public DateTime ModifiedAtUtc { get; set; }
    public long RowVersion { get; set; }
    public bool IsDeleted { get; set; }
}

public class Helper : SyncEntityBase
{
    [MaxLength(128)] public string Name { get; set; } = string.Empty;
    [MaxLength(32)] public string WhatsAppNumber { get; set; } = string.Empty;
    [MaxLength(128)] public string? UpiId { get; set; }
    public HelperCategory Category { get; set; }
    public WageType WageType { get; set; }
    [Column(TypeName = "decimal(12,2)")] public decimal MonthlyWage { get; set; }
    [Column(TypeName = "decimal(10,2)")] public decimal RatePerUnit { get; set; }
    [MaxLength(16)] public string UnitLabel { get; set; } = "L";
    public int MonthlyAllowedAbsences { get; set; }
    public bool CarryOverLeaveAllowed { get; set; }
    public int CarriedOverLeaves { get; set; }
    public bool IsActive { get; set; } = true;
}

public class AttendanceEntry : SyncEntityBase
{
    public Guid HelperId { get; set; }
    [MaxLength(10)] public string Date { get; set; } = string.Empty; // yyyy-MM-dd
    public AttendanceStatus Status { get; set; }
    [Column(TypeName = "decimal(10,2)")] public decimal UnitsDelivered { get; set; }
}

public class LedgerEntry : SyncEntityBase
{
    public Guid HelperId { get; set; }
    public LedgerEntryType Type { get; set; }
    [Column(TypeName = "decimal(12,2)")] public decimal Amount { get; set; }
    public PaymentMethod Method { get; set; }
    [MaxLength(512)] public string? Note { get; set; }
    [MaxLength(7)] public string Period { get; set; } = string.Empty; // yyyy-MM
    public DateTime OccurredAtUtc { get; set; }
    [MaxLength(64)] public string? UpiTransactionRef { get; set; }
}

public class Settlement : SyncEntityBase
{
    public Guid HelperId { get; set; }
    [MaxLength(7)] public string Period { get; set; } = string.Empty;
    public SettlementStatus Status { get; set; }
    [Column(TypeName = "decimal(12,2)")] public decimal FinalPayable { get; set; }
    public DateTime? PaidAtUtc { get; set; }
}

/// <summary>
/// Fallback row-version source for providers without sequences (SQLite).
/// Each inserted row's auto-increment Id is one monotonic version number.
/// </summary>
public class RowVersionTicket
{
    [Key] public long Id { get; set; }
}

/// <summary>Idempotency ledger: one row per processed sync push batch.</summary>
public class SyncBatch
{
    [Key] public Guid ClientBatchId { get; set; }
    public Guid UserId { get; set; }
    [MaxLength(64)] public string DeviceId { get; set; } = string.Empty;
    public DateTime ProcessedAtUtc { get; set; }
    /// <summary>Serialized SyncPushResponse, replayed verbatim on duplicate delivery.</summary>
    public string ResponseJson { get; set; } = string.Empty;
}
