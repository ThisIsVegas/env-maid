using System.IO;
using EnvMaid.App.Models;
using EnvMaid.App.Services;
using EnvMaid.App.ViewModels;

namespace EnvMaid.App.Tests;

public class DashboardViewModelTests
{
    private static ConflictsViewModel EmptyConflicts(PathListViewModel u, PathListViewModel s) =>
        new(new ConflictAnalysisService(new ConflictRanker(new CliToolListService(
            Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".txt")))), u, s);

    [Fact]
    public void Combined_CountsBrokenAcrossBothScopes()
    {
        var user = new PathListViewModel(PathScope.User);
        var system = new PathListViewModel(PathScope.System);
        user.Entries.Add(EntryFactory.Missing("x", PathScope.User));
        system.Entries.Add(EntryFactory.Empty(PathScope.System));

        var vm = new DashboardViewModel(user, system, EmptyConflicts(user, system));
        vm.Refresh();

        Assert.Equal(2, vm.BrokenCount);
    }

    [Fact]
    public void ShowLength_FalseForCombined_TrueForSingleScope()
    {
        var user = new PathListViewModel(PathScope.User);
        var system = new PathListViewModel(PathScope.System);
        var vm = new DashboardViewModel(user, system, EmptyConflicts(user, system));

        Assert.False(vm.ShowLength);           // combined
        vm.Scope = DashboardScope.User;
        Assert.True(vm.ShowLength);
    }

    [Fact]
    public void ScopeUser_ExcludesSystemEntries()
    {
        var user = new PathListViewModel(PathScope.User);
        var system = new PathListViewModel(PathScope.System);
        user.Entries.Add(EntryFactory.Missing("u", PathScope.User));
        system.Entries.Add(EntryFactory.Missing("s", PathScope.System));

        var vm = new DashboardViewModel(user, system, EmptyConflicts(user, system));
        vm.Scope = DashboardScope.User; // triggers Refresh

        Assert.Equal(1, vm.BrokenCount);
    }

    [Fact]
    public void DuplicateCount_CountsDuplicateFlag()
    {
        var user = new PathListViewModel(PathScope.User);
        var system = new PathListViewModel(PathScope.System);
        user.Entries.Add(EntryFactory.Duplicate("dup", PathScope.User));

        var vm = new DashboardViewModel(user, system, EmptyConflicts(user, system));
        vm.Refresh();

        Assert.Equal(1, vm.DuplicateCount);
    }

    [Fact]
    public void HealthSummary_LeadsWithPlainLanguageConclusion()
    {
        var user = new PathListViewModel(PathScope.User);
        var system = new PathListViewModel(PathScope.System);
        user.Entries.Add(EntryFactory.Missing("gone", PathScope.User));
        user.Entries.Add(EntryFactory.Duplicate("dup", PathScope.User));

        var vm = new DashboardViewModel(user, system, EmptyConflicts(user, system));
        vm.Refresh();

        Assert.Equal("2 findings need attention", vm.HealthTitle);
        Assert.Contains("missing or empty location", vm.HealthSummary);
        Assert.Contains("duplicate entry", vm.HealthSummary);
    }
}
