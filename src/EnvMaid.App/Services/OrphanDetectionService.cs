using System.IO;
using EnvMaid.App.Models;

namespace EnvMaid.App.Services;

public class OrphanDetectionService
{
    /// <summary>
    /// Answers "does this folder look useful?" — deliberately <em>not</em> derived from PATHEXT.
    /// </summary>
    /// <remarks>
    /// <c>.dll</c> is the clearest reason the two sets differ: it will never be in PATHEXT, but a
    /// DLL-only folder on PATH is legitimate because the loader searches PATH as its last stage.
    /// Deriving this from PATHEXT would flag such a folder as having no executables, which is
    /// wrong. <c>.ps1</c> is similar — PowerShell runs it, but it is not a bare command.
    /// </remarks>
    private static readonly HashSet<string> ExecutableExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".bat", ".cmd", ".ps1", ".dll"
    };

    private readonly ConflictRanker _ranker;
    private readonly PathExtService _pathExt;

    public OrphanDetectionService(ConflictRanker ranker, PathExtService? pathExt = null)
    {
        _ranker = ranker;
        _pathExt = pathExt ?? new PathExtService();
    }

    public void ApplyFlags(IReadOnlyList<PathEntry> userEntries, IReadOnlyList<PathEntry> systemEntries)
    {
        // System first, matching real PATH resolution order. Grouping does not depend on the
        // order, but every composition site in the app uses the same one so that none of them
        // has to be re-derived when someone reads it.
        var allEntries = systemEntries.Concat(userEntries).ToList();
        var byNormalized = allEntries
            .GroupBy(e => Normalize(e.Path))
            .ToDictionary(g => g.Key, g => g.ToList());

        ApplyFlagsForScope(userEntries, byNormalized);
        ApplyFlagsForScope(systemEntries, byNormalized);

        // Real PATH resolution order: System entries first, then User entries appended.
        ApplyShadowFlags(systemEntries.Concat(userEntries).ToList());
    }

    private void ApplyShadowFlags(IReadOnlyList<PathEntry> resolutionOrderEntries)
    {
        // command name (no extension) -> the winning folder + the file that actually runs.
        var seenCommands = new Dictionary<string, (string Normalized, string Display, string WinnerFile)>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in resolutionOrderEntries)
        {
            var expanded = Environment.ExpandEnvironmentVariables(entry.Path);
            if (!Directory.Exists(expanded))
                continue;

            var files = TryEnumerateFiles(expanded);
            if (files is null)
                continue;

            var normalizedFolder = Normalize(entry.Path);

            entry.ShadowConflicts.Clear();
            foreach (var (command, file) in WinnersPerCommand(files))
            {
                var name = Path.GetFileName(file);
                if (seenCommands.TryGetValue(command, out var first))
                {
                    if (!string.Equals(first.Normalized, normalizedFolder, StringComparison.OrdinalIgnoreCase)
                        && !entry.ShadowConflicts.Any(c => c.ExeName == name))
                    {
                        var confidence = _ranker.Rank(name, first.WinnerFile, file);
                        entry.ShadowConflicts.Add(new ShadowConflict(name, first.Display, confidence));
                    }
                }
                else
                {
                    seenCommands[command] = (normalizedFolder, expanded, file);
                }
            }

            if (entry.ShadowConflicts.Count == 0)
                continue;

            if (entry.Confidence == FlagConfidence.None)
                entry.Confidence = FlagConfidence.Low;
        }
    }

    private void ApplyFlagsForScope(IReadOnlyList<PathEntry> scopeEntries, Dictionary<string, List<PathEntry>> byNormalized)
    {
        var seenNormalized = new HashSet<string>();

        foreach (var entry in scopeEntries)
        {
            var (existenceReason, existenceConfidence, existenceFlag) = CheckExistence(entry.Path);
            var normalized = Normalize(entry.Path);
            var group = byNormalized[normalized];

            var isFirstOccurrence = seenNormalized.Add(normalized);
            var duplicateReason = !isFirstOccurrence ? BuildDuplicateReason(entry, group) : null;

            var reasonParts = new List<string>();
            if (existenceReason is not null) reasonParts.Add(existenceReason);
            if (duplicateReason is not null) reasonParts.Add(duplicateReason);

            entry.Reason = string.Join("; ", reasonParts);

            entry.Flags = existenceFlag | (duplicateReason is not null ? PathFlag.Duplicate : PathFlag.None);

            entry.Confidence = duplicateReason is not null
                ? FlagConfidence.High
                : existenceConfidence;

            entry.IsChecked = entry.Confidence == FlagConfidence.High;
        }
    }

    private static string? BuildDuplicateReason(PathEntry entry, List<PathEntry> group)
    {
        var otherScopes = group.Where(g => g.Scope != entry.Scope).Select(g => g.Scope).Distinct().ToList();
        if (otherScopes.Count > 0)
            return $"Duplicate (also in {otherScopes[0]} PATH)";

        return "Duplicate entry";
    }

    private (string? Reason, FlagConfidence Confidence, PathFlag Flag) CheckExistence(string path)
    {
        var expanded = Environment.ExpandEnvironmentVariables(path);

        if (string.IsNullOrWhiteSpace(expanded))
            return ("Empty entry", FlagConfidence.High, PathFlag.Empty);

        if (!Directory.Exists(expanded))
            return ("Folder does not exist", FlagConfidence.High, PathFlag.Missing);

        var files = TryEnumerateFiles(expanded);
        if (files is null)
            // The folder is there but we are not allowed to list it. Reporting "no executables"
            // would invite the user to delete a folder that may be full of them.
            return ("Folder exists but could not be read", FlagConfidence.None, PathFlag.None);

        var hasExecutable = files.Any(f => ExecutableExtensions.Contains(Path.GetExtension(f)));

        if (!hasExecutable)
            return ("No executable-type files found", FlagConfidence.Low, PathFlag.NoExecutable);

        return (null, FlagConfidence.None, PathFlag.None);
    }

    /// <summary>
    /// Lists a folder's files, or returns <c>null</c> when it exists but cannot be read.
    /// </summary>
    /// <remarks>
    /// <c>Directory.Exists</c> answers "is there a folder here", not "may I list it" — a folder
    /// on PATH under another user's profile answers yes to the first and throws on the second.
    /// Returning null keeps that distinct from a genuinely empty folder.
    /// </remarks>
    /// <summary>
    /// For one folder's files, the file that would actually run for each command it provides.
    /// </summary>
    /// <remarks>
    /// Within a folder PATHEXT order decides, so a <c>foo.com</c> beside a <c>foo.exe</c> means
    /// the <c>.exe</c> never runs by that name and does not represent the folder.
    /// </remarks>
    private IEnumerable<(string Command, string File)> WinnersPerCommand(IReadOnlyList<string> files)
    {
        var best = new Dictionary<string, (string File, int Precedence)>(StringComparer.OrdinalIgnoreCase);

        foreach (var file in files)
        {
            var precedence = _pathExt.PrecedenceOf(Path.GetExtension(file));
            if (precedence < 0)
                continue; // not runnable as a bare command

            var command = Path.GetFileNameWithoutExtension(file);
            if (command.Length == 0)
                continue;

            if (!best.TryGetValue(command, out var current) || precedence < current.Precedence)
                best[command] = (file, precedence);
        }

        return best.Select(kv => (kv.Key, kv.Value.File));
    }

    // Not directly unit-tested: reproducing it needs a folder with a deny ACL, which means
    // mutating machine state from a test. The behaviour is one try/catch; the risk it removes
    // (mislabelling an unreadable folder "no executables") is what earns the branch.
    private static IReadOnlyList<string>? TryEnumerateFiles(string expandedFolder)
    {
        try
        {
            return Directory.GetFiles(expandedFolder);
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    private static string Normalize(string path)
    {
        var expanded = Environment.ExpandEnvironmentVariables(path);
        return expanded.TrimEnd('\\').ToLowerInvariant();
    }
}
