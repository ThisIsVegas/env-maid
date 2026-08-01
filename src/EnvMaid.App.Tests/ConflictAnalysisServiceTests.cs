using System.IO;
using EnvMaid.App.Models;
using EnvMaid.App.Services;

namespace EnvMaid.App.Tests;

public class ConflictAnalysisServiceTests
{
    // A fixed PATHEXT, so these never depend on whatever the machine running them has configured.
    private static readonly PathExtService PathExt = new(() => ".COM;.EXE;.BAT;.CMD");

    private readonly ConflictAnalysisService _sut = new(
        new ConflictRanker(new CliToolListService(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".txt"))),
        PathExt);

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
        // The group is keyed on the command as typed, not the filename.
        Assert.Equal("git", group.ExeName);
        Assert.Equal("git.exe", group.WinnerFileName);
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
    public void Analyze_DifferentExtensions_StillCompeteForTheSameCommand()
    {
        // The bug this replaces: foo.bat and foo.exe were treated as unrelated files, so the
        // shadowing between them was invisible. Only one of them ever runs as "tool".
        var dirA = CreateTempDir();
        var dirB = CreateTempDir();
        File.WriteAllText(Path.Combine(dirA, "tool.bat"), "A");
        File.WriteAllText(Path.Combine(dirB, "tool.exe"), "B");
        var user = new List<PathEntry> { new(dirA, PathScope.User), new(dirB, PathScope.User) };

        var group = Assert.Single(_sut.Analyze(user, []));

        Assert.Equal("tool", group.ExeName);
        Assert.Equal("tool.bat", group.WinnerFileName);
        Assert.Equal(dirA, group.Winner.DisplayPath);
        Assert.True(group.ShadowsAcrossExtensions);
    }

    [Fact]
    public void Analyze_DirectoryOrderBeatsExtensionOrder()
    {
        // .COM sorts before .EXE, but the folder listed first still wins: every extension is
        // tried within one folder before moving on to the next.
        var first = CreateTempDir();
        var second = CreateTempDir();
        File.WriteAllText(Path.Combine(first, "cross.exe"), "first");
        File.WriteAllText(Path.Combine(second, "cross.com"), "second");
        var user = new List<PathEntry> { new(first, PathScope.User), new(second, PathScope.User) };

        var group = Assert.Single(_sut.Analyze(user, []));

        Assert.Equal(first, group.Winner.DisplayPath);
        Assert.Equal("cross.exe", group.WinnerFileName);
    }

    [Fact]
    public void Analyze_WithinAFolder_PathExtOrderDecidesTheWinner()
    {
        // Both live in the same folder, so only the PATHEXT-preferred one represents it.
        var dirA = CreateTempDir();
        var dirB = CreateTempDir();
        File.WriteAllText(Path.Combine(dirA, "dual.exe"), "exe");
        File.WriteAllText(Path.Combine(dirA, "dual.com"), "com");
        File.WriteAllText(Path.Combine(dirB, "dual.exe"), "other");
        var user = new List<PathEntry> { new(dirA, PathScope.User), new(dirB, PathScope.User) };

        var group = Assert.Single(_sut.Analyze(user, []));

        Assert.Equal("dual.com", group.WinnerFileName);
    }

    [Fact]
    public void Analyze_IgnoresFilesThatArePathExtNoCommands()
    {
        var dirA = CreateTempDir();
        var dirB = CreateTempDir();
        File.WriteAllText(Path.Combine(dirA, "shared.dll"), "A");
        File.WriteAllText(Path.Combine(dirB, "shared.dll"), "B");
        var user = new List<PathEntry> { new(dirA, PathScope.User), new(dirB, PathScope.User) };

        // A DLL in two folders is not a command conflict — nothing runs by typing "shared".
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

    [Fact]
    public void CoverageAfterRemoving_MatchesOnTheCommandNotTheFilename()
    {
        // Removing a folder holding tool.cmd is safe when another folder has tool.exe — typing
        // "tool" still works. Matching filenames would report a false loss.
        var loserDir = CreateTempDir();
        var coveringDir = CreateTempDir();
        File.WriteAllText(Path.Combine(loserDir, "tool.cmd"), "l");
        File.WriteAllText(Path.Combine(coveringDir, "tool.exe"), "w");
        var coveringEntry = new PathEntry(coveringDir, PathScope.User);
        var loserEntry = new PathEntry(loserDir, PathScope.User);
        var user = new List<PathEntry> { coveringEntry, loserEntry };

        var coverage = _sut.CoverageAfterRemoving(new ConflictLocation(loserEntry, loserDir), user, []);

        Assert.Equal(coveringDir, Assert.Single(coverage).CoveredBy);
    }

    private static string CreateTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(dir);
        return dir;
    }
}
