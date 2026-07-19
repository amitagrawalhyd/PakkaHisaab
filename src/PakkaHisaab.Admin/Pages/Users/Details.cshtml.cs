using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using PakkaHisaab.Infrastructure.Data;
using PakkaHisaab.Shared.Enums;

namespace PakkaHisaab.Admin.Pages.Users;

public class DetailsModel : PageModel
{
    readonly AppDbContext _db;
    public DetailsModel(AppDbContext db) => _db = db;

    public User? Profile { get; set; }
    public List<Helper> Helpers { get; set; } = new();
    public decimal TotalAdvancesOutstanding { get; set; }
    public int PendingSettlementsCount { get; set; }

    public async Task<IActionResult> OnGetAsync(Guid id, CancellationToken ct)
    {
        Profile = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id, ct);
        if (Profile is null) return NotFound();

        Helpers = await _db.Helpers.AsNoTracking()
            .Where(h => h.UserId == id && !h.IsDeleted)
            .OrderBy(h => h.Name)
            .ToListAsync(ct);

        PendingSettlementsCount = await _db.Settlements.AsNoTracking()
            .CountAsync(s => s.UserId == id && !s.IsDeleted && s.Status == SettlementStatus.Pending, ct);

        // Materialized before summing: SQLite (local/self-host) can't apply SUM to a decimal
        // column server-side, only SQL Server can — this keeps both providers working.
        TotalAdvancesOutstanding = (await _db.LedgerEntries.AsNoTracking()
            .Where(l => l.UserId == id && !l.IsDeleted && l.Type == LedgerEntryType.Advance)
            .Select(l => l.Amount)
            .ToListAsync(ct)).Sum();

        return Page();
    }
}
