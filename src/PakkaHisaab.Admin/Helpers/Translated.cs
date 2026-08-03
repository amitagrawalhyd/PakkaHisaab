namespace PakkaHisaab.Admin.Helpers;

/// <summary>
/// Resolves what an Admin page should show for a user-entered field that may have been typed
/// in any language: the machine-translated English copy when one exists, falling back to the
/// original text when translation hasn't happened yet (no API key configured, or the call
/// failed) — Admin pages must never show nothing just because a translation is missing.
/// </summary>
public static class Translated
{
    /// <param name="original">The raw, as-entered value.</param>
    /// <param name="english">The translated value from the *_English column, if any.</param>
    /// <returns><c>Display</c> is what to show as the primary text; <c>Original</c> is
    /// non-null only when it differs from <c>Display</c>, for an original-language caption.</returns>
    public static (string Display, string? Original) For(string original, string? english)
    {
        if (string.IsNullOrWhiteSpace(english) || english == original)
            return (original, null);
        return (english, original);
    }
}
