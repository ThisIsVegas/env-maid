using EnvMaid.App.Models;

namespace EnvMaid.App.Tests;

/// <summary>
/// The entry model: one stored token, three derived views. Pure and disk-free apart from
/// variable expansion.
/// </summary>
public class PathEntryTests
{
    private static PathEntry Entry(string rawToken) => new(rawToken, PathScope.User);

    [Theory]
    [InlineData(@"C:\tools", @"C:\tools")]
    [InlineData(@"  C:\spaced  ", @"C:\spaced")]
    [InlineData(@"""C:\Program Files\X""", @"C:\Program Files\X")]
    [InlineData(@"  ""C:\quoted and padded""  ", @"C:\quoted and padded")]
    public void ParsedValue_CleansForDisplayOnly(string raw, string expectedDisplay)
    {
        var entry = Entry(raw);

        Assert.Equal(expectedDisplay, entry.ParsedValue);
        // The stored token is never touched by parsing.
        Assert.Equal(raw, entry.RawToken);
    }

    [Theory]
    [InlineData(@"  C:\spaced  ")]
    [InlineData(@"""C:\Program Files\X""")]
    [InlineData(@"C:\plain")]
    public void UneditedEntry_RoundTripsByteForByte(string raw)
    {
        // What Save writes is the stored token, so an entry nobody touched comes back out
        // exactly as it went in — padding, quotes and all.
        Assert.Equal(raw, Entry(raw).RawToken);
    }

    [Fact]
    public void DisplayDiffersFromRaw_MarksTokensThatWereCleanedForDisplay()
    {
        Assert.True(Entry(@"  C:\spaced  ").DisplayDiffersFromRaw);
        Assert.True(Entry(@"""C:\quoted""").DisplayDiffersFromRaw);
        Assert.False(Entry(@"C:\plain").DisplayDiffersFromRaw);
    }

    [Theory]
    [InlineData(@"C:\dir\", @"c:\dir")]
    [InlineData(@"C:\DIR", @"c:\dir")]
    [InlineData(@"C:\dir/", @"c:\dir")]
    public void ComparisonKey_FoldsCaseAndTrailingSeparators(string raw, string expected)
    {
        Assert.Equal(expected, Entry(raw).ComparisonKey);
    }

    [Theory]
    [InlineData(@"C:\")]
    [InlineData(@"c:\")]
    public void ComparisonKey_KeepsADriveRootsSeparator(string raw)
    {
        // C:\ folded to C: would name the current directory on that drive, which is a different
        // place — so the root keeps its separator.
        Assert.Equal(@"c:\", Entry(raw).ComparisonKey);
    }

    [Fact]
    public void AssigningRawToken_RecomputesEveryDerivedValue()
    {
        var entry = Entry(@"  C:\before  ");

        entry.RawToken = @"C:\after\";

        Assert.Equal(@"C:\after\", entry.ParsedValue);
        Assert.Equal(@"c:\after", entry.ComparisonKey);
        Assert.False(entry.DisplayDiffersFromRaw);
    }

    [Fact]
    public void AssigningRawToken_RaisesChangeNotificationsForTheDerivedViews()
    {
        var entry = Entry(@"C:\before");
        var changed = new List<string?>();
        entry.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        entry.RawToken = @"C:\after";

        // The grid binds to the derived views, so they have to update with the stored token.
        Assert.Contains(nameof(PathEntry.RawToken), changed);
        Assert.Contains(nameof(PathEntry.ParsedValue), changed);
        Assert.Contains(nameof(PathEntry.ExpandedValue), changed);
        Assert.Contains(nameof(PathEntry.ComparisonKey), changed);
        Assert.Contains(nameof(PathEntry.DisplayValue), changed);
    }

    [Fact]
    public void AutoSelectable_RequiresEveryDiagnosticToBeSafe()
    {
        var safeOnly = EntryFactory.With(@"C:\x", PathScope.User, DiagnosticKind.DuplicateL1);
        Assert.True(safeOnly.IsAutoSelectable);

        var mixed = EntryFactory.With(@"C:\x", PathScope.User,
            DiagnosticKind.DuplicateL1, DiagnosticKind.UnresolvedVariable);
        Assert.False(mixed.IsAutoSelectable);
    }

    [Fact]
    public void AnEntryWithNothingWrong_IsNotAutoSelectable()
    {
        Assert.False(Entry(@"C:\fine").IsAutoSelectable);
    }

    [Theory]
    [InlineData(DiagnosticKind.EmptyToken, true)]
    [InlineData(DiagnosticKind.FolderMissing, true)]
    [InlineData(DiagnosticKind.DuplicateL1, true)]
    [InlineData(DiagnosticKind.DuplicateL2, true)]
    [InlineData(DiagnosticKind.DuplicateL3, false)]
    [InlineData(DiagnosticKind.DuplicateL4, false)]
    [InlineData(DiagnosticKind.UnresolvedVariable, false)]
    [InlineData(DiagnosticKind.SurroundingQuotes, false)]
    [InlineData(DiagnosticKind.StructurallyAmbiguous, false)]
    [InlineData(DiagnosticKind.FolderInaccessible, false)]
    public void SafeToAutoSelect_IsAPropertyOfWhatIsWrong(DiagnosticKind kind, bool expected)
    {
        var diagnostic = new Diagnostic(kind, Severity.Warning, "test");

        Assert.Equal(expected, diagnostic.SafeToAutoSelect);
    }

    [Fact]
    public void WorstSeverity_ReportsTheMostSeriousFinding()
    {
        var entry = EntryFactory.With(@"C:\x", PathScope.User,
            DiagnosticKind.SurroundingQuotes, DiagnosticKind.FolderMissing);

        Assert.Equal(Severity.Error, entry.WorstSeverity);
        Assert.Null(Entry(@"C:\fine").WorstSeverity);
    }

    [Fact]
    public void IsBroken_ExcludesProblemsDeletionWouldNotFix()
    {
        Assert.True(EntryFactory.Missing(@"C:\gone", PathScope.User).IsBroken);
        Assert.True(EntryFactory.Empty(PathScope.User).IsBroken);

        // Both look broken; neither is fixed by deleting the entry.
        Assert.False(EntryFactory.With(@"C:\x", PathScope.User, DiagnosticKind.FolderInaccessible).IsBroken);
        Assert.False(EntryFactory.With(@"%X%\b", PathScope.User, DiagnosticKind.UnresolvedVariable).IsBroken);
    }
}
