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

    private static void ApplyShadowFlags(IReadOnlyList<PathEntry> resolutionOrderEntries)
    {
        var seenExeNames = new Dictionary<string, (string Normalized, string Display)>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in resolutionOrderEntries)
        {
            var expanded = Environment.ExpandEnvironmentVariables(entry.Path);
            if (!Directory.Exists(expanded))
                continue;

            var normalizedFolder = Normalize(entry.Path);

            var shadowedFrom = new List<string>();
            foreach (var file in Directory.EnumerateFiles(expanded))
            {
                if (!ShadowCheckExtensions.Contains(Path.GetExtension(file)))
                    continue;

                var name = Path.GetFileName(file);
                if (seenExeNames.TryGetValue(name, out var first))
                {
                    if (!string.Equals(first.Normalized, normalizedFolder, StringComparison.OrdinalIgnoreCase)
                        && !shadowedFrom.Contains(first.Display))
                        shadowedFrom.Add(first.Display);
                }
                else
                {
                    seenExeNames[name] = (normalizedFolder, expanded);
                }
            }

            if (shadowedFrom.Count == 0)
                continue;

            var shadowReason = $"Shadowed by {string.Join(", ", shadowedFrom)}";
            entry.Reason = string.IsNullOrEmpty(entry.Reason)
                ? shadowReason
                : $"{entry.Reason}; {shadowReason}";

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
