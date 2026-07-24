using System.IO;
using EnvMaid.App.Models;
using EnvMaid.App.Services;

namespace EnvMaid.App.Tests;

public class ConflictAnalysisServiceTests
{
    private readonly ConflictAnalysisService _sut = new(new ConflictRanker(
        new CliToolListService(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".txt"))));

    [Fact]
    public void Analyze_SameExeInTwoFolders_GroupsWithWinnerAndLoser()
    {
        var dirA = CreateTempDir();
        var dirB = CreateTempDir();
        File.WriteAllText(Path.Combine(dirA, "git.exe"), "A");
        File.WriteAllText(Path.Combine(dirB, "git.exe"), "bigger B");
        var user = new List<PathEntry> { new(dirA, PathScope.User), new(dirB, PathScope.User) };

        var groups = _sut.Analyze(user, []);

        var group = Assert.Single(groups);
        Assert.Equal("git.exe", group.ExeName);
        Assert.Equal(dirA, group.Winner.DisplayPath);
        var loser = Assert.Single(group.Losers);
        Assert.Equal(dirB, loser.DisplayPath);
    }

    [Fact]
    public void Analyze_SystemResolvesBeforeUser_SystemIsWinner()
    {
        var dirSys = CreateTempDir();
        var dirUser = CreateTempDir();
        File.WriteAllText(Path.Combine(dirSys, "python.exe"), "sys");
        File.WriteAllText(Path.Combine(dirUser, "python.exe"), "user longer");
        var user = new List<PathEntry> { new(dirUser, PathScope.User) };
        var system = new List<PathEntry> { new(dirSys, PathScope.System) };

        var groups = _sut.Analyze(user, system);

        var group = Assert.Single(groups);
        Assert.Equal(PathScope.System, group.Winner.Scope);
        Assert.Equal(dirSys, group.Winner.DisplayPath);
    }

    [Fact]
    public void Analyze_NoSharedExe_NoGroups()
    {
        var dirA = CreateTempDir();
        var dirB = CreateTempDir();
        File.WriteAllText(Path.Combine(dirA, "git.exe"), "A");
        File.WriteAllText(Path.Combine(dirB, "node.exe"), "B");
        var user = new List<PathEntry> { new(dirA, PathScope.User), new(dirB, PathScope.User) };

        Assert.Empty(_sut.Analyze(user, []));
    }

    [Fact]
    public void CoverageAfterRemoving_ExeElsewhere_ReportedCovered()
    {
        var loserDir = CreateTempDir();
        var winnerDir = CreateTempDir();
        File.WriteAllText(Path.Combine(loserDir, "python.exe"), "l");
        File.WriteAllText(Path.Combine(loserDir, "pip.exe"), "l");
        File.WriteAllText(Path.Combine(winnerDir, "python.exe"), "w"); // covers python, not pip
        var winnerEntry = new PathEntry(winnerDir, PathScope.User);
        var loserEntry = new PathEntry(loserDir, PathScope.User);
        var user = new List<PathEntry> { winnerEntry, loserEntry };
        var loserLoc = new ConflictLocation(loserEntry, loserDir);

        var coverage = _sut.CoverageAfterRemoving(loserLoc, user, []);

        var python = coverage.Single(c => c.ExeName == "python.exe");
        var pip = coverage.Single(c => c.ExeName == "pip.exe");
        Assert.Equal(winnerDir, python.CoveredBy); // still reachable
        Assert.Null(pip.CoveredBy);                // lost
    }

    private static string CreateTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(dir);
        return dir;
    }
}
