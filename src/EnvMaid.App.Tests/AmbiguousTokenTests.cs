using System.IO;
using EnvMaid.App.Models;
using EnvMaid.App.Services;
using EnvMaid.App.ViewModels;

namespace EnvMaid.App.Tests;

/// <summary>
/// A single token that contributes several directories. Windows supports this — the environment
/// builder expands the value and consumers then split on ';' — so EnvMaid protects such a token
/// rather than treating it as damage.
/// </summary>
public class AmbiguousTokenTests : IDisposable
{
    private readonly List<string> _tempDirs = new();
    private readonly List<string> _variables = new();

    private OrphanDetectionService Service(bool longPathsEnabled = false) => new(
        new ConflictRanker(new CliToolListService(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".txt"))),
        new PathExtService(() => ".COM;.EXE;.BAT;.CMD"),
        identities: null,
        longPaths: new LongPathSupport(() => longPathsEnabled));

    [Theory]
    [InlineData(@"C:\simple", 1, false)]
    [InlineData(@"C:\a;C:\b", 2, true)]
    [InlineData(@"C:\a;C:\b;C:\c", 3, true)]
    public void EffectiveDirectories_CountsWhatTheTokenContributes(string raw, int expected, bool ambiguous)
    {
        var entry = new PathEntry(raw, PathScope.User);

        Assert.Equal(expected, entry.EffectiveDirectories.Count);
        Assert.Equal(ambiguous, entry.IsStructurallyAmbiguous);
    }

    [Fact]
    public void AVariableHoldingTwoDirectories_IsAmbiguousAfterExpansion()
    {
        var a = NewDir();
        var b = NewDir();
        SetVariable("ENVMAID_TOOLCHAIN", $"{a};{b}");

        var entry = new PathEntry(@"%ENVMAID_TOOLCHAIN%", PathScope.User);

        Assert.True(entry.IsStructurallyAmbiguous);
        Assert.Equal(new[] { a, b }, entry.EffectiveDirectories);
    }

    [Fact]
    public void AWorkingMultiDirectoryToken_IsNotReportedAsMissing()
    {
        // The live defect: the whole expanded string went to Directory.Exists, always failed,
        // and the entry was flagged as a missing folder — pre-checked for deletion.
        var a = NewDir();
        var b = NewDir();
        SetVariable("ENVMAID_TOOLCHAIN", $"{a};{b}");
        var entries = new List<PathEntry> { new(@"%ENVMAID_TOOLCHAIN%", PathScope.User) };

        Service().Analyze(entries, []);

        Assert.False(entries[0].Has(DiagnosticKind.FolderMissing));
        Assert.Equal(ExistenceStatus.Exists, entries[0].ExistenceStatus);
        Assert.True(entries[0].Has(DiagnosticKind.StructurallyAmbiguous));
        Assert.False(entries[0].IsChecked);
    }

    [Fact]
    public void OneMissingDirectoryOfTwo_IsSaidExactly()
    {
        var real = NewDir();
        var gone = Path.Combine(Path.GetTempPath(), "envmaid-gone-" + Guid.NewGuid().ToString("N")[..8]);
        SetVariable("ENVMAID_TOOLCHAIN", $"{real};{gone}");
        var entries = new List<PathEntry> { new(@"%ENVMAID_TOOLCHAIN%", PathScope.User) };

        Service().Analyze(entries, []);

        // Neither wholly present nor wholly absent, and the message names which part is missing.
        Assert.Equal(ExistenceStatus.Unknown, entries[0].ExistenceStatus);
        Assert.Contains("1 of 2", entries[0].Reason + string.Join(" ", entries[0].Diagnostics.Select(d => d.Message)));
        Assert.False(entries[0].IsChecked);
    }

    [Fact]
    public void AllDirectoriesMissing_IsStillReportedAsMissing()
    {
        var goneA = Path.Combine(Path.GetTempPath(), "envmaid-gone-" + Guid.NewGuid().ToString("N")[..8]);
        var goneB = Path.Combine(Path.GetTempPath(), "envmaid-gone-" + Guid.NewGuid().ToString("N")[..8]);
        SetVariable("ENVMAID_TOOLCHAIN", $"{goneA};{goneB}");
        var entries = new List<PathEntry> { new(@"%ENVMAID_TOOLCHAIN%", PathScope.User) };

        Service().Analyze(entries, []);

        Assert.Equal(ExistenceStatus.Missing, entries[0].ExistenceStatus);
        Assert.True(entries[0].Has(DiagnosticKind.FolderMissing));

        // Still not auto-selected: StructurallyAmbiguous is unsafe, and one unsafe finding vetoes.
        Assert.False(entries[0].IsChecked);
    }

    [Fact]
    public void AnAmbiguousTokenIsExcludedFromDuplicateDetection()
    {
        var a = NewDir();
        SetVariable("ENVMAID_TOOLCHAIN", $"{a};{a}");
        var entries = new List<PathEntry>
        {
            new(a, PathScope.User),
            new(@"%ENVMAID_TOOLCHAIN%", PathScope.User),
        };

        Service().Analyze(entries, []);

        // It has no single directory identity to compare, so it is not a duplicate of anything.
        Assert.False(entries[1].IsDuplicate);
    }

    [Fact]
    public void ShadowAnalysisStillSeesEachEffectiveDirectory()
    {
        // Maintenance refuses an ambiguous token, but analysis must not — those directories are
        // genuinely on the search path and can genuinely shadow something.
        var winner = NewDir();
        var shadowed = NewDir();
        File.WriteAllText(Path.Combine(winner, "tool.exe"), "first");
        File.WriteAllText(Path.Combine(shadowed, "tool.exe"), "second and longer");
        SetVariable("ENVMAID_TOOLCHAIN", $"{NewDir()};{shadowed}");

        var entries = new List<PathEntry>
        {
            new(winner, PathScope.User),
            new(@"%ENVMAID_TOOLCHAIN%", PathScope.User),
        };

        Service().Analyze(entries, []);

        var conflict = Assert.Single(entries[1].ShadowConflicts);
        Assert.Equal("tool.exe", conflict.ExeName);
    }

    [Fact]
    public void NormalizeAndCompress_LeaveAnAmbiguousTokenAlone()
    {
        var vm = new PathListViewModel(PathScope.User, new PathNormalizer(),
            new PathCompressor(name => name == "LOCALAPPDATA" ? @"C:\Users\me\AppData\Local" : null));

        // A trailing slash that Normalize would otherwise strip.
        vm.Entries.Add(new PathEntry(@"C:\a\;C:\b\", PathScope.User));
        vm.ConfirmMaintenance = _ => true;

        vm.NormalizeCommand.Execute(null);
        vm.CompressCommand.Execute(null);

        Assert.Equal(@"C:\a\;C:\b\", vm.Entries[0].RawToken);
    }

    [Fact]
    public void ALongDirectory_IsFlaggedAccordingToMachineState()
    {
        var longPath = @"C:\" + new string('x', LongPathSupport.MaxPath);
        var entries = new List<PathEntry> { new(longPath, PathScope.User) };

        Service(longPathsEnabled: false).Analyze(entries, []);
        var warned = entries[0].Diagnostics.Single(d => d.Kind == DiagnosticKind.ExceedsMaxPath);
        Assert.Equal(Severity.Warning, warned.Severity);

        entries = new List<PathEntry> { new(longPath, PathScope.User) };
        Service(longPathsEnabled: true).Analyze(entries, []);
        var noted = entries[0].Diagnostics.Single(d => d.Kind == DiagnosticKind.ExceedsMaxPath);
        Assert.Equal(Severity.Info, noted.Severity);

        // A long path is usually a working directory, so it is never pre-checked for removal.
        Assert.False(noted.SafeToAutoSelect);
    }

    [Fact]
    public void AShortTokenExpandingPastTheLimit_IsStillFlagged()
    {
        // Measured on the expanded directory, not the raw token.
        SetVariable("ENVMAID_DEEP", @"C:\" + new string('y', LongPathSupport.MaxPath));
        var entries = new List<PathEntry> { new(@"%ENVMAID_DEEP%", PathScope.User) };

        Service().Analyze(entries, []);

        Assert.True(entries[0].Has(DiagnosticKind.ExceedsMaxPath));
    }

    private string NewDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "envmaid-amb-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        _tempDirs.Add(dir);
        return dir;
    }

    private void SetVariable(string name, string value)
    {
        Environment.SetEnvironmentVariable(name, value);
        _variables.Add(name);
    }

    public void Dispose()
    {
        foreach (var name in _variables)
            Environment.SetEnvironmentVariable(name, null);

        foreach (var dir in _tempDirs)
        {
            try
            {
                Directory.Delete(dir, recursive: true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
