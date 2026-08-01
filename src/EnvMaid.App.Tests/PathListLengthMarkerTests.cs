using EnvMaid.App.Models;
using EnvMaid.App.Services;
using EnvMaid.App.ViewModels;

namespace EnvMaid.App.Tests;

/// <summary>
/// The two per-entry length markers. They exist for users who also edit PATH outside EnvMaid and
/// need to see where their value crosses a boundary — so the marker has to land on the right
/// entry, not merely somewhere near it.
/// </summary>
public class PathListLengthMarkerTests
{
    private static PathListViewModel WithLength(int totalCharacters, int entryLength = 100)
    {
        var vm = new PathListViewModel(PathScope.User);

        // Each entry contributes its own length plus one separator.
        var count = totalCharacters / (entryLength + 1) + 1;
        for (var i = 0; i < count; i++)
            vm.Entries.Add(new PathEntry(new string('x', entryLength), PathScope.User));

        return vm;
    }

    [Fact]
    public void ShortPath_HasNoMarkersAndNoColour()
    {
        var vm = WithLength(500);

        Assert.All(vm.Entries, e =>
        {
            Assert.False(e.IsPastLengthLimit);
            Assert.False(e.IsLengthLimitBoundary);
            Assert.False(e.IsPastWriteLimit);
        });
        Assert.False(vm.LengthOverLimit);
    }

    [Fact]
    public void CautionBoundary_MarksExactlyOneEntry()
    {
        var vm = WithLength(4000);

        Assert.Single(vm.Entries, e => e.IsLengthLimitBoundary);
        Assert.Contains(vm.Entries, e => e.IsPastLengthLimit);

        // The marked entry is the last one that still fits under the threshold.
        var boundary = vm.Entries.First(e => e.IsLengthLimitBoundary);
        Assert.False(boundary.IsPastLengthLimit);
        Assert.True(vm.Entries[vm.Entries.IndexOf(boundary) + 1].IsPastLengthLimit);
    }

    [Fact]
    public void CautionBand_DoesNotBlockSaving()
    {
        // Common and permanent on real machines. Colour is reserved for the band that refuses.
        var vm = WithLength(6000);

        Assert.Contains(vm.Entries, e => e.IsPastLengthLimit);
        Assert.False(vm.LengthOverLimit);
        Assert.DoesNotContain(vm.Entries, e => e.IsPastWriteLimit);
    }

    [Fact]
    public void PastTheWriteLimit_MarksTheSecondBoundaryAndFlagsTheScope()
    {
        var vm = WithLength(PathLengthLimits.HardMaximum + 2000);

        Assert.Single(vm.Entries, e => e.IsWriteLimitBoundary);
        Assert.Contains(vm.Entries, e => e.IsPastWriteLimit);
        Assert.True(vm.LengthOverLimit);

        // Both markers are present — crossing the write limit means the caution one was passed
        // long ago, and hiding it would make the list look like it starts at 32,767.
        Assert.Single(vm.Entries, e => e.IsLengthLimitBoundary);
    }

    [Fact]
    public void RemovingEntries_ClearsTheMarkers()
    {
        var vm = WithLength(4000);
        Assert.Contains(vm.Entries, e => e.IsLengthLimitBoundary);

        while (vm.Entries.Count > 2)
            vm.Entries.RemoveAt(vm.Entries.Count - 1);

        Assert.DoesNotContain(vm.Entries, e => e.IsLengthLimitBoundary);
        Assert.DoesNotContain(vm.Entries, e => e.IsPastLengthLimit);
    }

    [Fact]
    public void LengthLabel_ReadsAsACountNotARatio()
    {
        var vm = WithLength(500);

        // "1,234 / 2047" framed a soft threshold as a ceiling; nothing is truncated there.
        Assert.Contains("characters", vm.LengthLabel);
        Assert.DoesNotContain("2047", vm.LengthLabel);
    }
}
