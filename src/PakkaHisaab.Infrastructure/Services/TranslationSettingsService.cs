using Microsoft.EntityFrameworkCore;
using PakkaHisaab.Infrastructure.Data;

namespace PakkaHisaab.Infrastructure.Services;

public interface ITranslationSettingsStore
{
    Task<(bool Enabled, string Provider)> GetAsync(CancellationToken ct = default);
}

/// <summary>
/// Reads dbo.TranslationSettings (the Admin-editable on/off + provider switch), memoized for
/// this instance's lifetime. Registered Scoped alongside AppDbContext, so it's one query per
/// HTTP request no matter how many helpers/ledger entries a single sync push translates.
/// </summary>
public sealed class TranslationSettingsStore : ITranslationSettingsStore
{
    readonly AppDbContext _db;
    (bool Enabled, string Provider)? _cached;

    public TranslationSettingsStore(AppDbContext db) => _db = db;

    public async Task<(bool Enabled, string Provider)> GetAsync(CancellationToken ct = default)
    {
        if (_cached is not null) return _cached.Value;

        var row = await _db.TranslationSettingsRow.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == TranslationSettings.SingletonId, ct);
        _cached = (row?.Enabled ?? false, row?.Provider ?? "GoogleFree");
        return _cached.Value;
    }
}

/// <summary>
/// The <see cref="ITranslationService"/> every caller actually depends on — checks
/// dbo.TranslationSettings first and short-circuits to "no translation" when disabled
/// (the default), so a disabled feature costs nothing: not a network call, not a cent.
/// When enabled, dispatches to whichever concrete provider is configured.
/// </summary>
public sealed class TranslationServiceSelector : ITranslationService
{
    readonly ITranslationSettingsStore _settings;
    readonly GoogleFreeTranslateService _free;
    readonly GoogleCloudTranslateService _cloud;

    public TranslationServiceSelector(
        ITranslationSettingsStore settings, GoogleFreeTranslateService free, GoogleCloudTranslateService cloud)
    {
        _settings = settings;
        _free = free;
        _cloud = cloud;
    }

    public async Task<string?> TranslateToEnglishAsync(string? text, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        var (enabled, provider) = await _settings.GetAsync(ct);
        if (!enabled) return null;

        return provider switch
        {
            "GoogleCloud" => await _cloud.TranslateToEnglishAsync(text, ct),
            _ => await _free.TranslateToEnglishAsync(text, ct) // "GoogleFree" and any unknown value
        };
    }

    public async Task<string?> TransliterateToLatinAsync(string? text, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        var (enabled, provider) = await _settings.GetAsync(ct);
        if (!enabled) return null;

        return provider switch
        {
            "GoogleCloud" => await _cloud.TransliterateToLatinAsync(text, ct),
            _ => await _free.TransliterateToLatinAsync(text, ct)
        };
    }
}
