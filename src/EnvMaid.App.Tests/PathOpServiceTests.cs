using EnvMaid.App.Models;
using EnvMaid.App.Services;
using Microsoft.Win32;

namespace EnvMaid.App.Tests;

/// <summary>
/// The ops that cross the privilege boundary. Pure logic — a round-trip through Diff and Apply
/// must reproduce the intended list, because the elevated helper applies these to a value the
/// parent never saw.
/// </summary>
public class PathOpServiceTests
{
    private static IReadOnlyList<string> RoundTrip(string[] from, string[] to) =>
        PathOpService.Apply(from, PathOpService.Diff(from, to));

    [Theory]
    // removal
    [InlineData(new[] { @"C:\a", @"C:\b", @"C:\c" }, new[] { @"C:\a", @"C:\c" })]
    // insertion in the middle
    [InlineData(new[] { @"C:\a", @"C:\c" }, new[] { @"C:\a", @"C:\b", @"C:\c" })]
    // reorder
    [InlineData(new[] { @"C:\a", @"C:\b", @"C:\c" }, new[] { @"C:\c", @"C:\a", @"C:\b" })]
    // add and remove together
    [InlineData(new[] { @"C:\a", @"C:\b" }, new[] { @"C:\b", @"C:\new" })]
    // emptied
    [InlineData(new[] { @"C:\a" }, new string[0])]
    // filled from empty
    [InlineData(new string[0], new[] { @"C:\a", @"C:\b" })]
    // unchanged
    [InlineData(new[] { @"C:\a", @"C:\b" }, new[] { @"C:\a", @"C:\b" })]
    public void DiffThenApply_ReproducesTheIntendedList(string[] from, string[] to)
    {
        Assert.Equal(to, RoundTrip(from, to));
    }

    [Fact]
    public void UnchangedList_ProducesNoOps()
    {
        Assert.Empty(PathOpService.Diff(new[] { @"C:\a", @"C:\b" }, new[] { @"C:\a", @"C:\b" }));
    }

    [Fact]
    public void Apply_SkipsARemovalSomeoneElseAlreadyMade()
    {
        // Ops describe intent, not a transcript. A token already gone is not an error.
        var ops = new[] { new PathOp(PathOpKind.Remove, @"C:\gone") };

        Assert.Equal(new[] { @"C:\a" }, PathOpService.Apply(new[] { @"C:\a" }, ops));
    }

    [Fact]
    public void Apply_ClampsAnAddPositionPastTheEnd()
    {
        var ops = new[] { new PathOp(PathOpKind.Add, @"C:\new", At: 99) };

        Assert.Equal(new[] { @"C:\a", @"C:\new" }, PathOpService.Apply(new[] { @"C:\a" }, ops));
    }

    [Fact]
    public void Baseline_MatchesOnlyTheExactStoredValue()
    {
        var value = VariableValue.Of(RegistryValueKind.ExpandString, @"C:\a;C:\b");
        var baseline = PathOpService.BaselineOf(value);

        Assert.True(PathOpService.Matches(baseline, value));

        // Same characters, different type — an installer downgrading REG_EXPAND_SZ to REG_SZ.
        Assert.False(PathOpService.Matches(baseline, VariableValue.Of(RegistryValueKind.String, @"C:\a;C:\b")));

        // A trailing separator is invisible once split, so the hash is what catches it.
        Assert.False(PathOpService.Matches(baseline, VariableValue.Of(RegistryValueKind.ExpandString, @"C:\a;C:\b;")));

        Assert.False(PathOpService.Matches(baseline, VariableValue.Absent));
    }
}
