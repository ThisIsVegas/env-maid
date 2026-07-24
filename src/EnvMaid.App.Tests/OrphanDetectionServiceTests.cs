using System.IO;
using EnvMaid.App.Models;
using EnvMaid.App.Services;

namespace EnvMaid.App.Tests;

public class OrphanDetectionServiceTests
{
    // Point the CLI-tool list at a nonexistent user file so tests depend only on
    // the built-in allowlist, not on whatever is in the real %APPDATA%.
    private readonly OrphanDetectionService _sut = new(
        new CliToolListService(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".txt")));

    [Fact]
    public void ApplyFlags_EmptyPath_FlagsHighConfidence()
    {
        var user = new List<PathEntry> { new("", PathScope.User) };

        _sut.ApplyFlags(user, []);

        Assert.Equal("Empty entry", user[0].Reason);
        Assert.Equal(FlagConfidence.High, user[0].Confidence);
        Assert.True(user[0].IsChecked);
    }

    [Fact]
    public void ApplyFlags_NonExistentFolder_FlagsHighConfidence()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        var user = new List<PathEntry> { new(path, PathScope.User) };

        _sut.ApplyFlags(user, []);

        Assert.Equal("Folder does not exist", user[0].Reason);
        Assert.Equal(FlagConfidence.High, user[0].Confidence);
        Assert.True(user[0].IsChecked);
    }

    [Fact]
    public void ApplyFlags_FolderWithNoExecutables_FlagsLowConfidence()
    {
        var dir = CreateTempDir();
        File.WriteAllText(Path.Combine(dir, "readme.txt"), "hi");
        var user = new List<PathEntry> { new(dir, PathScope.User) };

        _sut.ApplyFlags(user, []);

        Assert.Equal("No executable-type files found", user[0].Reason);
        Assert.Equal(FlagConfidence.Low, user[0].Confidence);
        Assert.False(user[0].IsChecked);
    }

    [Fact]
    public void ApplyFlags_FolderWithExecutable_NotFlagged()
    {
        var dir = CreateTempDir();
        File.WriteAllText(Path.Combine(dir, "tool.exe"), "hi");
        var user = new List<PathEntry> { new(dir, PathScope.User) };

        _sut.ApplyFlags(user, []);

        Assert.Equal(string.Empty, user[0].Reason);
        Assert.Equal(FlagConfidence.None, user[0].Confidence);
        Assert.False(user[0].IsChecked);
    }

    [Fact]
    public void ApplyFlags_DuplicateWithinSameScope_SecondFlaggedAsDuplicate()
    {
        var dir = CreateTempDir();
        File.WriteAllText(Path.Combine(dir, "tool.exe"), "hi");
        var user = new List<PathEntry>
        {
            new(dir, PathScope.User),
            new(dir, PathScope.User),
        };

        _sut.ApplyFlags(user, []);

        Assert.Equal(string.Empty, user[0].Reason);
        Assert.Equal("Duplicate entry", user[1].Reason);
        Assert.Equal(FlagConfidence.High, user[1].Confidence);
        Assert.True(user[1].IsChecked);
    }

    [Fact]
    public void ApplyFlags_SingleEntryEachScope_NeitherFlaggedAsDuplicate()
    {
        // Dedup only triggers on a second occurrence within the same scope's
        // own iteration order, so one entry per scope never cross-flags.
        var dir = CreateTempDir();
        File.WriteAllText(Path.Combine(dir, "tool.exe"), "hi");
        var user = new List<PathEntry> { new(dir, PathScope.User) };
        var system = new List<PathEntry> { new(dir, PathScope.System) };

        _sut.ApplyFlags(user, system);

        Assert.Equal(string.Empty, user[0].Reason);
        Assert.Equal(string.Empty, system[0].Reason);
    }

    [Fact]
    public void ApplyFlags_DuplicateWithinUserScope_ReportsOtherScopeWhenAlsoInSystem()
    {
        var dir = CreateTempDir();
        File.WriteAllText(Path.Combine(dir, "tool.exe"), "hi");
        var user = new List<PathEntry>
        {
            new(dir, PathScope.User),
            new(dir, PathScope.User),
        };
        var system = new List<PathEntry> { new(dir, PathScope.System) };

        _sut.ApplyFlags(user, system);

        Assert.Equal("Duplicate (also in System PATH)", user[1].Reason);
        Assert.Equal(FlagConfidence.High, user[1].Confidence);
    }

    [Fact]
    public void ApplyFlags_NormalizesTrailingSlashAndCase_TreatsAsDuplicate()
    {
        var dir = CreateTempDir();
        File.WriteAllText(Path.Combine(dir, "tool.exe"), "hi");
        var user = new List<PathEntry>
        {
            new(dir, PathScope.User),
            new(dir.ToUpperInvariant() + "\\", PathScope.User),
        };

        _sut.ApplyFlags(user, []);

        Assert.Equal("Duplicate entry", user[1].Reason);
    }

    [Fact]
    public void ApplyFlags_SameExeInTwoDistinctFolders_LaterOneFlaggedAsShadowed()
    {
        var dirA = CreateTempDir();
        var dirB = CreateTempDir();
        File.WriteAllText(Path.Combine(dirA, "java.exe"), "version A");
        File.WriteAllText(Path.Combine(dirB, "java.exe"), "different version B");
        var user = new List<PathEntry>
        {
            new(dirA, PathScope.User),
            new(dirB, PathScope.User),
        };

        _sut.ApplyFlags(user, []);

        Assert.Equal(string.Empty, user[0].Reason);
        Assert.Empty(user[0].ShadowConflicts);
        Assert.Equal(FlagConfidence.Low, user[1].Confidence);
        Assert.False(user[1].IsChecked);
        var conflict = Assert.Single(user[1].ShadowConflicts);
        Assert.Equal("java.exe", conflict.ExeName);
        Assert.Equal(dirA, conflict.ShadowedFolderPath);
        // java is a known CLI tool and the two files differ in size -> likely real.
        Assert.Equal(ConflictConfidence.LikelyReal, conflict.Confidence);
    }

    [Fact]
    public void ApplyFlags_SystemResolvesBeforeUser_UserEntryFlaggedAsShadowed()
    {
        var dirSystem = CreateTempDir();
        var dirUser = CreateTempDir();
        File.WriteAllText(Path.Combine(dirSystem, "python.exe"), "hi");
        File.WriteAllText(Path.Combine(dirUser, "python.exe"), "hi");
        var user = new List<PathEntry> { new(dirUser, PathScope.User) };
        var system = new List<PathEntry> { new(dirSystem, PathScope.System) };

        _sut.ApplyFlags(user, system);

        Assert.Equal(string.Empty, system[0].Reason);
        Assert.Empty(system[0].ShadowConflicts);
        var conflict = Assert.Single(user[0].ShadowConflicts);
        Assert.Equal("python.exe", conflict.ExeName);
        Assert.Equal(dirSystem, conflict.ShadowedFolderPath);
    }

    [Fact]
    public void ApplyFlags_DifferentExeNamesInDifferentFolders_NotFlaggedAsShadowed()
    {
        var dirA = CreateTempDir();
        var dirB = CreateTempDir();
        File.WriteAllText(Path.Combine(dirA, "java.exe"), "hi");
        File.WriteAllText(Path.Combine(dirB, "python.exe"), "hi");
        var user = new List<PathEntry>
        {
            new(dirA, PathScope.User),
            new(dirB, PathScope.User),
        };

        _sut.ApplyFlags(user, []);

        Assert.Equal(string.Empty, user[0].Reason);
        Assert.Equal(string.Empty, user[1].Reason);
    }

    [Fact]
    public void ApplyFlags_ShadowedUninstaller_RankedLikelyFalsePositive()
    {
        var dirA = CreateTempDir();
        var dirB = CreateTempDir();
        File.WriteAllText(Path.Combine(dirA, "unins000.exe"), "a");
        File.WriteAllText(Path.Combine(dirB, "unins000.exe"), "bb"); // different size
        var user = new List<PathEntry> { new(dirA, PathScope.User), new(dirB, PathScope.User) };

        _sut.ApplyFlags(user, []);

        var conflict = Assert.Single(user[1].ShadowConflicts);
        Assert.Equal(ConflictConfidence.LikelyFalsePositive, conflict.Confidence);
    }

    [Fact]
    public void ApplyFlags_ByteIdenticalCopies_RankedLikelyFalsePositive()
    {
        // Same known CLI tool, but byte-identical files -> nobody cares which wins.
        var dirA = CreateTempDir();
        var dirB = CreateTempDir();
        File.WriteAllText(Path.Combine(dirA, "node.exe"), "same");
        File.WriteAllText(Path.Combine(dirB, "node.exe"), "same");
        var user = new List<PathEntry> { new(dirA, PathScope.User), new(dirB, PathScope.User) };

        _sut.ApplyFlags(user, []);

        var conflict = Assert.Single(user[1].ShadowConflicts);
        Assert.Equal(ConflictConfidence.LikelyFalsePositive, conflict.Confidence);
    }

    [Fact]
    public void ApplyFlags_UnknownExeDifferentSizes_RankedPossibly()
    {
        var dirA = CreateTempDir();
        var dirB = CreateTempDir();
        File.WriteAllText(Path.Combine(dirA, "acme.exe"), "a");
        File.WriteAllText(Path.Combine(dirB, "acme.exe"), "bb");
        var user = new List<PathEntry> { new(dirA, PathScope.User), new(dirB, PathScope.User) };

        _sut.ApplyFlags(user, []);

        var conflict = Assert.Single(user[1].ShadowConflicts);
        Assert.Equal(ConflictConfidence.Possibly, conflict.Confidence);
    }

    private static string CreateTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(dir);
        return dir;
    }
}
