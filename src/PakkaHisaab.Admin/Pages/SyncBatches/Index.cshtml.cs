using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using PakkaHisaab.Infrastructure.Data;

namespace PakkaHisaab.Admin.Pages.SyncBatches;

public class IndexModel : PageModel
{
    const int PageSize = 25;
    readonly AppDbContext _db;
    public IndexModel(AppDbContext db) => _db = db;

    [BindProperty(SupportsGet = true)] public string? Q { get; set; }
    [BindProperty(SupportsGet = true)] public new int Page { get; set; } = 1;

    public List<Row> Rows { get; set; } = new();
    public int TotalPages { get; set; }
    public string QueryString => Q is null ? "" : $"q={Uri.EscapeDataString(Q)}";

    public record Row(Guid ClientBatchId, string OwnerEmail, string DeviceId, DateTime ProcessedAtUtc, string ResponsePreview);

    public async Task OnGetAsync(CancellationToken ct)
    {
        var query =
            from b in _db.SyncBatches.AsNoTracking()
            join u in _db.Users.AsNoTracking() on b.UserId equals u.Id
            select new { b, u.Email };

        if (!string.IsNullOrWhiteSpace(Q))
        {
            var term = Q.Trim();
            query = query.Where(x => x.Email.Contains(term) || x.b.DeviceId.Contains(term));
        }

        var total = await query.CountAsync(ct);
        TotalPages = Math.Max(1, (int)Math.Ceiling(total / (double)PageSize));
        Page = Math.Clamp(Page, 1, TotalPages);

        var page = await query.OrderByDescending(x => x.b.ProcessedAtUtc)
            .Skip((Page - 1) * PageSize).Take(PageSize)
            .Select(x => new { x.b.ClientBatchId, x.Email, x.b.DeviceId, x.b.ProcessedAtUtc, x.b.ResponseJson })
            .ToListAsync(ct);

        Rows = page.Select(x => new Row(x.ClientBatchId, x.Email, x.DeviceId, x.ProcessedAtUtc,
            x.ResponseJson.Length > 160 ? x.ResponseJson[..160] + "…" : x.ResponseJson)).ToList();
    }
}
