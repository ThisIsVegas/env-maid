using EnvMaid.App.Models;
using EnvMaid.App.Services;

namespace EnvMaid.App.Tests;

public class PathDiffServiceTests
{
    private readonly PathDiffService _sut = new();

    [Fact]
    public void NoChanges_WhenIdentical()
    {
        var diff = _sut.Diff(PathScope.User, new[] { "a", "b" }, new[] { "a", "b" });

        Assert.False(diff.HasChanges);
        Assert.Empty(diff.Changes);
        Assert.False(diff.OrderChanged);
    }

    [Fact]
    public void DetectsAddedAndRemoved()
    {
        var diff = _sut.Diff(PathScope.User, new[] { "a", "b" }, new[] { "a", "c" });

        Assert.Contains(diff.Changes, c => c.Kind == PathChangeKind.Added && c.Path == "c");
        Assert.Contains(diff.Changes, c => c.Kind == PathChangeKind.Removed && c.Path == "b");
    }

    [Fact]
    public void DetectsReorderOfSurvivingEntries()
    {
        var diff = _sut.Diff(PathScope.User, new[] { "a", "b", "c" }, new[] { "c", "b", "a" });

        Assert.True(diff.OrderChanged);
        Assert.Empty(diff.Changes); // same set, only order differs
    }

    [Fact]
    public void AddRemoveWithoutReorder_DoesNotFlagOrderChange()
    {
        // Removing "b" and adding "d" at the end leaves a,c in the same relative order.
        var diff = _sut.Diff(PathScope.User, new[] { "a", "b", "c" }, new[] { "a", "c", "d" });

        Assert.False(diff.OrderChanged);
        Assert.Equal(2, diff.Changes.Count);
    }

    [Fact]
    public void ComparisonIsCaseInsensitive()
    {
        var diff = _sut.Diff(PathScope.User, new[] { "C:\\Tools" }, new[] { "c:\\tools" });

        Assert.False(diff.HasChanges);
    }
}
