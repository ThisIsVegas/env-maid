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
        Assert.Contains(diff.Changes, change =>
            change.Kind == PathChangeKind.Moved &&
            change.Path == "a" &&
            change.PreviousPosition == 1 &&
            change.NewPosition == 3);
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
    public void RemovedEmptyEntry_DisplaysAsEmptyLabelWithReason()
    {
        var diff = _sut.Diff(PathScope.User, new[] { "" }, Array.Empty<string>());

        var removed = Assert.Single(diff.Changes);
        Assert.Equal(PathChangeKind.Removed, removed.Kind);
        Assert.Equal("(empty entry)", removed.DisplayPath);
        Assert.Equal("Empty entry.", removed.Reason);
    }

    [Fact]
    public void RemovedMissingFolder_HasReason()
    {
        var missing = "Z:\\definitely\\not\\here\\" + Guid.NewGuid();
        var diff = _sut.Diff(PathScope.User, new[] { missing }, Array.Empty<string>());

        Assert.Equal("Folder did not exist.", Assert.Single(diff.Changes).Reason);
    }

    [Fact]
    public void ComparisonIsCaseInsensitive()
    {
        var diff = _sut.Diff(PathScope.User, new[] { "C:\\Tools" }, new[] { "c:\\tools" });

        Assert.False(diff.HasChanges);
    }

    [Fact]
    public void NormalizedStoredValue_IsPresentedAsOneChange()
    {
        var diff = _sut.Diff(
            PathScope.User,
            new[] { @"C:\Tools\" },
            new[] { @"C:\Tools" });

        var change = Assert.Single(diff.Changes);
        Assert.Equal(PathChangeKind.Changed, change.Kind);
        Assert.Equal(@"C:\Tools\", change.PreviousPath);
        Assert.Equal(@"C:\Tools", change.Path);
    }

    [Fact]
    public void AddRemoveWithoutRelativeReorder_HasNoMoveChanges()
    {
        var diff = _sut.Diff(
            PathScope.User,
            new[] { "a", "b", "c" },
            new[] { "a", "c", "d" });

        Assert.DoesNotContain(diff.Changes, change => change.Kind == PathChangeKind.Moved);
    }

    [Fact]
    public void NormalizedEntryThatAlsoMoves_ReportsBothChangeAndMove()
    {
        var diff = _sut.Diff(
            PathScope.User,
            new[] { @"C:\A\", @"C:\B" },
            new[] { @"C:\B", @"C:\A" });

        Assert.True(diff.OrderChanged);
        Assert.Contains(diff.Changes, change =>
            change.Kind == PathChangeKind.Changed &&
            change.PreviousPath == @"C:\A\" &&
            change.Path == @"C:\A");
        Assert.Contains(diff.Changes, change =>
            change.Kind == PathChangeKind.Moved &&
            change.Path == @"C:\A" &&
            change.PreviousPosition == 1 &&
            change.NewPosition == 2);
    }
}
