using System.IO;
using EnvMaid.App.Models;
using EnvMaid.App.Services;
using EnvMaid.App.ViewModels;

namespace EnvMaid.App.Tests;

public class ConflictsViewModelTests
{
    private static ConflictAnalysisService Analysis() =>
        new(new ConflictRanker(new CliToolListService(
            Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".txt"))));

    [Fact]
    public void PickWinner_SameScope_MovesWinnerAboveLoser()
    {
        var winnerDir = CreateTempDir();
        var loserDir = CreateTempDir();
        File.WriteAllText(Path.Combine(winnerDir, "git.exe"), "w");
        File.WriteAllText(Path.Combine(loserDir, "git.exe"), "loser bigger");

        var user = new PathListViewModel(PathScope.User);
        var system = new PathListViewModel(PathScope.System);
        // loser first so winner is actually behind it
        user.LoadEntries(new[] { loserDir, winnerDir });

        var vm = new ConflictsViewModel(Analysis(), user, system);
        vm.Refresh();

        // winner is loserDir's competitor: whichever resolves first is winner.
        // Here loserDir is first in list, so it's the winner initially.
        // Re-point: we want to verify reordering happens. Grab the loser item.
        var group = Assert.Single(vm.Groups);
        var loserItem = new LoserItem(group.Losers[0], group.Winner.Scope);

        var before = user.Entries.Select(e => e.Path).ToList();
        vm.PickWinnerOverCommand.Execute(loserItem);
        var after = user.Entries.Select(e => e.Path).ToList();

        // Winner folder now sits at or above the loser's old position.
        Assert.True(after.IndexOf(group.Winner.DisplayPath) <= before.IndexOf(group.Losers[0].DisplayPath));
    }

    [Fact]
    public void DeleteLoser_RemovesFolderFromScope()
    {
        var winnerDir = CreateTempDir();
        var loserDir = CreateTempDir();
        File.WriteAllText(Path.Combine(winnerDir, "git.exe"), "w");
        File.WriteAllText(Path.Combine(loserDir, "git.exe"), "loser bigger");

        var user = new PathListViewModel(PathScope.User);
        var system = new PathListViewModel(PathScope.System);
        user.LoadEntries(new[] { winnerDir, loserDir });

        var vm = new ConflictsViewModel(Analysis(), user, system) { ConfirmDelete = (_, _) => true };
        vm.Refresh();

        var group = Assert.Single(vm.Groups);
        var loserItem = new LoserItem(group.Losers[0], group.Winner.Scope);
        var loserPath = group.Losers[0].DisplayPath;

        vm.DeleteLoserCommand.Execute(loserItem);

        Assert.DoesNotContain(user.Entries, e => e.Path == loserPath);
        Assert.Empty(vm.Groups); // conflict resolved
    }

    [Fact]
    public void DeleteLoser_WhenConfirmReturnsFalse_KeepsFolder()
    {
        var winnerDir = CreateTempDir();
        var loserDir = CreateTempDir();
        File.WriteAllText(Path.Combine(winnerDir, "git.exe"), "w");
        File.WriteAllText(Path.Combine(loserDir, "git.exe"), "loser bigger");

        var user = new PathListViewModel(PathScope.User);
        var system = new PathListViewModel(PathScope.System);
        user.LoadEntries(new[] { winnerDir, loserDir });

        var vm = new ConflictsViewModel(Analysis(), user, system) { ConfirmDelete = (_, _) => false };
        vm.Refresh();
        var loserItem = new LoserItem(vm.Groups[0].Losers[0], vm.Groups[0].Winner.Scope);

        vm.DeleteLoserCommand.Execute(loserItem);

        Assert.Equal(2, user.Entries.Count); // nothing removed
    }

    [Fact]
    public void CrossScopeLoser_CannotReorder()
    {
        var sysDir = CreateTempDir();
        var userDir = CreateTempDir();
        File.WriteAllText(Path.Combine(sysDir, "python.exe"), "s");
        File.WriteAllText(Path.Combine(userDir, "python.exe"), "user longer");

        var user = new PathListViewModel(PathScope.User);
        var system = new PathListViewModel(PathScope.System);
        user.LoadEntries(new[] { userDir });
        system.LoadEntries(new[] { sysDir });

        var vm = new ConflictsViewModel(Analysis(), user, system);
        vm.Refresh();

        var loser = Assert.Single(vm.SelectedLosers);
        Assert.True(loser.IsCrossScope);
        Assert.False(loser.CanReorder);
    }

    [Fact]
    public void ConflictCountForScope_CrossScopeGroup_CountsForBothScopes()
    {
        var sysDir = CreateTempDir();
        var userDir = CreateTempDir();
        File.WriteAllText(Path.Combine(sysDir, "python.exe"), "s");
        File.WriteAllText(Path.Combine(userDir, "python.exe"), "user longer");

        var user = new PathListViewModel(PathScope.User);
        var system = new PathListViewModel(PathScope.System);
        user.LoadEntries(new[] { userDir });
        system.LoadEntries(new[] { sysDir });

        var vm = new ConflictsViewModel(Analysis(), user, system);
        vm.Refresh();

        // The single group has a System winner and a User loser, so it is relevant
        // to both scope views — not just the combined total.
        Assert.Equal(1, vm.ConflictCount);
        Assert.Equal(1, vm.ConflictCountForScope(PathScope.User));
        Assert.Equal(1, vm.ConflictCountForScope(PathScope.System));
    }

    [Fact]
    public void ConflictCountForScope_SingleScopeGroup_CountsOnlyForThatScope()
    {
        var winnerDir = CreateTempDir();
        var loserDir = CreateTempDir();
        File.WriteAllText(Path.Combine(winnerDir, "git.exe"), "w");
        File.WriteAllText(Path.Combine(loserDir, "git.exe"), "loser bigger");

        var user = new PathListViewModel(PathScope.User);
        var system = new PathListViewModel(PathScope.System);
        user.LoadEntries(new[] { winnerDir, loserDir }); // both folders in User scope

        var vm = new ConflictsViewModel(Analysis(), user, system);
        vm.Refresh();

        Assert.Equal(1, vm.ConflictCountForScope(PathScope.User));
        Assert.Equal(0, vm.ConflictCountForScope(PathScope.System));
    }

    private static string CreateTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(dir);
        return dir;
    }
}
