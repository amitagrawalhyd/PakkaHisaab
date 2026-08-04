using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using PakkaHisaab.Shared.Enums;

namespace PakkaHisaab.Shared.Domain;

/// <summary>Why a command didn't fully resolve, structured so callers (MAUI service, /ai/parse
/// API clients) can build a localized, specific message instead of a generic "didn't understand".
/// Args are plain strings (helper names, example phrases) — never pre-translated, since this
/// type lives in the UI-free Shared layer; callers localize the surrounding sentence.</summary>
public enum VoiceSuggestionKind
{
    /// <summary>Command fully resolved — no suggestion needed.</summary>
    None = 0,
    /// <summary>Nothing recognizable at all. Args: [exampleCommand].</summary>
    GenericExample = 1,
    /// <summary>An action keyword (advance/deduction/bonus/payment) was heard but no amount.
    /// Args: [keywordName, exampleCommand].</summary>
    KeywordFoundNoAmount = 2,
    /// <summary>A delivery word was heard but no quantity. Args: [exampleCommand].</summary>
    DeliveryNoUnits = 3,
    /// <summary>The action was understood but no helper could be matched, confidently or
    /// otherwise. Args: known helper names (up to 4).</summary>
    HelperNotFound = 4,
    /// <summary>Two or more helpers are an equally close phonetic match — deliberately not
    /// guessing between them. Args: [candidate1, candidate2].</summary>
    HelperAmbiguous = 5
}

public record VoiceCommand(
    VoiceIntent Intent,
    string? HelperNameHint,
    decimal Amount,
    decimal Units,
    AttendanceStatus? Attendance,
    string RawText,
    VoiceSuggestionKind Suggestion = VoiceSuggestionKind.None,
    string[]? SuggestionArgs = null);

public enum VoiceIntent
{
    Unknown = 0,
    LogAdvance = 1,
    LogDeduction = 2,
    MarkAttendance = 3,
    LogDelivery = 4,
    LogPayment = 5,
    LogBonus = 6,
    DeleteAdvance = 7,
    DeleteBonus = 8
}

/// <summary>
/// Rule-based NLP for the Voice-to-Ledger feature. Runs fully offline on-device;
/// the API exposes the same parser at /ai/parse for thin clients — one implementation, two hosts.
/// Handles English and romanized Hindi keywords, e.g.:
///   "Deducted 500 rupees from Geeta"        → LogDeduction  (500, Geeta)
///   "Gave Raju an advance of 200"           → LogAdvance    (200, Raju)
///   "Geeta was absent today"                → MarkAttendance(Absent, Geeta)
///   "Raju delivered 1.5 litres"             → LogDelivery   (1.5, Raju)
///   "Paid Geeta 4500 salary"                → LogPayment    (4500, Geeta)
///   "Delete Geeta's advance"                → DeleteAdvance (Geeta)
///   "Remove Raju's bonus"                   → DeleteBonus   (Raju)
///
/// Helper names tolerate speech-to-text transcription noise (e.g. "Gita" for "Geeta") via a
/// Soundex + Levenshtein-distance fallback (see <see cref="FindFuzzyMatch"/>) when no exact
/// substring match is found — but it never guesses between two equally plausible helpers; that
/// comes back as <see cref="VoiceSuggestionKind.HelperAmbiguous"/> instead.
/// </summary>
public static class VoiceLedgerParser
{
    static readonly Regex AmountRx = new(@"(?<amt>\d+(?:[.,]\d{1,2})?)\s*(?:rupees|rupee|rs\.?|₹)?",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    static readonly Regex UnitsRx = new(@"(?<units>\d+(?:[.,]\d{1,2})?)\s*(?:liters?|litres?|l\b|packets?|units?|kg)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    static readonly Regex WordRx = new(@"[a-zA-Z]+", RegexOptions.Compiled);

    static readonly string[] AdvanceWords = { "advance", "udhaar", "udhar", "peshgi", "gave", "diya" };
    static readonly string[] DeductWords = { "deduct", "deducted", "cut", "kata", "kaata", "minus" };
    static readonly string[] AbsentWords = { "absent", "chhutti", "chutti", "leave", "didn't come", "did not come", "nahi aayi", "nahi aaya" };
    static readonly string[] PresentWords = { "present", "came", "aayi", "aaya" };
    static readonly string[] HalfDayWords = { "half day", "half-day", "aadha din" };
    static readonly string[] DeliveryWords = { "delivered", "delivery", "liter", "litre", "milk", "doodh" };
    static readonly string[] PaymentWords = { "paid", "salary", "settle", "settled", "tankha", "pagaar", "pagar" };
    static readonly string[] BonusWords = { "bonus", "diwali", "baksheesh", "inaam" };
    static readonly string[] DeleteWords = { "delete", "remove", "undo", "cancel", "hata", "hatao", "mita", "mitao" };

    public static VoiceCommand Parse(string text, IReadOnlyCollection<string> knownHelperNames)
    {
        var t = (text ?? string.Empty).Trim().ToLowerInvariant();

        string? nameHint = knownHelperNames
            .OrderByDescending(n => n.Length)
            .FirstOrDefault(n => t.Contains(n.ToLowerInvariant()));

        var ambiguousCandidates = Array.Empty<string>();
        if (nameHint is null && knownHelperNames.Count > 0)
        {
            var fuzzy = FindFuzzyMatch(t, knownHelperNames);
            if (fuzzy.Name is not null)
                nameHint = fuzzy.Name;
            else if (fuzzy.Ambiguous)
                ambiguousCandidates = fuzzy.Candidates;
        }

        decimal amount = 0, units = 0;
        var am = AmountRx.Match(t);
        if (am.Success)
            amount = decimal.Parse(am.Groups["amt"].Value.Replace(',', '.'), CultureInfo.InvariantCulture);
        var um = UnitsRx.Match(t);
        if (um.Success)
            units = decimal.Parse(um.Groups["units"].Value.Replace(',', '.'), CultureInfo.InvariantCulture);

        bool Any(string[] words) => words.Any(t.Contains);
        bool advanceWord = Any(AdvanceWords), deductWord = Any(DeductWords), bonusWord = Any(BonusWords),
             paymentWord = Any(PaymentWords), deliveryWord = Any(DeliveryWords);

        VoiceIntent intent;
        AttendanceStatus? attendance = null;

        // Delete checked first: "delete/remove ... bonus/advance" carries no amount, so it would
        // never satisfy the amount-gated Log* checks below anyway, but keeping it first avoids any
        // ambiguity with words (e.g. "gave", "diya") shared with the Advance/Bonus word lists.
        if (Any(DeleteWords) && bonusWord) intent = VoiceIntent.DeleteBonus;
        else if (Any(DeleteWords) && advanceWord) intent = VoiceIntent.DeleteAdvance;
        else if (Any(HalfDayWords)) { intent = VoiceIntent.MarkAttendance; attendance = AttendanceStatus.HalfDay; }
        else if (Any(AbsentWords)) { intent = VoiceIntent.MarkAttendance; attendance = AttendanceStatus.Absent; }
        else if (deliveryWord && units > 0) intent = VoiceIntent.LogDelivery;
        else if (deductWord && amount > 0) intent = VoiceIntent.LogDeduction;
        else if (bonusWord && amount > 0) intent = VoiceIntent.LogBonus;
        else if (paymentWord && amount > 0) intent = VoiceIntent.LogPayment;
        else if (advanceWord && amount > 0) intent = VoiceIntent.LogAdvance;
        else if (Any(PresentWords)) { intent = VoiceIntent.MarkAttendance; attendance = AttendanceStatus.Present; }
        else intent = VoiceIntent.Unknown;

        var (suggestion, args) = BuildSuggestion(
            intent, nameHint, ambiguousCandidates,
            advanceWord, deductWord, bonusWord, paymentWord, deliveryWord, knownHelperNames);

        return new VoiceCommand(intent, nameHint, amount, units, attendance, text!, suggestion, args);
    }

    static (VoiceSuggestionKind Kind, string[]? Args) BuildSuggestion(
        VoiceIntent intent, string? nameHint, string[] ambiguousCandidates,
        bool advanceWord, bool deductWord, bool bonusWord, bool paymentWord, bool deliveryWord,
        IReadOnlyCollection<string> knownHelperNames)
    {
        string example = knownHelperNames.FirstOrDefault() ?? "Geeta";

        if (intent == VoiceIntent.Unknown)
        {
            if (deliveryWord) return (VoiceSuggestionKind.DeliveryNoUnits, new[] { $"{example} delivered 1.5 litres" });
            if (deductWord) return (VoiceSuggestionKind.KeywordFoundNoAmount, new[] { "deduction", $"Deducted 500 from {example}" });
            if (bonusWord) return (VoiceSuggestionKind.KeywordFoundNoAmount, new[] { "bonus", $"Diwali bonus 500 for {example}" });
            if (paymentWord) return (VoiceSuggestionKind.KeywordFoundNoAmount, new[] { "payment", $"Paid {example} 4500 salary" });
            if (advanceWord) return (VoiceSuggestionKind.KeywordFoundNoAmount, new[] { "advance", $"Gave {example} 500 advance" });
            return (VoiceSuggestionKind.GenericExample, new[] { $"Gave {example} 500 advance" });
        }

        // The action was understood but no helper could be pinned down — only meaningful to
        // flag when there's actually a choice to make; a single-helper household has none
        // (VoiceLedgerService's own "only one helper" fallback handles that case instead).
        if (nameHint is null && knownHelperNames.Count > 1)
        {
            if (ambiguousCandidates.Length >= 2)
                return (VoiceSuggestionKind.HelperAmbiguous, new[] { ambiguousCandidates[0], ambiguousCandidates[1] });
            return (VoiceSuggestionKind.HelperNotFound, knownHelperNames.Take(4).ToArray());
        }

        return (VoiceSuggestionKind.None, null);
    }

    /// <summary>
    /// Finds the known helper name that best matches a mis-transcribed mention in
    /// <paramref name="text"/>. Phonetic equality (Soundex) decides which helpers are even
    /// candidates — this is deliberately the primary signal, since speech-to-text errors are
    /// sound-alike substitutions ("Gita" for "Geeta"), not random typos — then Levenshtein
    /// distance ranks among those candidates and vetoes the match entirely if the closest
    /// candidate still isn't a good enough fit, or if two candidates are too close to call.
    /// </summary>
    public static (string? Name, bool Ambiguous, string[] Candidates) FindFuzzyMatch(
        string text, IReadOnlyCollection<string> knownHelperNames)
    {
        var words = WordRx.Matches(text).Select(m => m.Value).Where(w => w.Length >= 3).ToList();
        if (words.Count == 0 || knownHelperNames.Count == 0)
            return (null, false, Array.Empty<string>());

        var scored = new List<(string Name, bool PhoneticHit, int MinDistance)>();
        foreach (var name in knownHelperNames)
        {
            var nameWords = WordRx.Matches(name).Select(m => m.Value).Where(w => w.Length >= 3).ToList();
            if (nameWords.Count == 0) continue;

            bool phoneticHit = false;
            int minDistance = int.MaxValue;
            foreach (var nw in nameWords)
            {
                var nwSoundex = Soundex(nw);
                foreach (var w in words)
                {
                    if (Soundex(w) == nwSoundex) phoneticHit = true;
                    int distance = LevenshteinDistance(nw.ToLowerInvariant(), w.ToLowerInvariant());
                    if (distance < minDistance) minDistance = distance;
                }
            }
            if (phoneticHit)
                scored.Add((name, true, minDistance));
        }

        if (scored.Count == 0)
            return (null, false, Array.Empty<string>());

        scored.Sort((a, b) => a.MinDistance.CompareTo(b.MinDistance));
        var best = scored[0];

        // Two candidates within one edit of each other are too close to guess between —
        // e.g. "Sita" and "Gita" could both phonetically match a garbled "Zita".
        bool ambiguous = scored.Count > 1 && scored[1].MinDistance <= best.MinDistance + 1;
        if (ambiguous)
            return (null, true, new[] { scored[0].Name, scored[1].Name });

        // A Soundex hit alone isn't sufficient if the actual spelling is wildly different —
        // Soundex codes are only 4 characters and can coincidentally collide on short/common
        // syllables. Require the edit distance to still be reasonably tight relative to name length.
        if (best.MinDistance > Math.Max(2, best.Name.Length / 2))
            return (null, false, Array.Empty<string>());

        return (best.Name, false, new[] { best.Name });
    }

    /// <summary>Classic American Soundex: first letter + up to 3 digits encoding the remaining
    /// consonant sounds, so words that sound alike collapse to the same code regardless of
    /// exact spelling (e.g. "Geeta", "Gita" and "Geetha" all encode to "G300").</summary>
    public static string Soundex(string word)
    {
        if (string.IsNullOrWhiteSpace(word)) return string.Empty;
        var letters = word.Where(char.IsLetter).Select(char.ToUpperInvariant).ToArray();
        if (letters.Length == 0) return string.Empty;

        var sb = new StringBuilder();
        sb.Append(letters[0]);
        int lastCode = Code(letters[0]);

        for (int i = 1; i < letters.Length && sb.Length < 4; i++)
        {
            int code = Code(letters[i]);
            if (code != 0 && code != lastCode)
                sb.Append(code);
            // H and W don't break a run of the same consonant code (official Soundex rule);
            // vowels do, so e.g. "GEETA" still collapses the two T-adjacent codes correctly.
            if (letters[i] != 'H' && letters[i] != 'W')
                lastCode = code;
        }
        while (sb.Length < 4) sb.Append('0');
        return sb.ToString();

        static int Code(char c) => c switch
        {
            'B' or 'F' or 'P' or 'V' => 1,
            'C' or 'G' or 'J' or 'K' or 'Q' or 'S' or 'X' or 'Z' => 2,
            'D' or 'T' => 3,
            'L' => 4,
            'M' or 'N' => 5,
            'R' => 6,
            _ => 0 // vowels and Y have no code
        };
    }

    /// <summary>Minimum single-character edits (insert/delete/substitute) to turn one string
    /// into the other — used to rank/veto among Soundex-phonetic candidates.</summary>
    public static int LevenshteinDistance(string a, string b)
    {
        a ??= string.Empty;
        b ??= string.Empty;
        int n = a.Length, m = b.Length;
        if (n == 0) return m;
        if (m == 0) return n;

        var previous = new int[m + 1];
        var current = new int[m + 1];
        for (int j = 0; j <= m; j++) previous[j] = j;

        for (int i = 1; i <= n; i++)
        {
            current[0] = i;
            for (int j = 1; j <= m; j++)
            {
                int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1), previous[j - 1] + cost);
            }
            (previous, current) = (current, previous);
        }
        return previous[m];
    }
}
