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
        var allEntries = userEntries.Concat(systemEntries).ToList();
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

            var normalizedFolder = Normalize(entry.Path);

            entry.ShadowConflicts.Clear();
            foreach (var file in Directory.EnumerateFiles(expanded))
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
            var (existenceReason, existenceConfidence) = CheckExistence(entry.Path);
            var normalized = Normalize(entry.Path);
            var group = byNormalized[normalized];

            var isFirstOccurrence = seenNormalized.Add(normalized);
            var duplicateReason = !isFirstOccurrence ? BuildDuplicateReason(entry, group) : null;

            var reasonParts = new List<string>();
            if (existenceReason is not null) reasonParts.Add(existenceReason);
            if (duplicateReason is not null) reasonParts.Add(duplicateReason);

            entry.Reason = string.Join("; ", reasonParts);

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

    private (string? Reason, FlagConfidence Confidence) CheckExistence(string path)
    {
        var expanded = Environment.ExpandEnvironmentVariables(path);

        if (string.IsNullOrWhiteSpace(expanded))
            return ("Empty entry", FlagConfidence.High);

        if (!Directory.Exists(expanded))
            return ("Folder does not exist", FlagConfidence.High);

        var hasExecutable = Directory.EnumerateFiles(expanded)
            .Any(f => ExecutableExtensions.Contains(Path.GetExtension(f)));

        if (!hasExecutable)
            return ("No executable-type files found", FlagConfidence.Low);

        return (null, FlagConfidence.None);
    }

    private static string Normalize(string path)
    {
        var expanded = Environment.ExpandEnvironmentVariables(path);
        return expanded.TrimEnd('\\').ToLowerInvariant();
    }
}
