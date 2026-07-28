using System.IO;
using EnvMaid.App.Models;

namespace EnvMaid.App.Services;

public class OrphanDetectionService
{
    private static readonly HashSet<string> ExecutableExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".bat", ".cmd", ".ps1", ".dll"
    };

    private static readonly HashSet<string> ShadowCheckExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".bat", ".cmd"
    };

    private readonly ConflictRanker _ranker;

    public OrphanDetectionService(ConflictRanker ranker)
    {
        _ranker = ranker;
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
        // exe name -> the winning (first-seen) folder + full file path of the winner.
        var seenExeNames = new Dictionary<string, (string Normalized, string Display, string WinnerFile)>(StringComparer.OrdinalIgnoreCase);

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
            foreach (var file in files)
            {
                if (!ShadowCheckExtensions.Contains(Path.GetExtension(file)))
                    continue;

                var name = Path.GetFileName(file);
                if (seenExeNames.TryGetValue(name, out var first))
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
                    seenExeNames[name] = (normalizedFolder, expanded, file);
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
