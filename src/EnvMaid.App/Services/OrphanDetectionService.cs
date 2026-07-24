using System.IO;
using EnvMaid.App.Models;

namespace EnvMaid.App.Services;

public class OrphanDetectionService
{
    private static readonly HashSet<string> ExecutableExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".bat", ".cmd", ".ps1", ".dll"
    };

    public void ApplyFlags(IReadOnlyList<PathEntry> userEntries, IReadOnlyList<PathEntry> systemEntries)
    {
        var allEntries = userEntries.Concat(systemEntries).ToList();
        var byNormalized = allEntries
            .GroupBy(e => Normalize(e.Path))
            .ToDictionary(g => g.Key, g => g.ToList());

        ApplyFlagsForScope(userEntries, byNormalized);
        ApplyFlagsForScope(systemEntries, byNormalized);
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
