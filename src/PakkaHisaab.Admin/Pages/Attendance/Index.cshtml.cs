using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using PakkaHisaab.Infrastructure.Data;
using PakkaHisaab.Shared.Enums;

namespace PakkaHisaab.Admin.Pages.Attendance;

public class IndexModel : PageModel
{
    const int PageSize = 25;
    readonly AppDbContext _db;
    public IndexModel(AppDbContext db) => _db = db;

    [BindProperty(SupportsGet = true)] public string? Q { get; set; }
    [BindProperty(SupportsGet = true)] public AttendanceStatus? Status { get; set; }
    [BindProperty(SupportsGet = true)] public string? From { get; set; }
    [BindProperty(SupportsGet = true)] public string? To { get; set; }
    [BindProperty(SupportsGet = true)] public new int Page { get; set; } = 1;

    public List<Row> Rows { get; set; } = new();
    public int TotalPages { get; set; }
    public IEnumerable<AttendanceStatus> Statuses => Enum.GetValues<AttendanceStatus>();

    public string QueryString
    {
        get
        {
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(Q)) parts.Add($"q={Uri.EscapeDataString(Q)}");
            if (Status is not null) parts.Add($"status={Status}");
            if (!string.IsNullOrWhiteSpace(From)) parts.Add($"from={From}");
            if (!string.IsNullOrWhiteSpace(To)) parts.Add($"to={To}");
            return string.Join("&", parts);
        }
    }

    public record Row(Guid Id, string Date, string HelperName, string OwnerEmail, AttendanceStatus Status, decimal UnitsDelivered);

    public async Task OnGetAsync(CancellationToken ct)
    {
        var query =
            from a in _db.Attendance.AsNoTracking()
            join h in _db.Helpers.AsNoTracking() on a.HelperId equals h.Id
            join u in _db.Users.AsNoTracking() on a.UserId equals u.Id
            where !a.IsDeleted
            select new { a, h.Name, u.Email };

        if (!string.IsNullOrWhiteSpace(Q))
        {
            var term = Q.Trim();
            query = query.Where(x => x.Name.Contains(term) || x.Email.Contains(term));
        }
        if (Status is not null) query = query.Where(x => x.a.Status == Status);
        if (!string.IsNullOrWhiteSpace(From)) query = query.Where(x => string.Compare(x.a.Date, From) >= 0);
        if (!string.IsNullOrWhiteSpace(To)) query = query.Where(x => string.Compare(x.a.Date, To) <= 0);

        var total = await query.CountAsync(ct);
        TotalPages = Math.Max(1, (int)Math.Ceiling(total / (double)PageSize));
        Page = Math.Clamp(Page, 1, TotalPages);

        Rows = await query.OrderByDescending(x => x.a.Date)
            .Skip((Page - 1) * PageSize).Take(PageSize)
            .Select(x => new Row(x.a.Id, x.a.Date, x.Name, x.Email, x.a.Status, x.a.UnitsDelivered))
            .ToListAsync(ct);
    }

    public async Task<IActionResult> OnPostRemoveAsync(Guid id)
    {
        var entry = await _db.Attendance.FindAsync(id);
        if (entry is null) return NotFound();

        entry.IsDeleted = true;
        entry.ModifiedAtUtc = DateTime.UtcNow;
        entry.RowVersion = await _db.NextRowVersionAsync();
        await _db.SaveChangesAsync();

        TempData["Flash"] = "Attendance entry removed.";
        return RedirectToPage(new { Q, Status, From, To, Page });
    }
}
