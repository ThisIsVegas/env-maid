using System.IO;
using EnvMaid.App.Models;
using EnvMaid.App.Services;

namespace EnvMaid.App.Tests;

public class OrphanDetectionServiceTests
{
    // Point the CLI-tool list at a nonexistent user file so tests depend only on
    // the built-in allowlist, not on whatever is in the real %APPDATA%.
    // A fixed PATHEXT keeps shadow results independent of the machine running them.
    private readonly OrphanDetectionService _sut = new(
        new ConflictRanker(new CliToolListService(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".txt"))),
        new PathExtService(() => ".COM;.EXE;.BAT;.CMD"));

    [Fact]
    public void EmptyToken_IsFlaggedAndSafeToAutoSelect()
    {
        var user = new List<PathEntry> { new("", PathScope.User) };

        _sut.Analyze(user, []);

        Assert.True(user[0].Has(DiagnosticKind.EmptyToken));
        Assert.True(user[0].IsChecked);
    }

    [Fact]
    public void NonExistentFolder_IsFlaggedAndSafeToAutoSelect()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        var user = new List<PathEntry> { new(path, PathScope.User) };

        _sut.Analyze(user, []);

        Assert.True(user[0].Has(DiagnosticKind.FolderMissing));
        Assert.Equal(ExistenceStatus.Missing, user[0].ExistenceStatus);
        Assert.True(user[0].IsChecked);
    }

    [Fact]
    public void UndefinedVariable_IsItsOwnDiagnostic_AndIsNeverAutoSelected()
    {
        // The fix is to define the variable, not to delete the entry — so this must not be
        // reported as a missing folder, and must never arrive pre-checked for removal.
        var user = new List<PathEntry> { new(@"%ENVMAID_NOT_A_REAL_VARIABLE%\bin", PathScope.User) };

        _sut.Analyze(user, []);

        Assert.True(user[0].Has(DiagnosticKind.UnresolvedVariable));
        Assert.False(user[0].Has(DiagnosticKind.FolderMissing));
        Assert.False(user[0].IsChecked);
    }

    [Fact]
    public void QuotedEntry_IsReportedButLeftStored_AndIsNeverAutoSelected()
    {
        var dir = CreateTempDir();
        File.WriteAllText(Path.Combine(dir, "tool.exe"), "hi");
        var user = new List<PathEntry> { new($"\"{dir}\"", PathScope.User) };

        _sut.Analyze(user, []);

        Assert.True(user[0].Has(DiagnosticKind.SurroundingQuotes));
        Assert.False(user[0].IsChecked);

        // Reported, not silently rewritten: the display form is clean, the stored form is not.
        Assert.Equal(dir, user[0].ParsedValue);
        Assert.Equal($"\"{dir}\"", user[0].RawToken);
        Assert.True(user[0].DisplayDiffersFromRaw);
    }

    [Fact]
    public void OneUnsafeDiagnosticVetoesAutoSelection()
    {
        // An exact duplicate is safe on its own. Paired with anything unsafe it is not, because
        // removing the entry would also discard whatever the other finding was about.
        var dir = CreateTempDir();
        var user = new List<PathEntry>
        {
            new(@"%ENVMAID_NOT_A_REAL_VARIABLE%\bin", PathScope.User),
            new(@"%ENVMAID_NOT_A_REAL_VARIABLE%\bin", PathScope.User),
        };

        _sut.Analyze(user, []);

        Assert.True(user[1].Has(DiagnosticKind.DuplicateL1));
        Assert.True(user[1].Has(DiagnosticKind.UnresolvedVariable));
        Assert.False(user[1].IsChecked);
    }

    [Fact]
    public void FolderWithNoExecutables_IsNotedButNotAutoSelected()
    {
        var dir = CreateTempDir();
        File.WriteAllText(Path.Combine(dir, "readme.txt"), "hi");
        var user = new List<PathEntry> { new(dir, PathScope.User) };

        _sut.Analyze(user, []);

        Assert.True(user[0].Has(DiagnosticKind.NoExecutables));
        Assert.Equal(ExistenceStatus.Exists, user[0].ExistenceStatus);
        Assert.False(user[0].IsChecked);
    }

    [Fact]
    public void FolderWithExecutable_HasNothingToReport()
    {
        var dir = CreateTempDir();
        File.WriteAllText(Path.Combine(dir, "tool.exe"), "hi");
        var user = new List<PathEntry> { new(dir, PathScope.User) };

        _sut.Analyze(user, []);

        Assert.Empty(user[0].Diagnostics);
        Assert.Equal(ExistenceStatus.Exists, user[0].ExistenceStatus);
        Assert.False(user[0].IsChecked);
    }

    [Fact]
    public void DuplicateWithinSameScope_SecondIsFlagged()
    {
        var dir = CreateTempDir();
        File.WriteAllText(Path.Combine(dir, "tool.exe"), "hi");
        var user = new List<PathEntry> { new(dir, PathScope.User), new(dir, PathScope.User) };

        _sut.Analyze(user, []);

        Assert.Empty(user[0].Diagnostics);
        Assert.True(user[1].Has(DiagnosticKind.DuplicateL1));
        Assert.True(user[1].IsChecked);
    }

    [Fact]
    public void SameFolderInBothScopes_TheUserCopyIsTheDuplicate()
    {
        // System resolves first, so the User copy is the redundant one. Analysing each scope
        // separately used to mean this was never reported at all.
        var dir = CreateTempDir();
        File.WriteAllText(Path.Combine(dir, "tool.exe"), "hi");
        var user = new List<PathEntry> { new(dir, PathScope.User) };
        var system = new List<PathEntry> { new(dir, PathScope.System) };

        _sut.Analyze(user, system);

        Assert.Empty(system[0].Diagnostics);
        Assert.True(user[0].IsDuplicate);
        Assert.Contains("System", user[0].Reason);
    }

    [Fact]
    public void CaseAndTrailingSlashDifferences_AreADuplicate()
    {
        var dir = CreateTempDir();
        File.WriteAllText(Path.Combine(dir, "tool.exe"), "hi");
        var user = new List<PathEntry>
        {
            new(dir, PathScope.User),
            new(dir.ToUpperInvariant() + "\\", PathScope.User),
        };

        _sut.Analyze(user, []);

        // Same folder written differently — safe to remove, but not the exact same token.
        Assert.True(user[1].Has(DiagnosticKind.DuplicateL2));
        Assert.True(user[1].IsChecked);
    }

    [Fact]
    public void SameExeInTwoDistinctFolders_LaterOneIsShadowed()
    {
        var dirA = CreateTempDir();
        var dirB = CreateTempDir();
        File.WriteAllText(Path.Combine(dirA, "java.exe"), "version A");
        File.WriteAllText(Path.Combine(dirB, "java.exe"), "different version B");
        var user = new List<PathEntry> { new(dirA, PathScope.User), new(dirB, PathScope.User) };

        _sut.Analyze(user, []);

        Assert.Empty(user[0].ShadowConflicts);

        // Being shadowed is a relationship between folders, not a fault in this entry — so it
        // must never make the entry auto-selectable for deletion.
        Assert.False(user[1].IsChecked);
        var conflict = Assert.Single(user[1].ShadowConflicts);
        Assert.Equal("java.exe", conflict.ExeName);
        Assert.Equal(dirA, conflict.ShadowedFolderPath);
        // java is a known CLI tool and the two files differ in size -> likely real.
        Assert.Equal(ConflictConfidence.LikelyReal, conflict.Confidence);
    }

    [Fact]
    public void SystemResolvesBeforeUser_UserEntryIsShadowed()
    {
        var dirSystem = CreateTempDir();
        var dirUser = CreateTempDir();
        File.WriteAllText(Path.Combine(dirSystem, "python.exe"), "hi");
        File.WriteAllText(Path.Combine(dirUser, "python.exe"), "hi");
        var user = new List<PathEntry> { new(dirUser, PathScope.User) };
        var system = new List<PathEntry> { new(dirSystem, PathScope.System) };

        _sut.Analyze(user, system);

        Assert.Empty(system[0].ShadowConflicts);
        var conflict = Assert.Single(user[0].ShadowConflicts);
        Assert.Equal("python.exe", conflict.ExeName);
        Assert.Equal(dirSystem, conflict.ShadowedFolderPath);
    }

    [Fact]
    public void DifferentExeNamesInDifferentFolders_AreNotShadowed()
    {
        var dirA = CreateTempDir();
        var dirB = CreateTempDir();
        File.WriteAllText(Path.Combine(dirA, "java.exe"), "hi");
        File.WriteAllText(Path.Combine(dirB, "python.exe"), "hi");
        var user = new List<PathEntry> { new(dirA, PathScope.User), new(dirB, PathScope.User) };

        _sut.Analyze(user, []);

        Assert.Empty(user[0].ShadowConflicts);
        Assert.Empty(user[1].ShadowConflicts);
    }

    [Fact]
    public void ShadowedUninstaller_RankedLikelyFalsePositive()
    {
        var dirA = CreateTempDir();
        var dirB = CreateTempDir();
        File.WriteAllText(Path.Combine(dirA, "unins000.exe"), "a");
        File.WriteAllText(Path.Combine(dirB, "unins000.exe"), "bb"); // different size
        var user = new List<PathEntry> { new(dirA, PathScope.User), new(dirB, PathScope.User) };

        _sut.Analyze(user, []);

        var conflict = Assert.Single(user[1].ShadowConflicts);
        Assert.Equal(ConflictConfidence.LikelyFalsePositive, conflict.Confidence);
    }

    [Fact]
    public void ByteIdenticalCopies_RankedLikelyFalsePositive()
    {
        // Same known CLI tool, but byte-identical files -> nobody cares which wins.
        var dirA = CreateTempDir();
        var dirB = CreateTempDir();
        File.WriteAllText(Path.Combine(dirA, "node.exe"), "same");
        File.WriteAllText(Path.Combine(dirB, "node.exe"), "same");
        var user = new List<PathEntry> { new(dirA, PathScope.User), new(dirB, PathScope.User) };

        _sut.Analyze(user, []);

        var conflict = Assert.Single(user[1].ShadowConflicts);
        Assert.Equal(ConflictConfidence.LikelyFalsePositive, conflict.Confidence);
    }

    [Fact]
    public void UnknownExeDifferentSizes_RankedPossibly()
    {
        var dirA = CreateTempDir();
        var dirB = CreateTempDir();
        File.WriteAllText(Path.Combine(dirA, "acme.exe"), "a");
        File.WriteAllText(Path.Combine(dirB, "acme.exe"), "bb");
        var user = new List<PathEntry> { new(dirA, PathScope.User), new(dirB, PathScope.User) };

        _sut.Analyze(user, []);

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
