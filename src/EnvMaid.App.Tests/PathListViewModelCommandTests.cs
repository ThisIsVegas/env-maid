using EnvMaid.App.Models;
using EnvMaid.App.Services;
using EnvMaid.App.ViewModels;

namespace EnvMaid.App.Tests;

public class PathListViewModelCommandTests
{
    private static PathListViewModel NewVm() =>
        new(PathScope.User, new PathNormalizer(),
            new PathCompressor(name => name == "LOCALAPPDATA" ? @"C:\Users\me\AppData\Local" : null));

    [Fact]
    public void Normalize_TrimsTrailingSlash_AndLeavesVarsAlone()
    {
        var vm = NewVm();
        vm.Entries.Add(new PathEntry(@"C:\bin\", PathScope.User));
        vm.Entries.Add(new PathEntry(@"%JAVA_HOME%\bin", PathScope.User));

        vm.NormalizeCommand.Execute(null);

        Assert.Equal(@"C:\bin", vm.Entries[0].RawToken);
        Assert.Equal(@"%JAVA_HOME%\bin", vm.Entries[1].RawToken);
    }

    [Fact]
    public void RemoveDuplicates_KeepsFirst_CollapsesSlashVariants()
    {
        // RemoveDuplicates acts on the level the analysis assigned, so the duplicate carries the
        // diagnostic here rather than the command re-deriving "same folder" for itself.
        var vm = NewVm();
        vm.Entries.Add(new PathEntry(@"C:\bin", PathScope.User));
        vm.Entries.Add(EntryFactory.With(@"C:\bin\", PathScope.User, DiagnosticKind.DuplicateL2));
        vm.Entries.Add(new PathEntry(@"C:\other", PathScope.User));

        vm.RemoveDuplicatesCommand.Execute(null);

        Assert.Equal(2, vm.Entries.Count);
        Assert.Equal(@"C:\bin", vm.Entries[0].RawToken);
        Assert.Equal(@"C:\other", vm.Entries[1].RawToken);
    }

    [Fact]
    public void RemoveDuplicates_ListsAnL3Unchecked_AndLeavesItAlone()
    {
        // Two references to the same folder today, maintained separately. Removing one is a
        // decision about which reference survives, so it must not happen by default.
        var vm = NewVm();
        vm.Entries.Add(new PathEntry(@"C:\jdk\bin", PathScope.User));
        vm.Entries.Add(EntryFactory.With(@"%JAVA_HOME%\bin", PathScope.User, DiagnosticKind.DuplicateL3));
        MaintenancePreview? captured = null;
        vm.ConfirmMaintenance = preview => { captured = preview; return true; };

        vm.RemoveDuplicatesCommand.Execute(null);

        Assert.NotNull(captured);
        Assert.False(Assert.Single(captured.Changes).IsSelected);
        Assert.Equal(2, vm.Entries.Count);
    }

    [Fact]
    public void RemoveDuplicates_DoesNotOfferAnL4AtAll()
    {
        // A junction or subst alias is advisory: the two paths may exist deliberately, so it is
        // reported on the grid and never offered for bulk removal.
        var vm = NewVm();
        vm.Entries.Add(new PathEntry(@"C:\real", PathScope.User));
        vm.Entries.Add(EntryFactory.With(@"C:\junction", PathScope.User, DiagnosticKind.DuplicateL4));
        MaintenancePreview? captured = null;
        vm.ConfirmMaintenance = preview => { captured = preview; return true; };

        vm.RemoveDuplicatesCommand.Execute(null);

        Assert.NotNull(captured);
        Assert.Empty(captured.Changes);
        Assert.Equal(2, vm.Entries.Count);
    }

    [Fact]
    public void RemoveBroken_RemovesMissingAndEmpty_KeepsOthers()
    {
        var vm = NewVm();
        vm.Entries.Add(new PathEntry(@"C:\good", PathScope.User));
        vm.Entries.Add(EntryFactory.Missing(@"C:\gone", PathScope.User));
        vm.Entries.Add(EntryFactory.Empty(PathScope.User));

        vm.RemoveBrokenCommand.Execute(null);

        Assert.Single(vm.Entries);
        Assert.Equal(@"C:\good", vm.Entries[0].RawToken);
    }

    [Fact]
    public void Compress_FoldsInVariable()
    {
        var vm = NewVm();
        vm.Entries.Add(new PathEntry(@"C:\Users\me\AppData\Local\Programs\x", PathScope.User));

        vm.CompressCommand.Execute(null);

        Assert.Equal(@"%LOCALAPPDATA%\Programs\x", vm.Entries[0].RawToken);
    }

    [Fact]
    public void RemoveDuplicates_WhenPreviewCancelled_DoesNotMutateEntries()
    {
        var vm = NewVm();
        vm.Entries.Add(new PathEntry(@"C:\bin", PathScope.User));
        vm.Entries.Add(EntryFactory.With(@"C:\bin\", PathScope.User, DiagnosticKind.DuplicateL2));
        MaintenancePreview? captured = null;
        vm.ConfirmMaintenance = preview =>
        {
            captured = preview;
            return false;
        };

        vm.RemoveDuplicatesCommand.Execute(null);

        Assert.Equal(2, vm.Entries.Count);
        Assert.NotNull(captured);
        Assert.Single(captured.Changes);
        Assert.Contains("first occurrence", captured.Summary);
    }

    [Fact]
    public void Compress_PreviewShowsBeforeAndAfter()
    {
        var vm = NewVm();
        vm.Entries.Add(new PathEntry(@"C:\Users\me\AppData\Local\Programs\x", PathScope.User));
        MaintenancePreview? captured = null;
        vm.ConfirmMaintenance = preview =>
        {
            captured = preview;
            return false;
        };

        vm.CompressCommand.Execute(null);

        var change = Assert.Single(Assert.IsType<MaintenancePreview>(captured).Changes);
        Assert.Equal(@"C:\Users\me\AppData\Local\Programs\x", change.Before);
        Assert.Equal(@"%LOCALAPPDATA%\Programs\x", change.After);
    }

    [Fact]
    public void RemoveBroken_PreviewIncludesScope_AndAllowsIndividualExclusion()
    {
        var vm = NewVm();
        vm.Entries.Add(EntryFactory.Missing(@"C:\gone", PathScope.User));
        vm.Entries.Add(EntryFactory.Empty(PathScope.User));
        vm.ConfirmMaintenance = preview =>
        {
            Assert.Equal(PathScope.User, preview.Scope);
            preview.Changes[0].IsSelected = false;
            return true;
        };

        vm.RemoveBrokenCommand.Execute(null);

        Assert.Single(vm.Entries);
        Assert.Equal(@"C:\gone", vm.Entries[0].RawToken);
    }

    [Fact]
    public void Normalize_NoChanges_StillShowsInformationalPreview()
    {
        var vm = NewVm();
        vm.Entries.Add(new PathEntry(@"C:\bin", PathScope.User));
        MaintenancePreview? captured = null;
        vm.ConfirmMaintenance = preview =>
        {
            captured = preview;
            return true;
        };

        vm.NormalizeCommand.Execute(null);

        Assert.NotNull(captured);
        Assert.False(captured.HasChanges);
        Assert.Equal(@"C:\bin", vm.Entries[0].RawToken);
    }

    [Fact]
    public void Normalize_WhenConfirmed_AppliesSelectedChange()
    {
        var vm = NewVm();
        vm.Entries.Add(new PathEntry(@"C:\bin\", PathScope.User));
        vm.ConfirmMaintenance = _ => true;

        vm.NormalizeCommand.Execute(null);

        Assert.Equal(@"C:\bin", vm.Entries[0].RawToken);
    }
}
