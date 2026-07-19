using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using PakkaHisaab.Infrastructure.Data;
using PakkaHisaab.Shared.Enums;

namespace PakkaHisaab.Admin.Pages.Helpers;

public class EditModel : PageModel
{
    readonly AppDbContext _db;
    public EditModel(AppDbContext db) => _db = db;

    [BindProperty] public InputModel Input { get; set; } = new();
    public string OwnerEmail { get; set; } = "";
    public IEnumerable<HelperCategory> Categories => Enum.GetValues<HelperCategory>();
    public IEnumerable<WageType> WageTypes => Enum.GetValues<WageType>();

    public class InputModel
    {
        public Guid Id { get; set; }
        [Required, MaxLength(128)] public string Name { get; set; } = "";
        [MaxLength(32)] public string WhatsAppNumber { get; set; } = "";
        [MaxLength(128)] public string? UpiId { get; set; }
        public HelperCategory Category { get; set; }
        public WageType WageType { get; set; }
        [Range(0, 10_000_000)] public decimal MonthlyWage { get; set; }
        [Range(0, 100_000)] public decimal RatePerUnit { get; set; }
        [MaxLength(16)] public string UnitLabel { get; set; } = "L";
        [Range(0, 31)] public int MonthlyAllowedAbsences { get; set; }
        public bool CarryOverLeaveAllowed { get; set; }
        public bool IsActive { get; set; }
    }

    public async Task<IActionResult> OnGetAsync(Guid id, CancellationToken ct)
    {
        var h = await _db.Helpers.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (h is null) return NotFound();

        OwnerEmail = (await _db.Users.AsNoTracking().Where(u => u.Id == h.UserId)
            .Select(u => u.Email).FirstOrDefaultAsync(ct)) ?? "—";

        Input = new InputModel
        {
            Id = h.Id, Name = h.Name, WhatsAppNumber = h.WhatsAppNumber, UpiId = h.UpiId,
            Category = h.Category, WageType = h.WageType, MonthlyWage = h.MonthlyWage,
            RatePerUnit = h.RatePerUnit, UnitLabel = h.UnitLabel,
            MonthlyAllowedAbsences = h.MonthlyAllowedAbsences,
            CarryOverLeaveAllowed = h.CarryOverLeaveAllowed, IsActive = h.IsActive
        };
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        var h = await _db.Helpers.FirstOrDefaultAsync(x => x.Id == Input.Id, ct);
        if (h is null) return NotFound();

        if (!ModelState.IsValid)
        {
            OwnerEmail = (await _db.Users.AsNoTracking().Where(u => u.Id == h.UserId)
                .Select(u => u.Email).FirstOrDefaultAsync(ct)) ?? "—";
            return Page();
        }

        h.Name = Input.Name.Trim();
        h.WhatsAppNumber = Input.WhatsAppNumber.Trim();
        h.UpiId = Input.UpiId;
        h.Category = Input.Category;
        h.WageType = Input.WageType;
        h.MonthlyWage = Input.MonthlyWage;
        h.RatePerUnit = Input.RatePerUnit;
        h.UnitLabel = Input.UnitLabel;
        h.MonthlyAllowedAbsences = Input.MonthlyAllowedAbsences;
        h.CarryOverLeaveAllowed = Input.CarryOverLeaveAllowed;
        h.IsActive = Input.IsActive;

        // Fresh watermark so this admin edit replicates to the owner's device (last-writer-wins sync).
        h.ModifiedAtUtc = DateTime.UtcNow;
        h.RowVersion = await _db.NextRowVersionAsync(ct);
        await _db.SaveChangesAsync(ct);

        TempData["Flash"] = $"{h.Name} updated.";
        return RedirectToPage("/Helpers/Index");
    }
}
