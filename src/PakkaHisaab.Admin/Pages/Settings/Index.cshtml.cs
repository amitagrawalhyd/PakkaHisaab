using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using PakkaHisaab.Infrastructure.Data;
using PakkaHisaab.Infrastructure.Services;

namespace PakkaHisaab.Admin.Pages.Settings;

public class IndexModel : PageModel
{
    readonly AppDbContext _db;
    readonly ITranslationService _translator;

    public IndexModel(AppDbContext db, ITranslationService translator)
    {
        _db = db;
        _translator = translator;
    }

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

    /// <summary>One-time maintenance action: re-derives every Helper.NameEnglish and
    /// User.DisplayNameEnglish by TRANSLITERATION (sound-for-sound), overwriting any value that
    /// was previously computed by the old meaning-based translation (e.g. Hindi "आशा" wrongly
    /// stored as "Hope" instead of "Asha"). Safe to run more than once — rows whose name is
    /// already correct are simply left unchanged, and a row is never blanked just because a
    /// single call failed (the existing value stays until a later run succeeds).</summary>
    public async Task<IActionResult> OnPostFixNamesAsync(CancellationToken ct)
    {
        var row = await _db.TranslationSettingsRow.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == TranslationSettings.SingletonId, ct);
        if (row is not { Enabled: true })
        {
            TempData["Flash"] = "Enable automatic translation above first — there's nothing to fix while it's off.";
            return RedirectToPage();
        }

        int helpersFixed = 0, usersFixed = 0;

        var helpers = await _db.Helpers.Where(h => !h.IsDeleted).ToListAsync(ct);
        foreach (var h in helpers)
        {
            var latin = await _translator.TransliterateToLatinAsync(h.Name, ct);
            if (latin is not null && latin != h.NameEnglish)
            {
                h.NameEnglish = latin;
                helpersFixed++;
            }
        }

        var users = await _db.Users.ToListAsync(ct);
        foreach (var u in users)
        {
            var latin = await _translator.TransliterateToLatinAsync(u.DisplayName, ct);
            if (latin is not null && latin != u.DisplayNameEnglish)
            {
                u.DisplayNameEnglish = latin;
                usersFixed++;
            }
        }

        await _db.SaveChangesAsync(ct);

        TempData["Flash"] = $"Fixed {helpersFixed} helper name(s) and {usersFixed} account name(s) — " +
            "re-transliterated (sound-for-sound) instead of the old meaning-based translation.";
        return RedirectToPage();
    }
}
