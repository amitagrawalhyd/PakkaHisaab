using PakkaHisaab.Api.Services;

namespace PakkaHisaab.Api.Tests;

/// <summary>Records every call so tests can assert translation ran exactly when expected —
/// e.g. skipped when a field didn't change, so a sync push touching unrelated fields doesn't
/// re-bill the translation API for no reason.</summary>
public sealed class FakeTranslationService : ITranslationService
{
    public List<string> Requests { get; } = new();
    public Func<string, string?> Translate { get; set; } = t => $"[en] {t}";
    public bool ShouldFail { get; set; }

    public Task<string?> TranslateToEnglishAsync(string? text, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(text)) return Task.FromResult<string?>(null);
        Requests.Add(text);
        return Task.FromResult(ShouldFail ? null : Translate(text));
    }
}
