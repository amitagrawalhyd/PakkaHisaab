using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using PakkaHisaab.Infrastructure.Data;
using PakkaHisaab.Shared.Enums;

namespace PakkaHisaab.Admin.Pages.Ledger;

public class IndexModel : PageModel
{
    const int PageSize = 25;
    readonly AppDbContext _db;
    public IndexModel(AppDbContext db) => _db = db;

    [BindProperty(SupportsGet = true)] public string? Q { get; set; }
    [BindProperty(SupportsGet = true)] public LedgerEntryType? Type { get; set; }
    [BindProperty(SupportsGet = true)] public string? Period { get; set; }
    [BindProperty(SupportsGet = true)] public new int Page { get; set; } = 1;

    public List<Row> Rows { get; set; } = new();
    public int TotalPages { get; set; }
    public IEnumerable<LedgerEntryType> Types => Enum.GetValues<LedgerEntryType>();

    public string QueryString
    {
        get
        {
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(Q)) parts.Add($"q={Uri.EscapeDataString(Q)}");
            if (Type is not null) parts.Add($"type={Type}");
            if (!string.IsNullOrWhiteSpace(Period)) parts.Add($"period={Period}");
            return string.Join("&", parts);
        }
    }

    public record Row(Guid Id, DateTime OccurredAtUtc, string HelperName, string OwnerEmail,
        LedgerEntryType Type, decimal Amount, PaymentMethod Method, string? Note, string Period);

    public async Task OnGetAsync(CancellationToken ct)
    {
        var query =
            from l in _db.LedgerEntries.AsNoTracking()
            join h in _db.Helpers.AsNoTracking() on l.HelperId equals h.Id
            join u in _db.Users.AsNoTracking() on l.UserId equals u.Id
            where !l.IsDeleted
            select new { l, h.Name, u.Email };

        if (!string.IsNullOrWhiteSpace(Q))
        {
            var term = Q.Trim();
            query = query.Where(x => x.Name.Contains(term) || x.Email.Contains(term));
        }
        if (Type is not null) query = query.Where(x => x.l.Type == Type);
        if (!string.IsNullOrWhiteSpace(Period)) query = query.Where(x => x.l.Period == Period);

        var total = await query.CountAsync(ct);
        TotalPages = Math.Max(1, (int)Math.Ceiling(total / (double)PageSize));
        Page = Math.Clamp(Page, 1, TotalPages);

        Rows = await query.OrderByDescending(x => x.l.OccurredAtUtc)
            .Skip((Page - 1) * PageSize).Take(PageSize)
            .Select(x => new Row(x.l.Id, x.l.OccurredAtUtc, x.Name, x.Email, x.l.Type, x.l.Amount, x.l.Method, x.l.Note, x.l.Period))
            .ToListAsync(ct);
    }

    public async Task<IActionResult> OnPostRemoveAsync(Guid id)
    {
        var entry = await _db.LedgerEntries.FindAsync(id);
        if (entry is null) return NotFound();

        entry.IsDeleted = true;
        entry.ModifiedAtUtc = DateTime.UtcNow;
        entry.RowVersion = await _db.NextRowVersionAsync();
        await _db.SaveChangesAsync();

        TempData["Flash"] = "Ledger entry removed.";
        return RedirectToPage(new { Q, Type, Period, Page });
    }
}
