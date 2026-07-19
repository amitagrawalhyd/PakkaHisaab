using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using PakkaHisaab.Infrastructure.Data;
using PakkaHisaab.Shared.Enums;

namespace PakkaHisaab.Admin.Pages.Settlements;

public class IndexModel : PageModel
{
    const int PageSize = 25;
    readonly AppDbContext _db;
    public IndexModel(AppDbContext db) => _db = db;

    [BindProperty(SupportsGet = true)] public SettlementStatus? Status { get; set; }
    [BindProperty(SupportsGet = true)] public string? Period { get; set; }
    [BindProperty(SupportsGet = true)] public new int Page { get; set; } = 1;

    public List<Row> Rows { get; set; } = new();
    public int TotalPages { get; set; }
    public IEnumerable<SettlementStatus> Statuses => Enum.GetValues<SettlementStatus>();

    public string QueryString
    {
        get
        {
            var parts = new List<string>();
            if (Status is not null) parts.Add($"status={Status}");
            if (!string.IsNullOrWhiteSpace(Period)) parts.Add($"period={Period}");
            return string.Join("&", parts);
        }
    }

    public record Row(Guid Id, string HelperName, string OwnerEmail, string Period,
        SettlementStatus Status, decimal FinalPayable, DateTime? PaidAtUtc);

    public async Task OnGetAsync(CancellationToken ct)
    {
        var query =
            from s in _db.Settlements.AsNoTracking()
            join h in _db.Helpers.AsNoTracking() on s.HelperId equals h.Id
            join u in _db.Users.AsNoTracking() on s.UserId equals u.Id
            where !s.IsDeleted
            select new { s, h.Name, u.Email };

        if (Status is not null) query = query.Where(x => x.s.Status == Status);
        if (!string.IsNullOrWhiteSpace(Period)) query = query.Where(x => x.s.Period == Period);

        var total = await query.CountAsync(ct);
        TotalPages = Math.Max(1, (int)Math.Ceiling(total / (double)PageSize));
        Page = Math.Clamp(Page, 1, TotalPages);

        Rows = await query.OrderByDescending(x => x.s.Period)
            .Skip((Page - 1) * PageSize).Take(PageSize)
            .Select(x => new Row(x.s.Id, x.Name, x.Email, x.s.Period, x.s.Status, x.s.FinalPayable, x.s.PaidAtUtc))
            .ToListAsync(ct);
    }

    public async Task<IActionResult> OnPostToggleStatusAsync(Guid id)
    {
        var s = await _db.Settlements.FindAsync(id);
        if (s is null) return NotFound();

        s.Status = s.Status == SettlementStatus.Paid ? SettlementStatus.Pending : SettlementStatus.Paid;
        s.PaidAtUtc = s.Status == SettlementStatus.Paid ? DateTime.UtcNow : null;
        s.ModifiedAtUtc = DateTime.UtcNow;
        s.RowVersion = await _db.NextRowVersionAsync();
        await _db.SaveChangesAsync();

        TempData["Flash"] = $"Settlement marked {(s.Status == SettlementStatus.Paid ? "paid" : "pending")}.";
        return RedirectToPage(new { Status, Period, Page });
    }
}
