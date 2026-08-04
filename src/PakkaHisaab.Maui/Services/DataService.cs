using PakkaHisaab.Maui.Data;
using PakkaHisaab.Maui.Models;
using PakkaHisaab.Shared.Domain;
using PakkaHisaab.Shared.Dtos;
using PakkaHisaab.Shared.Enums;
using SQLite;

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
    Task DeleteLedgerEntryAsync(Guid id);

    Task<SettlementBreakdown> ComputeSettlementAsync(Guid helperId, int year, int month);
    Task<SettlementDto?> GetSettlementAsync(Guid helperId, string period);
    Task<SettlementDto> MarkPaidAsync(Guid helperId, string period, decimal amount,
        PaymentMethod method, string? upiRef);
    /// <summary>Past months (before the current one) that still have money owed and no
    /// recorded payment — the arrears list behind the Dashboard's "N months pending" badge.
    /// Bounded to <paramref name="maxLookbackMonths"/> and to the helper's earliest activity.</summary>
    Task<List<PendingSettlement>> GetUnpaidPeriodsAsync(Guid helperId, int maxLookbackMonths = 24);
}

/// <summary>One unpaid past month, as surfaced to the Dashboard/arrears UI.</summary>
public sealed record PendingSettlement(int Year, int Month, string Period, SettlementBreakdown Breakdown);

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

        var now = DateTime.UtcNow;
        row.IsDeleted = true;
        row.IsDirty = true;
        row.ModifiedAtUtc = now;
        await conn.UpdateAsync(row);

        // Cascade soft-delete so dependent records don't linger as orphans / keep syncing as live data.
        await SoftDeleteAllAsync<LocalAttendance>(conn, a => a.HelperId == id, now);
        await SoftDeleteAllAsync<LocalLedgerEntry>(conn, l => l.HelperId == id, now);
        await SoftDeleteAllAsync<LocalSettlement>(conn, s => s.HelperId == id, now);

        await _notifications.CancelForHelperAsync(id);
        await _sync.RequestSyncAsync();
    }

    static async Task SoftDeleteAllAsync<T>(SQLiteAsyncConnection conn,
        System.Linq.Expressions.Expression<Func<T, bool>> predicate, DateTime now)
        where T : LocalEntityBase, new()
    {
        var rows = await conn.Table<T>().Where(predicate).ToListAsync();
        foreach (var row in rows.Where(r => !r.IsDeleted))
        {
            row.IsDeleted = true;
            row.IsDirty = true;
            row.ModifiedAtUtc = now;
            await conn.UpdateAsync(row);
        }
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

    public async Task DeleteLedgerEntryAsync(Guid id)
    {
        var conn = await _db.GetConnectionAsync();
        var row = await conn.FindAsync<LocalLedgerEntry>(id);
        if (row is null) return;

        row.IsDeleted = true;
        row.IsDirty = true;
        row.ModifiedAtUtc = DateTime.UtcNow;
        await conn.UpdateAsync(row);

        // Deleting the payment that settled a month must un-settle it — otherwise
        // SalaryCalculator correctly shows money owed again, but the separate
        // LocalSettlement.Status flag stays "Paid" forever, so the Settlement screen keeps
        // showing "already settled" and blocks re-paying the very month that's now unpaid.
        if (row.Type == LedgerEntryType.SalaryPayment)
            await RevertSettlementIfUnpaidAsync(conn, row.HelperId, row.Period);

        await _sync.RequestSyncAsync();
    }

    static async Task RevertSettlementIfUnpaidAsync(SQLiteAsyncConnection conn, Guid helperId, string period)
    {
        int remainingPayments = await conn.Table<LocalLedgerEntry>()
            .Where(l => l.HelperId == helperId && l.Period == period
                        && l.Type == LedgerEntryType.SalaryPayment && !l.IsDeleted)
            .CountAsync();
        if (remainingPayments > 0) return; // another payment still covers this period

        var settlement = await conn.Table<LocalSettlement>()
            .Where(s => s.HelperId == helperId && s.Period == period && !s.IsDeleted)
            .FirstOrDefaultAsync();
        if (settlement is null || settlement.Status != SettlementStatus.Paid) return;

        settlement.Status = SettlementStatus.Pending;
        settlement.PaidAtUtc = null;
        settlement.IsDirty = true;
        settlement.ModifiedAtUtc = DateTime.UtcNow;
        await conn.UpdateAsync(settlement);
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

    public async Task<List<PendingSettlement>> GetUnpaidPeriodsAsync(Guid helperId, int maxLookbackMonths = 24)
    {
        var conn = await _db.GetConnectionAsync();

        var earliestAttendance = await conn.Table<LocalAttendance>()
            .Where(a => a.HelperId == helperId && !a.IsDeleted)
            .OrderBy(a => a.Date).FirstOrDefaultAsync();
        var earliestLedger = await conn.Table<LocalLedgerEntry>()
            .Where(l => l.HelperId == helperId && !l.IsDeleted)
            .OrderBy(l => l.Period).FirstOrDefaultAsync();

        DateTime? earliestActivity = null;
        if (earliestAttendance is not null)
            earliestActivity = new DateTime(int.Parse(earliestAttendance.Date[..4]), int.Parse(earliestAttendance.Date[5..7]), 1);
        if (earliestLedger is not null)
        {
            var ledgerMonth = new DateTime(int.Parse(earliestLedger.Period[..4]), int.Parse(earliestLedger.Period[5..7]), 1);
            if (earliestActivity is null || ledgerMonth < earliestActivity) earliestActivity = ledgerMonth;
        }

        var today = DateTime.Today;
        var candidates = SettlementPeriodPlanner.EnumerateBackPeriods(
            today.Year, today.Month, earliestActivity, maxLookbackMonths);
        if (candidates.Count == 0) return [];

        var paidPeriods = (await conn.Table<LocalSettlement>()
                .Where(s => s.HelperId == helperId && s.Status == SettlementStatus.Paid && !s.IsDeleted)
                .ToListAsync())
            .Select(s => s.Period).ToHashSet();

        var results = new List<PendingSettlement>();
        foreach (var (year, month) in candidates)
        {
            var period = $"{year:D4}-{month:D2}";
            if (paidPeriods.Contains(period)) continue;

            var breakdown = await ComputeSettlementAsync(helperId, year, month);
            if (breakdown.FinalPayable > 0)
                results.Add(new PendingSettlement(year, month, period, breakdown));
        }
        return results;
    }

    public async Task<SettlementDto?> GetSettlementAsync(Guid helperId, string period)
    {
        var conn = await _db.GetConnectionAsync();
        var row = await conn.Table<LocalSettlement>()
            .Where(s => s.HelperId == helperId && s.Period == period && !s.IsDeleted)
            .FirstOrDefaultAsync();
        return row?.ToDto();
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

        // Roll the unused leave allowance forward onto the helper record — otherwise
        // CarryOverLeaveAllowed helpers would keep the same balance forever.
        var periodParts = period.Split('-');
        if (periodParts.Length == 2
            && int.TryParse(periodParts[0], out var year)
            && int.TryParse(periodParts[1], out var month))
        {
            var helperRow = await conn.FindAsync<LocalHelper>(helperId);
            if (helperRow is not null && helperRow.CarryOverLeaveAllowed)
            {
                // Settling months out of order (Calendar now allows picking any past month) must
                // not let an older arrear's numbers clobber a more recent month's already-applied
                // carry-forward — only the latest Paid period may write the balance. "yyyy-MM"
                // periods sort correctly as plain strings.
                var paidPeriods = await conn.Table<LocalSettlement>()
                    .Where(s => s.HelperId == helperId && s.Status == SettlementStatus.Paid && !s.IsDeleted)
                    .ToListAsync();
                // paidPeriods always contains at least the row just written above in the normal
                // case, but must not assume that — Max() on an empty sequence throws, which would
                // otherwise abort MarkPaidAsync entirely and strand the caller mid-settlement.
                var latestPaidPeriod = paidPeriods.Count > 0 ? paidPeriods.Max(s => s.Period) : period;

                if (string.CompareOrdinal(period, latestPaidPeriod) >= 0)
                {
                    var breakdown = await ComputeSettlementAsync(helperId, year, month);
                    helperRow.CarriedOverLeaves = breakdown.LeavesToCarryForward;
                    helperRow.IsDirty = true;
                    helperRow.ModifiedAtUtc = DateTime.UtcNow;
                    await conn.UpdateAsync(helperRow);
                }
            }
        }

        // Paid → stop nagging: cancel the 1st–10th salary alert for this helper.
        await _notifications.CancelSalaryAlertAsync(helperId);
        await _sync.RequestSyncAsync();
        return settlement.ToDto();
    }
}
