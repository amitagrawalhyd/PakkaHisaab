using PakkaHisaab.Shared.Domain;
using PakkaHisaab.Shared.Enums;

namespace PakkaHisaab.Shared.Tests;

public class SoundexTests
{
    [Theory]
    [InlineData("Geeta", "Gita")]
    [InlineData("Geeta", "Geetha")]
    [InlineData("Raju", "Raaju")]
    [InlineData("Robert", "Rupert")] // the textbook Soundex example
    public void SoundAlikeNames_ProduceTheSameCode(string a, string b)
    {
        Assert.Equal(VoiceLedgerParser.Soundex(a), VoiceLedgerParser.Soundex(b));
    }

    [Fact]
    public void DistinctSoundingNames_ProduceDifferentCodes()
    {
        Assert.NotEqual(VoiceLedgerParser.Soundex("Geeta"), VoiceLedgerParser.Soundex("Raju"));
    }

    [Fact]
    public void EmptyOrNonLetterInput_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, VoiceLedgerParser.Soundex(""));
        Assert.Equal(string.Empty, VoiceLedgerParser.Soundex("123"));
    }
}

public class LevenshteinDistanceTests
{
    [Fact]
    public void IdenticalStrings_HaveZeroDistance() =>
        Assert.Equal(0, VoiceLedgerParser.LevenshteinDistance("geeta", "geeta"));

    [Fact]
    public void OneSubstitution_HasDistanceOne() =>
        Assert.Equal(1, VoiceLedgerParser.LevenshteinDistance("geeta", "geata"));

    [Fact]
    public void EmptyVsNonEmpty_EqualsTheOthersLength() =>
        Assert.Equal(5, VoiceLedgerParser.LevenshteinDistance("", "geeta"));
}

public class VoiceFuzzyMatchTests
{
    static readonly string[] GeetaRaju = { "Geeta", "Raju" };

    [Theory]
    [InlineData("gita")]
    [InlineData("geetha")]
    [InlineData("geita")]
    public void MisTranscribedName_ResolvesToTheRealHelper(string mangled)
    {
        var result = VoiceLedgerParser.FindFuzzyMatch(mangled, GeetaRaju);
        Assert.Equal("Geeta", result.Name);
        Assert.False(result.Ambiguous);
    }

    [Fact]
    public void CompletelyUnrelatedWord_DoesNotMatchAnyone()
    {
        var result = VoiceLedgerParser.FindFuzzyMatch("hello there", GeetaRaju);
        Assert.Null(result.Name);
        Assert.False(result.Ambiguous);
    }

    [Fact]
    public void TwoPhoneticallySimilarHelpers_AreReportedAsAmbiguousRatherThanGuessed()
    {
        // "Geeta" and "Geetha" are both close, same-Soundex neighbors of "gita" — must not
        // silently pick one and risk logging money against the wrong person.
        var result = VoiceLedgerParser.FindFuzzyMatch("gita", new[] { "Geeta", "Geetha" });
        Assert.Null(result.Name);
        Assert.True(result.Ambiguous);
        Assert.Equal(2, result.Candidates.Length);
    }

    [Fact]
    public void NoKnownHelpers_ReturnsNoMatchWithoutThrowing()
    {
        var result = VoiceLedgerParser.FindFuzzyMatch("gita", Array.Empty<string>());
        Assert.Null(result.Name);
    }
}

public class VoiceLedgerParserTests
{
    static readonly string[] GeetaRaju = { "Geeta", "Raju" };

    // ---------- regression: existing exact-match behavior must be unchanged ----------

    [Fact]
    public void ExactHelperName_StillMatchesDirectly()
    {
        var cmd = VoiceLedgerParser.Parse("Deducted 500 rupees from Geeta", GeetaRaju);
        Assert.Equal(VoiceIntent.LogDeduction, cmd.Intent);
        Assert.Equal("Geeta", cmd.HelperNameHint);
        Assert.Equal(500m, cmd.Amount);
        Assert.Equal(VoiceSuggestionKind.None, cmd.Suggestion);
    }

    [Fact]
    public void Advance_StillParsesCorrectly()
    {
        var cmd = VoiceLedgerParser.Parse("Gave Raju an advance of 200", GeetaRaju);
        Assert.Equal(VoiceIntent.LogAdvance, cmd.Intent);
        Assert.Equal("Raju", cmd.HelperNameHint);
        Assert.Equal(200m, cmd.Amount);
    }

    [Fact]
    public void Attendance_StillParsesCorrectly()
    {
        var cmd = VoiceLedgerParser.Parse("Geeta was absent today", GeetaRaju);
        Assert.Equal(VoiceIntent.MarkAttendance, cmd.Intent);
        Assert.Equal(AttendanceStatus.Absent, cmd.Attendance);
        Assert.Equal("Geeta", cmd.HelperNameHint);
    }

    [Fact]
    public void Delivery_StillParsesCorrectly()
    {
        var cmd = VoiceLedgerParser.Parse("Raju delivered 1.5 litres", GeetaRaju);
        Assert.Equal(VoiceIntent.LogDelivery, cmd.Intent);
        Assert.Equal(1.5m, cmd.Units);
        Assert.Equal("Raju", cmd.HelperNameHint);
    }

    [Fact]
    public void DeleteAdvance_StillParsesCorrectly()
    {
        var cmd = VoiceLedgerParser.Parse("Delete Geeta's advance", GeetaRaju);
        Assert.Equal(VoiceIntent.DeleteAdvance, cmd.Intent);
        Assert.Equal("Geeta", cmd.HelperNameHint);
    }

    // ---------- new: fuzzy name resolution inside the full parse ----------

    [Fact]
    public void GarbledHelperName_ResolvesViaFuzzyMatch()
    {
        // "Gita" instead of "Geeta" — a very plausible STT mis-transcription.
        var cmd = VoiceLedgerParser.Parse("Deducted 500 rupees from Gita", GeetaRaju);
        Assert.Equal(VoiceIntent.LogDeduction, cmd.Intent);
        Assert.Equal("Geeta", cmd.HelperNameHint);
        Assert.Equal(VoiceSuggestionKind.None, cmd.Suggestion);
    }

    [Fact]
    public void AmbiguousGarbledName_LeavesHelperUnsetAndExplainsWhy()
    {
        var helpers = new[] { "Geeta", "Geetha" };
        var cmd = VoiceLedgerParser.Parse("Deducted 500 rupees from Gita", helpers);
        Assert.Equal(VoiceIntent.LogDeduction, cmd.Intent);
        Assert.Null(cmd.HelperNameHint);
        Assert.Equal(VoiceSuggestionKind.HelperAmbiguous, cmd.Suggestion);
        Assert.Equal(2, cmd.SuggestionArgs!.Length);
    }

    // ---------- new: suggestions when a command is genuinely incomplete ----------

    [Fact]
    public void KeywordWithoutAmount_SuggestsAWorkingExample()
    {
        var cmd = VoiceLedgerParser.Parse("Deducted from Geeta", GeetaRaju);
        Assert.Equal(VoiceIntent.Unknown, cmd.Intent);
        Assert.Equal(VoiceSuggestionKind.KeywordFoundNoAmount, cmd.Suggestion);
        Assert.Equal("deduction", cmd.SuggestionArgs![0]);
        Assert.Contains("Geeta", cmd.SuggestionArgs[1]); // uses a real household helper name
    }

    [Fact]
    public void DeliveryWithoutQuantity_SuggestsAWorkingExample()
    {
        var cmd = VoiceLedgerParser.Parse("Raju delivered milk", GeetaRaju);
        Assert.Equal(VoiceIntent.Unknown, cmd.Intent);
        Assert.Equal(VoiceSuggestionKind.DeliveryNoUnits, cmd.Suggestion);
    }

    [Fact]
    public void TotallyUnrelatedSpeech_GetsAGenericExample()
    {
        var cmd = VoiceLedgerParser.Parse("what's the weather today", GeetaRaju);
        Assert.Equal(VoiceIntent.Unknown, cmd.Intent);
        Assert.Equal(VoiceSuggestionKind.GenericExample, cmd.Suggestion);
        Assert.NotNull(cmd.SuggestionArgs);
    }

    // ---------- new: helper-not-found suggestions only fire when there's real ambiguity ----------

    [Fact]
    public void RecognizedActionWithNoHelperMention_SuggestsKnownHelpers_WhenMultipleExist()
    {
        var cmd = VoiceLedgerParser.Parse("Deducted 500 rupees", GeetaRaju);
        Assert.Equal(VoiceIntent.LogDeduction, cmd.Intent);
        Assert.Null(cmd.HelperNameHint);
        Assert.Equal(VoiceSuggestionKind.HelperNotFound, cmd.Suggestion);
        Assert.Contains("Geeta", cmd.SuggestionArgs!);
        Assert.Contains("Raju", cmd.SuggestionArgs!);
    }

    [Fact]
    public void RecognizedActionWithNoHelperMention_NoSuggestion_WhenOnlyOneHelperExists()
    {
        // A single-helper household has nothing to disambiguate — VoiceLedgerService's own
        // "only one helper" fallback resolves this case, so Parse must not flag it as an issue.
        var cmd = VoiceLedgerParser.Parse("Deducted 500 rupees", new[] { "Geeta" });
        Assert.Equal(VoiceIntent.LogDeduction, cmd.Intent);
        Assert.Equal(VoiceSuggestionKind.None, cmd.Suggestion);
    }

    [Fact]
    public void NoKnownHelpersAtAll_DoesNotThrow()
    {
        var cmd = VoiceLedgerParser.Parse("Deducted 500 rupees from Geeta", Array.Empty<string>());
        Assert.Equal(VoiceIntent.LogDeduction, cmd.Intent);
        Assert.Null(cmd.HelperNameHint);
        Assert.Equal(VoiceSuggestionKind.None, cmd.Suggestion); // knownHelperNames.Count > 1 is false
    }
}
