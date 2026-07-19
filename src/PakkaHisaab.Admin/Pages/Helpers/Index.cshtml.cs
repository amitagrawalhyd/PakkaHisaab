using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using PakkaHisaab.Infrastructure.Data;
using PakkaHisaab.Shared.Enums;

namespace PakkaHisaab.Admin.Pages.Helpers;

public class IndexModel : PageModel
{
    const int PageSize = 20;
    readonly AppDbContext _db;
    public IndexModel(AppDbContext db) => _db = db;

    [BindProperty(SupportsGet = true)] public string? Q { get; set; }
    [BindProperty(SupportsGet = true)] public HelperCategory? Category { get; set; }
    [BindProperty(SupportsGet = true)] public bool ActiveOnly { get; set; }
    [BindProperty(SupportsGet = true)] public new int Page { get; set; } = 1;

    public List<Row> Rows { get; set; } = new();
    public int TotalPages { get; set; }
    public IEnumerable<HelperCategory> Categories => Enum.GetValues<HelperCategory>();

    public string QueryString
    {
        get
        {
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(Q)) parts.Add($"q={Uri.EscapeDataString(Q)}");
            if (Category is not null) parts.Add($"category={Category}");
            if (ActiveOnly) parts.Add("activeOnly=true");
            return string.Join("&", parts);
        }
    }

    public record Row(Guid Id, string Name, string OwnerEmail, HelperCategory Category, WageType WageType,
        decimal MonthlyWage, decimal RatePerUnit, string UnitLabel, bool IsActive);

    public async Task OnGetAsync(CancellationToken ct)
    {
        var query =
            from h in _db.Helpers.AsNoTracking()
            join u in _db.Users.AsNoTracking() on h.UserId equals u.Id
            where !h.IsDeleted
            select new { h, u.Email };

        if (!string.IsNullOrWhiteSpace(Q))
        {
            var term = Q.Trim();
            query = query.Where(x => x.h.Name.Contains(term) || x.Email.Contains(term));
        }
        if (Category is not null) query = query.Where(x => x.h.Category == Category);
        if (ActiveOnly) query = query.Where(x => x.h.IsActive);

        var total = await query.CountAsync(ct);
        TotalPages = Math.Max(1, (int)Math.Ceiling(total / (double)PageSize));
        Page = Math.Clamp(Page, 1, TotalPages);

        Rows = await query.OrderBy(x => x.h.Name)
            .Skip((Page - 1) * PageSize).Take(PageSize)
            .Select(x => new Row(x.h.Id, x.h.Name, x.Email, x.h.Category, x.h.WageType,
                x.h.MonthlyWage, x.h.RatePerUnit, x.h.UnitLabel, x.h.IsActive))
            .ToListAsync(ct);
    }

    /// <summary>Tombstones the helper (IsDeleted = true) with a fresh RowVersion so the change
    /// replicates to the owner's device on next sync pull, exactly like a client-side delete.</summary>
    public async Task<IActionResult> OnPostRemoveAsync(Guid id)
    {
        var helper = await _db.Helpers.FindAsync(id);
        if (helper is null) return NotFound();

        helper.IsDeleted = true;
        helper.ModifiedAtUtc = DateTime.UtcNow;
        helper.RowVersion = await _db.NextRowVersionAsync();
        await _db.SaveChangesAsync();

        TempData["Flash"] = $"{helper.Name} removed.";
        return RedirectToPage(new { Q, Category, ActiveOnly, Page });
    }
}
