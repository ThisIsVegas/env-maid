using System.Diagnostics;
using System.IO;
using EnvMaid.App.Models;
using EnvMaid.App.Services;

namespace EnvMaid.App.Tests;

/// <summary>
/// The four duplicate levels. L1–L3 are textual and run anywhere; the L4 tests create real
/// junctions, which needs no elevation.
/// </summary>
public class DuplicateLevelTests : IDisposable
{
    private readonly OrphanDetectionService _sut = new(
        new ConflictRanker(new CliToolListService(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".txt"))),
        new PathExtService(() => ".COM;.EXE;.BAT;.CMD"));

    private readonly List<string> _tempDirs = new();
    private readonly List<string> _junctions = new();

    private static DiagnosticKind? LevelOf(PathEntry entry) => entry.Diagnostics
        .Where(d => d.Kind.ToString().StartsWith("Duplicate", StringComparison.Ordinal))
        .Select(d => (DiagnosticKind?)d.Kind)
        .FirstOrDefault();

    [Fact]
    public void IdenticalTokens_AreL1()
    {
        var dir = NewDir();
        var entries = new List<PathEntry> { new(dir, PathScope.User), new(dir, PathScope.User) };

        _sut.Analyze(entries, []);

        Assert.Equal(DiagnosticKind.DuplicateL1, LevelOf(entries[1]));
        Assert.True(entries[1].IsChecked);
    }

    [Fact]
    public void CaseOrTrailingSeparatorDifference_IsL2_NotL1()
    {
        var dir = NewDir();
        var entries = new List<PathEntry>
        {
            new(dir, PathScope.User),
            new(dir.ToUpperInvariant() + @"\", PathScope.User),
        };

        _sut.Analyze(entries, []);

        // Levels are most-specific-first, so this is L2 and never also reported as L1.
        Assert.Equal(DiagnosticKind.DuplicateL2, LevelOf(entries[1]));
        Assert.True(entries[1].IsChecked);
    }

    [Fact]
    public void TwoTokensExpandingToTheSameFolder_AreL3_AndAreNotAutoSelected()
    {
        var dir = NewDir();
        Environment.SetEnvironmentVariable("ENVMAID_TEST_HOME", dir);
        try
        {
            var entries = new List<PathEntry>
            {
                new(dir, PathScope.User),
                new(@"%ENVMAID_TEST_HOME%", PathScope.User),
            };

            _sut.Analyze(entries, []);

            Assert.Equal(DiagnosticKind.DuplicateL3, LevelOf(entries[1]));

            // The two are maintained separately: removing one silently would leave the survivor
            // no longer tracking the variable the other followed.
            Assert.False(entries[1].IsChecked);
            Assert.Contains("same folder today", entries[1].Reason);
        }
        finally
        {
            Environment.SetEnvironmentVariable("ENVMAID_TEST_HOME", null);
        }
    }

    [Fact]
    public void AJunctionToAnEntry_IsL4_AndIsAdvisoryOnly()
    {
        var real = NewDir();
        var junction = Path.Combine(Path.GetTempPath(), "envmaid-junction-" + Guid.NewGuid().ToString("N")[..8]);

        if (!TryCreateJunction(junction, real))
            return; // junction creation unavailable here; the textual levels still cover the rest

        var entries = new List<PathEntry> { new(real, PathScope.User), new(junction, PathScope.User) };

        _sut.Analyze(entries, []);

        // Textual comparison sees two unrelated paths; the filesystem says one directory.
        Assert.Equal(DiagnosticKind.DuplicateL4, LevelOf(entries[1]));
        Assert.False(entries[1].IsChecked);
        Assert.Equal(Severity.Info, entries[1].Diagnostics.First(d => d.Kind == DiagnosticKind.DuplicateL4).Severity);
    }

    [Fact]
    public void DistinctFoldersOnTheSameVolume_AreNotDuplicates()
    {
        // The guard against the struct-layout bug that made every volume look identical: two
        // genuinely different directories must not match on identity.
        var a = NewDir();
        var b = NewDir();
        var entries = new List<PathEntry> { new(a, PathScope.User), new(b, PathScope.User) };

        _sut.Analyze(entries, []);

        Assert.Null(LevelOf(entries[1]));
    }

    [Fact]
    public void DriveRootsAreDistinct_DespiteSharingAFileId()
    {
        // Volume roots share file ID ...0005 across volumes, so matching on the file ID alone
        // collides. The volume serial is what keeps them apart.
        var identities = new DirectoryIdentityService();
        var roots = DriveInfo.GetDrives()
            .Where(d => d.IsReady)
            .Select(d => identities.Resolve(d.RootDirectory.FullName))
            .Where(i => i is not null)
            .ToList();

        if (roots.Count < 2)
            return; // single-volume machine; nothing to distinguish

        Assert.All(roots, r => Assert.NotEqual(0u, r!.VolumeSerialNumber));
        Assert.Equal(roots.Count, roots.Select(r => r!.VolumeSerialNumber).Distinct().Count());
    }

    [Fact]
    public void UnresolvableEntries_AreStillL1WhenTheTokenRepeats_ButNeverBeyond()
    {
        // An undefined variable could expand to anything, so nothing past L1 can be claimed —
        // but listing the same broken token twice is still listing it twice.
        var entries = new List<PathEntry>
        {
            new(@"%ENVMAID_NOT_DEFINED%\bin", PathScope.User),
            new(@"%ENVMAID_NOT_DEFINED%\bin", PathScope.User),
            new(@"%ENVMAID_ALSO_NOT_DEFINED%\bin", PathScope.User),
        };

        _sut.Analyze(entries, []);

        Assert.Equal(DiagnosticKind.DuplicateL1, LevelOf(entries[1]));
        Assert.Null(LevelOf(entries[2]));
    }

    private string NewDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "envmaid-dup-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        // An executable, so the folder does not also pick up NoExecutables — which is unsafe to
        // auto-select and would veto the entry, hiding whether the duplicate level itself is safe.
        File.WriteAllText(Path.Combine(dir, "tool.exe"), "x");
        _tempDirs.Add(dir);
        return dir;
    }

    private bool TryCreateJunction(string link, string target)
    {
        var psi = new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{link}\" \"{target}\"")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var process = Process.Start(psi);
        process?.WaitForExit(10_000);

        if (!Directory.Exists(link))
            return false;

        _junctions.Add(link);
        return true;
    }

    public void Dispose()
    {
        // Deleting a junction removes the link, not the target it points at.
        foreach (var junction in _junctions)
            TryDelete(() => Directory.Delete(junction));

        foreach (var dir in _tempDirs)
            TryDelete(() => Directory.Delete(dir, recursive: true));
    }

    private static void TryDelete(Action delete)
    {
        try
        {
            delete();
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
