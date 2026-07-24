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

    // Installer/uninstaller name patterns that make a shadow almost certainly noise.
    // Matched case-insensitively against the exe name without extension.
    private static readonly string[] DenylistPrefixes =
    {
        "unins", "setup", "install", "uninstall", "update", "updater",
        "vcredist", "vc_redist", "dotnetfx", "wix", "msiexec"
    };

    private static readonly string[] DenylistContains =
    {
        "redist", "setup", "installer"
    };

    private readonly CliToolListService _cliTools;

    public OrphanDetectionService(CliToolListService cliTools)
    {
        _cliTools = cliTools;
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
                        var confidence = RankConflict(name, first.WinnerFile, file);
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

    /// <summary>
    /// Bands a shadow conflict. First match wins:
    /// denylist name or byte-identical files -> false positive;
    /// known CLI tool -> likely real; otherwise -> possibly.
    /// </summary>
    private ConflictConfidence RankConflict(string exeName, string winnerFile, string loserFile)
    {
        if (IsDenylisted(exeName) || SameFileSize(winnerFile, loserFile))
            return ConflictConfidence.LikelyFalsePositive;

        if (_cliTools.IsKnownCliTool(exeName))
            return ConflictConfidence.LikelyReal;

        return ConflictConfidence.Possibly;
    }

    private static bool IsDenylisted(string exeName)
    {
        var stem = Path.GetFileNameWithoutExtension(exeName);
        return DenylistPrefixes.Any(p => stem.StartsWith(p, StringComparison.OrdinalIgnoreCase))
            || DenylistContains.Any(c => stem.Contains(c, StringComparison.OrdinalIgnoreCase));
    }

    private static bool SameFileSize(string a, string b)
    {
        try
        {
            return new FileInfo(a).Length == new FileInfo(b).Length;
        }
        catch (IOException)
        {
            return false;
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
