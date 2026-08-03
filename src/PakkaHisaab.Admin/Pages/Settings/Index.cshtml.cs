using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using PakkaHisaab.Infrastructure.Data;

namespace PakkaHisaab.Admin.Pages.Settings;

public class IndexModel : PageModel
{
    readonly AppDbContext _db;
    public IndexModel(AppDbContext db) => _db = db;

    [BindProperty] public InputModel Input { get; set; } = new();

    public class InputModel
    {
        public bool Enabled { get; set; }
        public string Provider { get; set; } = "GoogleFree";
    }

    public async Task OnGetAsync(CancellationToken ct)
    {
        var row = await _db.TranslationSettingsRow.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == TranslationSettings.SingletonId, ct);
        Input = new InputModel { Enabled = row?.Enabled ?? false, Provider = row?.Provider ?? "GoogleFree" };
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        var row = await _db.TranslationSettingsRow.FirstOrDefaultAsync(x => x.Id == TranslationSettings.SingletonId, ct);
        if (row is null)
        {
            row = new TranslationSettings { Id = TranslationSettings.SingletonId };
            _db.TranslationSettingsRow.Add(row);
        }

        row.Enabled = Input.Enabled;
        row.Provider = Input.Provider is "GoogleCloud" ? "GoogleCloud" : "GoogleFree";
        await _db.SaveChangesAsync(ct);

        TempData["Flash"] = row.Enabled
            ? $"Automatic translation enabled ({(row.Provider == "GoogleCloud" ? "Google Cloud Translation" : "Free Google Translate")})."
            : "Automatic translation disabled.";
        return RedirectToPage();
    }
}
