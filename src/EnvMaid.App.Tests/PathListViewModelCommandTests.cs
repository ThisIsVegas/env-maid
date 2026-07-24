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

        Assert.Equal(@"C:\bin", vm.Entries[0].Path);
        Assert.Equal(@"%JAVA_HOME%\bin", vm.Entries[1].Path);
    }

    [Fact]
    public void RemoveDuplicates_KeepsFirst_CollapsesSlashVariants()
    {
        var vm = NewVm();
        vm.Entries.Add(new PathEntry(@"C:\bin", PathScope.User));
        vm.Entries.Add(new PathEntry(@"C:\bin\", PathScope.User)); // same folder, trailing slash
        vm.Entries.Add(new PathEntry(@"C:\other", PathScope.User));

        vm.RemoveDuplicatesCommand.Execute(null);

        Assert.Equal(2, vm.Entries.Count);
        Assert.Equal(@"C:\bin", vm.Entries[0].Path);
        Assert.Equal(@"C:\other", vm.Entries[1].Path);
    }

    [Fact]
    public void RemoveBroken_RemovesMissingAndEmpty_KeepsOthers()
    {
        var vm = NewVm();
        vm.Entries.Add(new PathEntry(@"C:\good", PathScope.User) { Flags = PathFlag.None });
        vm.Entries.Add(new PathEntry(@"C:\gone", PathScope.User) { Flags = PathFlag.Missing });
        vm.Entries.Add(new PathEntry("", PathScope.User) { Flags = PathFlag.Empty });

        vm.RemoveBrokenCommand.Execute(null);

        Assert.Single(vm.Entries);
        Assert.Equal(@"C:\good", vm.Entries[0].Path);
    }

    [Fact]
    public void Compress_FoldsInVariable()
    {
        var vm = NewVm();
        vm.Entries.Add(new PathEntry(@"C:\Users\me\AppData\Local\Programs\x", PathScope.User));

        vm.CompressCommand.Execute(null);

        Assert.Equal(@"%LOCALAPPDATA%\Programs\x", vm.Entries[0].Path);
    }

    [Fact]
    public void RemoveDuplicates_WhenPreviewCancelled_DoesNotMutateEntries()
    {
        var vm = NewVm();
        vm.Entries.Add(new PathEntry(@"C:\bin", PathScope.User));
        vm.Entries.Add(new PathEntry(@"C:\bin\", PathScope.User));
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
        vm.Entries.Add(new PathEntry(@"C:\gone", PathScope.User) { Flags = PathFlag.Missing });
        vm.Entries.Add(new PathEntry("", PathScope.User) { Flags = PathFlag.Empty });
        vm.ConfirmMaintenance = preview =>
        {
            Assert.Equal(PathScope.User, preview.Scope);
            preview.Changes[0].IsSelected = false;
            return true;
        };

        vm.RemoveBrokenCommand.Execute(null);

        Assert.Single(vm.Entries);
        Assert.Equal(@"C:\gone", vm.Entries[0].Path);
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
        Assert.Equal(@"C:\bin", vm.Entries[0].Path);
    }

    [Fact]
    public void Normalize_WhenConfirmed_AppliesSelectedChange()
    {
        var vm = NewVm();
        vm.Entries.Add(new PathEntry(@"C:\bin\", PathScope.User));
        vm.ConfirmMaintenance = _ => true;

        vm.NormalizeCommand.Execute(null);

        Assert.Equal(@"C:\bin", vm.Entries[0].Path);
    }
}
