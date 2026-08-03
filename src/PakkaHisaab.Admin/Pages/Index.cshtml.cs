using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using PakkaHisaab.Infrastructure.Data;
using PakkaHisaab.Shared.Enums;

namespace PakkaHisaab.Admin.Pages;

public class IndexModel : PageModel
{
    readonly AppDbContext _db;
    public IndexModel(AppDbContext db) => _db = db;

    public int TotalUsers { get; set; }
    public int TotalHelpers { get; set; }
    public int ActiveHelpers { get; set; }
    public int PendingSettlements { get; set; }
    public decimal PendingSettlementsAmount { get; set; }
    public decimal PaidThisMonthAmount { get; set; }
    public int SyncBatchesLast7Days { get; set; }

    // Chart data
    public List<string> UserGrowthLabels { get; set; } = new();
    public List<int> UserGrowthValues { get; set; } = new();

    public List<string> AttendanceLabels { get; set; } = new();
    public List<int> AttendanceValues { get; set; } = new();

    public List<string> LedgerLabels { get; set; } = new();
    public List<decimal> LedgerValues { get; set; } = new();

    public List<string> SettlementLabels { get; set; } = new() { "Pending", "Paid" };
    public List<decimal> SettlementValues { get; set; } = new();

    public List<(string Name, string Email, DateTime CreatedAtUtc)> RecentUsers { get; set; } = new();

    public async Task OnGetAsync(CancellationToken ct)
    {
        TotalUsers = await _db.Users.CountAsync(ct);
        TotalHelpers = await _db.Helpers.CountAsync(x => !x.IsDeleted, ct);
        ActiveHelpers = await _db.Helpers.CountAsync(x => !x.IsDeleted && x.IsActive, ct);

        var pending = await _db.Settlements
            .Where(x => !x.IsDeleted && x.Status == SettlementStatus.Pending)
            .ToListAsync(ct);
        PendingSettlements = pending.Count;
        PendingSettlementsAmount = pending.Sum(x => x.FinalPayable);

        // Materialized before summing: SQLite (local/self-host) can't apply SUM to a decimal
        // column server-side, only SQL Server can — this keeps both providers working.
        var monthStart = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1);
        PaidThisMonthAmount = (await _db.Settlements
            .Where(x => !x.IsDeleted && x.Status == SettlementStatus.Paid && x.PaidAtUtc >= monthStart)
            .Select(x => x.FinalPayable)
            .ToListAsync(ct)).Sum();

        var since7 = DateTime.UtcNow.AddDays(-7);
        SyncBatchesLast7Days = await _db.SyncBatches.CountAsync(x => x.ProcessedAtUtc >= since7, ct);

        // ---- Users signed up per month, last 6 months ----
        var sixMonthsAgo = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1).AddMonths(-5);
        var users = await _db.Users
            .Where(u => u.CreatedAtUtc >= sixMonthsAgo)
            .Select(u => u.CreatedAtUtc)
            .ToListAsync(ct);
        for (int i = 0; i < 6; i++)
        {
            var bucket = sixMonthsAgo.AddMonths(i);
            UserGrowthLabels.Add(bucket.ToString("MMM"));
            UserGrowthValues.Add(users.Count(d => d.Year == bucket.Year && d.Month == bucket.Month));
        }

        // ---- Attendance breakdown, last 30 days ----
        var since30 = DateTime.UtcNow.AddDays(-30).ToString("yyyy-MM-dd");
        var attendance = await _db.Attendance
            .Where(a => !a.IsDeleted && string.Compare(a.Date, since30) >= 0)
            .GroupBy(a => a.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(ct);
        foreach (AttendanceStatus s in Enum.GetValues<AttendanceStatus>())
        {
            AttendanceLabels.Add(s.ToString());
            AttendanceValues.Add(attendance.FirstOrDefault(x => x.Status == s)?.Count ?? 0);
        }

        // ---- Ledger totals by type, this month ----
        var thisMonthPeriod = DateTime.UtcNow.ToString("yyyy-MM");
        var ledger = (await _db.LedgerEntries
                .Where(l => !l.IsDeleted && l.Period == thisMonthPeriod)
                .Select(l => new { l.Type, l.Amount })
                .ToListAsync(ct))
            .GroupBy(l => l.Type)
            .Select(g => new { Type = g.Key, Total = g.Sum(x => x.Amount) })
            .ToList();
        foreach (LedgerEntryType t in Enum.GetValues<LedgerEntryType>())
        {
            LedgerLabels.Add(t.ToString());
            LedgerValues.Add(ledger.FirstOrDefault(x => x.Type == t)?.Total ?? 0);
        }

        SettlementValues = new List<decimal> { PendingSettlementsAmount, PaidThisMonthAmount };

        var recent = await _db.Users
            .OrderByDescending(u => u.CreatedAtUtc)
            .Take(5)
            .Select(u => new { u.DisplayName, u.Email, u.CreatedAtUtc })
            .ToListAsync(ct);
        RecentUsers = recent.Select(u => (u.DisplayName, u.Email, u.CreatedAtUtc)).ToList();
    }
}
