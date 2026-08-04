using System.Text;
using System.Text.Json;

namespace PakkaHisaab.Maui.Services;

public interface ITranslationService
{
    /// <summary>Best-effort translation of <paramref name="text"/> to English. Returns the
    /// input unchanged if it's already ASCII-only (no network call needed) or if translation
    /// fails for any reason (offline, endpoint unavailable, malformed response) — this must
    /// never throw or block a caller on network availability.</summary>
    Task<string> TranslateToEnglishAsync(string text, CancellationToken ct = default);
}

/// <summary>
/// Calls the free, unofficial Google Translate web endpoint (no API key, no billing account —
/// the same trick behind most "free Google Translate" scripts/libraries). This is NOT an
/// official Google API: it has no SLA and Google can rate-limit or block it without notice.
/// Every failure mode (offline, blocked, timeout, unexpected response shape) falls back to
/// returning the original text unchanged, so a flaky or unavailable network never breaks voice
/// commands or helper-name entry — it just leaves them in their original language/script.
/// </summary>
public sealed class GoogleFreeTranslateService : ITranslationService
{
    const string Endpoint = "https://translate.googleapis.com/translate_a/single";
    static readonly TimeSpan CallTimeout = TimeSpan.FromSeconds(4);

    readonly IHttpClientFactory _factory;
    readonly ITelemetryService _telemetry;

    public GoogleFreeTranslateService(IHttpClientFactory factory, ITelemetryService telemetry)
    {
        _factory = factory;
        _telemetry = telemetry;
    }

    public async Task<string> TranslateToEnglishAsync(string text, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(text) || IsAsciiOnly(text))
            return text; // already English/Latin — skip the round-trip entirely

        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(CallTimeout);

            var client = _factory.CreateClient("google-translate-free");
            var url = $"{Endpoint}?client=gtx&sl=auto&tl=en&dt=t&q={Uri.EscapeDataString(text)}";
            using var response = await client.GetAsync(url, timeoutCts.Token);
            if (!response.IsSuccessStatusCode) return text;

            using var stream = await response.Content.ReadAsStreamAsync(timeoutCts.Token);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: timeoutCts.Token);

            // Response shape (undocumented, stable in practice): the first element is an array
            // of [translatedSegment, originalSegment, ...] pairs; concatenating every segment's
            // translated piece reassembles the full translated sentence.
            var sb = new StringBuilder();
            foreach (var segment in doc.RootElement[0].EnumerateArray())
            {
                var piece = segment[0].GetString();
                if (piece is not null) sb.Append(piece);
            }

            var translated = sb.ToString();
            return string.IsNullOrWhiteSpace(translated) ? text : translated;
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            _telemetry.TrackError(ex, "translate_fallback");
            return text;
        }
    }

    static bool IsAsciiOnly(string s)
    {
        foreach (var c in s)
            if (c > 127) return false;
        return true;
    }
}
