using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using PakkaHisaab.Infrastructure.Data;

namespace PakkaHisaab.Admin.Pages.Users;

public class IndexModel : PageModel
{
    const int PageSize = 20;
    readonly AppDbContext _db;
    public IndexModel(AppDbContext db) => _db = db;

    [BindProperty(SupportsGet = true)] public string? Q { get; set; }
    [BindProperty(SupportsGet = true)] public new int Page { get; set; } = 1;

    public List<UserRow> Rows { get; set; } = new();
    public int TotalPages { get; set; }
    public string QueryString => Q is null ? "" : $"q={Uri.EscapeDataString(Q)}";

    public record UserRow(Guid Id, string DisplayName, string Email, string? Phone, DateTime CreatedAtUtc,
        bool IsAdmin, int HelperCount);

    public async Task OnGetAsync(CancellationToken ct)
    {
        var query = _db.Users.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(Q))
        {
            var term = Q.Trim();
            query = query.Where(u => u.DisplayName.Contains(term) || u.Email.Contains(term));
        }

        var total = await query.CountAsync(ct);
        TotalPages = Math.Max(1, (int)Math.Ceiling(total / (double)PageSize));
        Page = Math.Clamp(Page, 1, TotalPages);

        var users = await query.OrderByDescending(u => u.CreatedAtUtc)
            .Skip((Page - 1) * PageSize).Take(PageSize)
            .ToListAsync(ct);

        var userIds = users.Select(u => u.Id).ToList();
        var helperCounts = await _db.Helpers.AsNoTracking()
            .Where(h => userIds.Contains(h.UserId) && !h.IsDeleted)
            .GroupBy(h => h.UserId)
            .Select(g => new { UserId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.UserId, x => x.Count, ct);

        Rows = users.Select(u => new UserRow(u.Id, u.DisplayName, u.Email, u.PhoneNumber, u.CreatedAtUtc,
            u.IsAdmin, helperCounts.GetValueOrDefault(u.Id))).ToList();
    }

    public async Task<IActionResult> OnPostToggleAdminAsync(Guid id)
    {
        var user = await _db.Users.FindAsync(id);
        if (user is null) return NotFound();

        var currentId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (user.Id.ToString() == currentId)
        {
            TempData["FlashError"] = "You cannot change your own admin access.";
            return RedirectToPage(new { Q, Page });
        }

        if (user.IsAdmin)
        {
            var otherAdmins = await _db.Users.CountAsync(u => u.IsAdmin && u.Id != id);
            if (otherAdmins == 0)
            {
                TempData["FlashError"] = "At least one admin account must remain.";
                return RedirectToPage(new { Q, Page });
            }
        }

        user.IsAdmin = !user.IsAdmin;
        await _db.SaveChangesAsync();
        TempData["Flash"] = $"{user.DisplayName} is {(user.IsAdmin ? "now" : "no longer")} an admin.";
        return RedirectToPage(new { Q, Page });
    }

    /// <summary>Hard-deletes the user and every replicated record — mirrors AccountEndpoints'
    /// "Delete My Account" cascade so admin-initiated deletes behave identically.</summary>
    public async Task<IActionResult> OnPostDeleteAsync(Guid id)
    {
        var user = await _db.Users.FindAsync(id);
        if (user is null) return NotFound();

        var currentId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (user.Id.ToString() == currentId)
        {
            TempData["FlashError"] = "You cannot delete your own account while signed in.";
            return RedirectToPage(new { Q, Page });
        }

        var strategy = _db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await _db.Database.BeginTransactionAsync();
            await _db.Helpers.Where(x => x.UserId == id).ExecuteDeleteAsync();
            await _db.Attendance.Where(x => x.UserId == id).ExecuteDeleteAsync();
            await _db.LedgerEntries.Where(x => x.UserId == id).ExecuteDeleteAsync();
            await _db.Settlements.Where(x => x.UserId == id).ExecuteDeleteAsync();
            await _db.SyncBatches.Where(x => x.UserId == id).ExecuteDeleteAsync();
            _db.Users.Remove(user);
            await _db.SaveChangesAsync();
            await tx.CommitAsync();
        });

        TempData["Flash"] = $"{user.DisplayName}'s account and all data were deleted.";
        return RedirectToPage(new { Q, Page });
    }
}
