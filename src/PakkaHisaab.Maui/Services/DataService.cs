using PakkaHisaab.Maui.Data;
using PakkaHisaab.Maui.Models;
using PakkaHisaab.Shared.Domain;
using PakkaHisaab.Shared.Dtos;
using PakkaHisaab.Shared.Enums;

namespace PakkaHisaab.Maui.Services;

public interface IDataService
{
    Task<List<HelperDto>> GetHelpersAsync(bool includeInactive = false);
    Task<HelperDto?> GetHelperAsync(Guid id);
    Task SaveHelperAsync(HelperDto helper);
    Task DeleteHelperAsync(Guid id);

    Task<List<AttendanceDto>> GetAttendanceAsync(Guid helperId, int year, int month);
    Task<List<AttendanceDto>> GetAttendanceHistoryAsync(Guid helperId);
    Task<AttendanceDto> ToggleAttendanceAsync(Guid helperId, DateOnly date);
    Task SetUnitsAsync(Guid helperId, DateOnly date, decimal units);
    Task SetAttendanceAsync(Guid helperId, DateOnly date, AttendanceStatus status);

    Task<List<LedgerEntryDto>> GetLedgerAsync(Guid helperId, string period);
    Task AddLedgerEntryAsync(LedgerEntryDto entry);

    Task<SettlementBreakdown> ComputeSettlementAsync(Guid helperId, int year, int month);
    Task<SettlementDto> MarkPaidAsync(Guid helperId, string period, decimal amount,
        PaymentMethod method, string? upiRef);
}

/// <summary>
/// The single write path for all business data. Every mutation:
///   1) writes to SQLite immediately (zero-latency, offline-first UI),
///   2) stamps ModifiedAtUtc + IsDirty for the outbox,
///   3) nudges the Shiny sync engine (a no-op in demo mode),
///   4) keeps local notifications consistent (e.g., cancels salary alerts when paid).
/// </summary>
public sealed class DataService : IDataService
{
    readonly ILocalDatabase _db;
    readonly ISyncEngine _sync;
    readonly INotificationService _notifications;

    public DataService(ILocalDatabase db, ISyncEngine sync, INotificationService notifications)
    {
        _db = db;
        _sync = sync;
        _notifications = notifications;
    }

    // ---------- Helpers ----------

    public async Task<List<HelperDto>> GetHelpersAsync(bool includeInactive = false)
    {
        var conn = await _db.GetConnectionAsync();
        var rows = await conn.Table<LocalHelper>()
            .Where(h => !h.IsDeleted && (includeInactive || h.IsActive))
            .OrderBy(h => h.Name)
            .ToListAsync();
        return rows.Select(r => r.ToDto()).ToList();
    }

    public async Task<HelperDto?> GetHelperAsync(Guid id)
    {
        var conn = await _db.GetConnectionAsync();
        var row = await conn.FindAsync<LocalHelper>(id);
        return row is null || row.IsDeleted ? null : row.ToDto();
    }

    public async Task SaveHelperAsync(HelperDto helper)
    {
        var conn = await _db.GetConnectionAsync();
        if (helper.Id == Guid.Empty) helper.Id = Guid.NewGuid();
        helper.ModifiedAtUtc = DateTime.UtcNow;
        await conn.InsertOrReplaceAsync(helper.ToLocal(dirty: true));
        await _notifications.ScheduleDailyAttendanceReminderAsync(helper);
        await _sync.RequestSyncAsync();
    }

    public async Task DeleteHelperAsync(Guid id)
    {
        var conn = await _db.GetConnectionAsync();
        var row = await conn.FindAsync<LocalHelper>(id);
        if (row is null) return;
        row.IsDeleted = true;
        row.IsDirty = true;
        row.ModifiedAtUtc = DateTime.UtcNow;
        await conn.UpdateAsync(row);
        await _notifications.CancelForHelperAsync(id);
        await _sync.RequestSyncAsync();
    }

    // ---------- Attendance (2-tap calendar) ----------

    public async Task<List<AttendanceDto>> GetAttendanceAsync(Guid helperId, int year, int month)
    {
        var prefix = $"{year:D4}-{month:D2}-";
        var conn = await _db.GetConnectionAsync();
        var rows = await conn.Table<LocalAttendance>()
            .Where(a => a.HelperId == helperId && !a.IsDeleted && a.Date.StartsWith(prefix))
            .ToListAsync();
        return rows.Select(r => r.ToDto()).ToList();
    }

    public async Task<List<AttendanceDto>> GetAttendanceHistoryAsync(Guid helperId)
    {
        var conn = await _db.GetConnectionAsync();
        var rows = await conn.Table<LocalAttendance>()
            .Where(a => a.HelperId == helperId && !a.IsDeleted)
            .ToListAsync();
        return rows.Select(r => r.ToDto()).ToList();
    }

    /// <summary>One tap cycles Present → Absent → Half-Day → Present.</summary>
    public async Task<AttendanceDto> ToggleAttendanceAsync(Guid helperId, DateOnly date)
    {
        var row = await GetOrCreateDayAsync(helperId, date);
        row.Status = row.Status switch
        {
            AttendanceStatus.Present => AttendanceStatus.Absent,
            AttendanceStatus.Absent => AttendanceStatus.HalfDay,
            _ => AttendanceStatus.Present
        };
        await PersistDayAsync(row);
        return row.ToDto();
    }

    public async Task SetAttendanceAsync(Guid helperId, DateOnly date, AttendanceStatus status)
    {
        var row = await GetOrCreateDayAsync(helperId, date);
        row.Status = status;
        await PersistDayAsync(row);
    }

    public async Task SetUnitsAsync(Guid helperId, DateOnly date, decimal units)
    {
        var row = await GetOrCreateDayAsync(helperId, date);
        row.UnitsDelivered = units;
        row.Status = units > 0 ? AttendanceStatus.Present : AttendanceStatus.Absent;
        await PersistDayAsync(row);
    }

    async Task<LocalAttendance> GetOrCreateDayAsync(Guid helperId, DateOnly date)
    {
        var key = date.ToString("yyyy-MM-dd");
        var conn = await _db.GetConnectionAsync();
        var row = await conn.Table<LocalAttendance>()
            .Where(a => a.HelperId == helperId && a.Date == key)
            .FirstOrDefaultAsync();
        return row ?? new LocalAttendance
        {
            Id = Guid.NewGuid(), HelperId = helperId, Date = key,
            Status = AttendanceStatus.Present
        };
    }

    async Task PersistDayAsync(LocalAttendance row)
    {
        row.IsDeleted = false;
        row.IsDirty = true;
        row.ModifiedAtUtc = DateTime.UtcNow;
        var conn = await _db.GetConnectionAsync();
        await conn.InsertOrReplaceAsync(row);
        await _sync.RequestSyncAsync();
    }

    // ---------- Ledger ----------

    public async Task<List<LedgerEntryDto>> GetLedgerAsync(Guid helperId, string period)
    {
        var conn = await _db.GetConnectionAsync();
        var rows = await conn.Table<LocalLedgerEntry>()
            .Where(l => l.HelperId == helperId && l.Period == period && !l.IsDeleted)
            .OrderByDescending(l => l.OccurredAtUtc)
            .ToListAsync();
        return rows.Select(r => r.ToDto()).ToList();
    }

    public async Task AddLedgerEntryAsync(LedgerEntryDto entry)
    {
        if (entry.Id == Guid.Empty) entry.Id = Guid.NewGuid();
        if (entry.OccurredAtUtc == default) entry.OccurredAtUtc = DateTime.UtcNow;
        if (string.IsNullOrEmpty(entry.Period)) entry.Period = DateTime.Today.ToString("yyyy-MM");
        entry.ModifiedAtUtc = DateTime.UtcNow;

        var conn = await _db.GetConnectionAsync();
        await conn.InsertOrReplaceAsync(entry.ToLocal(dirty: true));
        await _sync.RequestSyncAsync();
    }

    // ---------- Settlement ----------

    public async Task<SettlementBreakdown> ComputeSettlementAsync(Guid helperId, int year, int month)
    {
        var helper = await GetHelperAsync(helperId)
                     ?? throw new InvalidOperationException("Helper not found");
        var attendance = await GetAttendanceAsync(helperId, year, month);
        var ledger = await GetLedgerAsync(helperId, $"{year:D4}-{month:D2}");
        // Shared engine — the exact same code the API uses to verify totals.
        return SalaryCalculator.Compute(helper, year, month, attendance, ledger);
    }

    public async Task<SettlementDto> MarkPaidAsync(Guid helperId, string period, decimal amount,
        PaymentMethod method, string? upiRef)
    {
        await AddLedgerEntryAsync(new LedgerEntryDto
        {
            HelperId = helperId, Type = LedgerEntryType.SalaryPayment, Amount = amount,
            Method = method, Period = period, UpiTransactionRef = upiRef
        });

        var conn = await _db.GetConnectionAsync();
        var settlement = await conn.Table<LocalSettlement>()
            .Where(s => s.HelperId == helperId && s.Period == period)
            .FirstOrDefaultAsync() ?? new LocalSettlement
            {
                Id = Guid.NewGuid(), HelperId = helperId, Period = period
            };

        settlement.Status = SettlementStatus.Paid;
        settlement.FinalPayable = amount;
        settlement.PaidAtUtc = DateTime.UtcNow;
        settlement.IsDirty = true;
        settlement.ModifiedAtUtc = DateTime.UtcNow;
        await conn.InsertOrReplaceAsync(settlement);

        // Paid → stop nagging: cancel the 1st–10th salary alert for this helper.
        await _notifications.CancelSalaryAlertAsync(helperId);
        await _sync.RequestSyncAsync();
        return settlement.ToDto();
    }
}
