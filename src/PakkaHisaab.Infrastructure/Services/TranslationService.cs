using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace PakkaHisaab.Infrastructure.Services;

public interface ITranslationService
{
    /// <summary>Translates arbitrary user-entered text to English for Admin display, preserving
    /// meaning — use for free-text sentences (e.g. ledger notes). Returns null when there's
    /// nothing to translate, the service isn't configured, or the call fails — callers must
    /// treat null as "no translation available yet" and fall back to the original text; a
    /// translation failure must never block the underlying write.</summary>
    Task<string?> TranslateToEnglishAsync(string? text, CancellationToken ct = default);

    /// <summary>Phonetic transliteration into Latin letters, preserving SOUND rather than
    /// meaning — use for proper nouns like a helper's name, where a meaning-based translation
    /// would silently turn the name into an unrelated English word (e.g. Hindi "आशा" must
    /// become "aasha", never the semantic translation "Hope"). Same null/fallback contract as
    /// <see cref="TranslateToEnglishAsync"/>.</summary>
    Task<string?> TransliterateToLatinAsync(string? text, CancellationToken ct = default);
}

/// <summary>
/// Google Cloud Translation API (v2, REST) client — the official, paid provider. Configured
/// via GoogleTranslate:ApiKey; when that's unset, every call is a no-op. Selected by setting
/// dbo.TranslationSettings.Provider = "GoogleCloud" (see <see cref="TranslationServiceSelector"/>).
/// </summary>
public sealed class GoogleCloudTranslateService : ITranslationService
{
    readonly HttpClient _http;
    readonly ILogger<GoogleCloudTranslateService> _logger;
    readonly string? _apiKey;

    public GoogleCloudTranslateService(HttpClient http, IConfiguration config, ILogger<GoogleCloudTranslateService> logger)
    {
        _http = http;
        _logger = logger;
        _apiKey = config["GoogleTranslate:ApiKey"];
    }

    public async Task<string?> TranslateToEnglishAsync(string? text, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_apiKey) || string.IsNullOrWhiteSpace(text))
            return null;

        try
        {
            var url = $"https://translation.googleapis.com/language/translate/v2?key={Uri.EscapeDataString(_apiKey)}";
            using var response = await _http.PostAsJsonAsync(
                url, new TranslateRequest(text, "en", "text"), ct);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Google Cloud Translate returned {Status} — leaving the field untranslated",
                    response.StatusCode);
                return null;
            }

            var body = await response.Content.ReadFromJsonAsync<TranslateResponse>(cancellationToken: ct);
            var translated = body?.Data?.Translations?.FirstOrDefault()?.TranslatedText;
            return string.IsNullOrWhiteSpace(translated) ? null : translated;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Never let a translation-provider hiccup break a sync push or registration.
            _logger.LogWarning(ex, "Google Cloud Translate call failed — leaving the field untranslated");
            return null;
        }
    }

    /// <summary>The Cloud Translation v2 REST API (what this class uses) has no romanization
    /// mode — that's only in Cloud Translation Advanced (v3, <c>:romanizeText</c>), which needs
    /// a GCP project + service-account auth rather than a plain API key. Returning null here is
    /// safe and intentional: the Admin UI's Translated.For helper already falls back to showing
    /// the original (untranslated) name, which is strictly better than a wrong, meaning-based one.</summary>
    public Task<string?> TransliterateToLatinAsync(string? text, CancellationToken ct = default) =>
        Task.FromResult<string?>(null);

    record TranslateRequest(
        [property: JsonPropertyName("q")] string Q,
        [property: JsonPropertyName("target")] string Target,
        [property: JsonPropertyName("format")] string Format);
    record TranslateResponse([property: JsonPropertyName("data")] TranslateData? Data);
    record TranslateData(List<TranslateItem>? Translations);
    record TranslateItem([property: JsonPropertyName("translatedText")] string TranslatedText);
}

/// <summary>
/// The free, unofficial Google Translate web endpoint (the same one behind translate.google.com
/// and libraries like Python's `googletrans`) — no API key, no billing, no quota you can rely
/// on. It is NOT an officially supported API: Google can rate-limit, block, or change it
/// without notice, and using it for production traffic sits outside Google's Terms of Service
/// for the Cloud Translation product. It's offered here purely as a zero-cost default for a
/// low-volume app; switch to <see cref="GoogleCloudTranslateService"/> (Provider =
/// "GoogleCloud") if you need a supported SLA. Selected via Provider = "GoogleFree" (default).
/// </summary>
public sealed class GoogleFreeTranslateService : ITranslationService
{
    readonly HttpClient _http;
    readonly ILogger<GoogleFreeTranslateService> _logger;

    public GoogleFreeTranslateService(HttpClient http, ILogger<GoogleFreeTranslateService> logger)
    {
        _http = http;
        _logger = logger;
    }

    public async Task<string?> TranslateToEnglishAsync(string? text, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        try
        {
            var url = "https://translate.googleapis.com/translate_a/single" +
                      $"?client=gtx&sl=auto&tl=en&dt=t&q={Uri.EscapeDataString(text)}";
            using var response = await _http.GetAsync(url, ct);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Free Google Translate endpoint returned {Status} — leaving the field untranslated",
                    response.StatusCode);
                return null;
            }

            var json = await response.Content.ReadAsStringAsync(ct);
            var translated = ParseResponse(json);
            return string.IsNullOrWhiteSpace(translated) ? null : translated;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Free Google Translate call failed — leaving the field untranslated");
            return null;
        }
    }

    /// <summary>
    /// The endpoint replies with a loosely-typed nested JSON array (not an object), e.g.
    /// <c>[[["Hello","नमस्ते",null,null,1]],null,"hi"]</c> — the outer array's first element is
    /// itself an array of translated/original chunk pairs (long inputs get split into several);
    /// concatenating each pair's first element reassembles the full translation.
    /// </summary>
    public static string? ParseResponse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Array || root.GetArrayLength() == 0)
            return null;

        var chunks = root[0];
        if (chunks.ValueKind != JsonValueKind.Array)
            return null;

        var sb = new System.Text.StringBuilder();
        foreach (var pair in chunks.EnumerateArray())
        {
            if (pair.ValueKind == JsonValueKind.Array && pair.GetArrayLength() > 0
                && pair[0].ValueKind == JsonValueKind.String)
                sb.Append(pair[0].GetString());
        }
        return sb.ToString();
    }

    public async Task<string?> TransliterateToLatinAsync(string? text, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        try
        {
            // dt=rm ("romanization") instead of dt=t ("translation") — same endpoint, but this
            // mode returns the phonetic Latin spelling of the SOURCE text instead of its English
            // meaning, e.g. Hindi "आशा" -> "aasha", not the semantic translation "Hope".
            var url = "https://translate.googleapis.com/translate_a/single" +
                      $"?client=gtx&sl=auto&tl=en&dt=rm&q={Uri.EscapeDataString(text)}";
            using var response = await _http.GetAsync(url, ct);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Free Google Translate (romanize) endpoint returned {Status} — leaving the field untransliterated",
                    response.StatusCode);
                return null;
            }

            var json = await response.Content.ReadAsStringAsync(ct);
            var romanized = ParseRomanization(json);
            return string.IsNullOrWhiteSpace(romanized) ? null : romanized;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Free Google Translate (romanize) call failed — leaving the field untransliterated");
            return null;
        }
    }

    /// <summary>
    /// Response shape for <c>dt=rm</c> alone, e.g. <c>[[[null,null,null,"aasha"]],null,"hi"]</c>
    /// — a single segment whose 4th element is the phonetic Latin spelling. If the source text
    /// is already Latin script, Google returns no romanization array at all (root[0] is null),
    /// which the checks below fall through safely.
    /// </summary>
    public static string? ParseRomanization(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Array || root.GetArrayLength() == 0)
            return null;

        var segments = root[0];
        if (segments.ValueKind != JsonValueKind.Array || segments.GetArrayLength() == 0)
            return null;

        var segment = segments[0];
        if (segment.ValueKind == JsonValueKind.Array && segment.GetArrayLength() > 3
            && segment[3].ValueKind == JsonValueKind.String)
            return segment[3].GetString();

        return null;
    }
}
