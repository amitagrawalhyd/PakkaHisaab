using PakkaHisaab.Infrastructure.Services;

namespace PakkaHisaab.Api.Tests;

/// <summary>Records every call so tests can assert translation ran exactly when expected —
/// e.g. skipped when a field didn't change, so a sync push touching unrelated fields doesn't
/// re-bill the translation API for no reason.</summary>
public sealed class FakeTranslationService : ITranslationService
{
    public List<string> Requests { get; } = new();
    public List<string> TransliterationRequests { get; } = new();
    public Func<string, string?> Translate { get; set; } = t => $"[en] {t}";
    public Func<string, string?> Transliterate { get; set; } = t => $"[lit] {t}";
    public bool ShouldFail { get; set; }

    public Task<string?> TranslateToEnglishAsync(string? text, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(text)) return Task.FromResult<string?>(null);
        Requests.Add(text);
        return Task.FromResult(ShouldFail ? null : Translate(text));
    }

    public Task<string?> TransliterateToLatinAsync(string? text, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(text)) return Task.FromResult<string?>(null);
        TransliterationRequests.Add(text);
        return Task.FromResult(ShouldFail ? null : Transliterate(text));
    }
}
