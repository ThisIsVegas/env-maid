using EnvMaid.App.Models;
using EnvMaid.App.Services;
using EnvMaid.App.ViewModels;
using Microsoft.Win32;

namespace EnvMaid.App.Tests;

/// <summary>
/// The §14 save sequence: detect an external change between scan and save, resolve it, write,
/// and verify the read-back. Everything runs against the fake store — no registry involved.
/// </summary>
public class MainViewModelSaveTests
{
    private const string Path = EnvironmentPathService.PathValueName;

    private static VariableValue Expand(string raw) =>
        VariableValue.Of(RegistryValueKind.ExpandString, raw);

    private static (MainViewModel Vm, FakeEnvironmentVariableStore Store, BackupService Backups) Build(
        string userPath = @"C:\a;C:\b", string systemPath = @"C:\sys")
    {
        var store = new FakeEnvironmentVariableStore();
        store.Seed(PathScope.User, Path, Expand(userPath));
        store.Seed(PathScope.System, Path, Expand(systemPath));

        var cliTools = new CliToolListService(
            System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid() + ".txt"));
        var ranker = new ConflictRanker(cliTools);
        var backups = new BackupService(
            System.IO.Path.Combine(System.IO.Path.GetTempPath(), "EnvMaidTests", Guid.NewGuid().ToString()));

        var vm = new MainViewModel(
            new EnvironmentPathService(store),
            new OrphanDetectionService(ranker),
            new ConflictAnalysisService(ranker),
            backups,
            new PathDiffService(),
            cliTools)
        {
            ConfirmSave = _ => true,
        };

        // Scan happened in the constructor; clear what it recorded so assertions see only the save.
        store.Writes.Clear();
        return (vm, store, backups);
    }

    [Fact]
    public void Save_WithNoExternalChange_WritesTheStagedValue()
    {
        var (vm, store, _) = Build();
        vm.UserPaths.Entries.RemoveAt(1);

        vm.SaveCommand.Execute(null);

        var write = Assert.Single(store.Writes);
        Assert.Equal(PathScope.User, write.Scope);
        Assert.Equal(@"C:\a", write.Value.RawData);
    }

    [Fact]
    public void Save_WhenAScopeChangedExternally_AndNoDelegateIsSet_LeavesItAlone()
    {
        var (vm, store, _) = Build();
        vm.ResolveConflict = null;          // an unconfigured view
        vm.UserPaths.Entries.RemoveAt(1);

        // An installer appends to User PATH after our scan.
        store.Seed(PathScope.User, Path, Expand(@"C:\a;C:\b;C:\installed"));

        vm.SaveCommand.Execute(null);

        // Cancel, not overwrite: the installer's entry survives.
        Assert.Empty(store.Writes);
        Assert.Equal(@"C:\a;C:\b;C:\installed", store.Read(PathScope.User, Path).RawData);
    }

    [Fact]
    public void Save_WhenTheUserChoosesOverwrite_WritesOverTheExternalChange()
    {
        var (vm, store, _) = Build();
        vm.ResolveConflict = _ => ConflictResolution.Overwrite;
        vm.UserPaths.Entries.RemoveAt(1);

        store.Seed(PathScope.User, Path, Expand(@"C:\a;C:\b;C:\installed"));

        vm.SaveCommand.Execute(null);

        Assert.Equal(@"C:\a", Assert.Single(store.Writes).Value.RawData);
    }

    [Fact]
    public void Save_ConflictPrompt_SeparatesTheirChangesFromMine()
    {
        var (vm, store, _) = Build();
        ConflictPrompt? seen = null;
        vm.ResolveConflict = prompt => { seen = prompt; return ConflictResolution.Cancel; };

        vm.UserPaths.Entries.RemoveAt(1);                                  // mine: removed C:\b
        store.Seed(PathScope.User, Path, Expand(@"C:\a;C:\b;C:\installed")); // theirs: added C:\installed

        vm.SaveCommand.Execute(null);

        Assert.NotNull(seen);
        Assert.Equal(PathScope.User, seen!.Scope);
        Assert.Contains(seen.ExternalChangeSummary, s => s.Contains(@"C:\installed"));
        Assert.Contains(seen.PendingChangeSummary, s => s.Contains(@"C:\b"));
    }

    [Fact]
    public void Save_ConflictInOneScope_DoesNotBlockTheOther()
    {
        // The System scope carries the external change; the User scope is clean. Cancelling
        // System must still let the User edit through — §13 partial success.
        var (vm, store, _) = Build();
        vm.ResolveConflict = _ => ConflictResolution.Cancel;

        vm.UserPaths.Entries.RemoveAt(1);
        store.Seed(PathScope.System, Path, Expand(@"C:\sys;C:\installed"));

        vm.SaveCommand.Execute(null);

        var write = Assert.Single(store.Writes);
        Assert.Equal(PathScope.User, write.Scope);
        Assert.Equal(@"C:\a", write.Value.RawData);
        Assert.Equal(@"C:\sys;C:\installed", store.Read(PathScope.System, Path).RawData);
    }

    [Fact]
    public void Save_BacksUpWhatIsOnDiskNow_NotTheStaleBaseline()
    {
        var (vm, store, backups) = Build();
        vm.ResolveConflict = _ => ConflictResolution.Overwrite;
        vm.UserPaths.Entries.RemoveAt(1);

        store.Seed(PathScope.User, Path, Expand(@"C:\a;C:\b;C:\installed"));

        vm.SaveCommand.Execute(null);

        // The backup has to be able to bring back the value actually replaced — including the
        // installer's entry, which the scan baseline never saw.
        var backup = Assert.Single(backups.ListBackups());
        Assert.Equal(@"C:\a;C:\b;C:\installed", backups.LoadBackup(backup.FullName).UserPath);
    }

    [Fact]
    public void Save_TypeChangeAlone_CountsAsAConflict()
    {
        var (vm, store, _) = Build();
        var prompted = false;
        vm.ResolveConflict = _ => { prompted = true; return ConflictResolution.Cancel; };

        vm.UserPaths.Entries.RemoveAt(1);

        // Same characters, different registry type — an installer rewriting REG_EXPAND_SZ as
        // REG_SZ is the §4.2 corruption, and overwriting it silently would erase the evidence.
        store.Seed(PathScope.User, Path, VariableValue.Of(RegistryValueKind.String, @"C:\a;C:\b"));

        vm.SaveCommand.Execute(null);

        Assert.True(prompted);
    }
}
