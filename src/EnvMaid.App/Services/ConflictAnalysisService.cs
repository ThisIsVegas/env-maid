using System.IO;
using EnvMaid.App.Models;

namespace EnvMaid.App.Services;

/// <summary>
/// Builds <see cref="ConflictGroup"/>s for the Conflicts tab: executables provided
/// by more than one PATH folder, grouped by exe name in resolution order (System
/// before User), with the winner and shadowed losers identified and banded.
/// Also answers coverage questions for the delete prompt.
/// </summary>
public class ConflictAnalysisService
{
    private static readonly HashSet<string> ShadowCheckExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".bat", ".cmd"
    };

    private readonly ConflictRanker _ranker;

    public ConflictAnalysisService(ConflictRanker ranker) => _ranker = ranker;

    /// <summary>
    /// Groups shadowed executables across both scopes. Resolution order is System
    /// entries first, then User entries (matches the real PATH lookup order).
    /// </summary>
    public IReadOnlyList<ConflictGroup> Analyze(
        IReadOnlyList<PathEntry> userEntries,
        IReadOnlyList<PathEntry> systemEntries)
    {
        var resolutionOrder = systemEntries.Concat(userEntries).ToList();

        // exe name -> ordered list of (location, full file path), winner first.
        var byExe = new Dictionary<string, List<(ConflictLocation Loc, string File)>>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in resolutionOrder)
        {
            var expanded = Environment.ExpandEnvironmentVariables(entry.Path);
            if (!Directory.Exists(expanded))
                continue;

            var location = new ConflictLocation(entry, expanded);
            var seenInThisFolder = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var file in EnumerateExecutables(expanded))
            {
                var name = Path.GetFileName(file);
                if (!seenInThisFolder.Add(name))
                    continue; // one entry per exe per folder

                if (!byExe.TryGetValue(name, out var list))
                    byExe[name] = list = new List<(ConflictLocation, string)>();

                // Only add a folder once per exe (a folder can appear twice on PATH).
                if (!list.Any(x => PathsEqual(x.Loc.ExpandedFolder, expanded)))
                    list.Add((location, file));
            }
        }

        var groups = new List<ConflictGroup>();
        foreach (var (exeName, locations) in byExe)
        {
            if (locations.Count < 2)
                continue; // no conflict

            var winner = locations[0];
            var losers = locations.Skip(1).ToList();

            // Band the group by its most-severe loser (the worst real problem present).
            var confidence = losers
                .Select(l => _ranker.Rank(exeName, winner.File, l.File))
                .Max();

            groups.Add(new ConflictGroup(
                exeName,
                confidence,
                winner.Loc,
                losers.Select(l => l.Loc).ToList()));
        }

        // Most-serious conflicts first, then alphabetically.
        return groups
            .OrderByDescending(g => g.Confidence)
            .ThenBy(g => g.ExeName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// For the delete prompt: which executables in <paramref name="folder"/> would
    /// still be reachable via another PATH folder after this folder is removed.
    /// Returns each exe name paired with the winning folder that still provides it
    /// (null = lost, no other folder provides it).
    /// </summary>
    public IReadOnlyList<(string ExeName, string? CoveredBy)> CoverageAfterRemoving(
        ConflictLocation folderToRemove,
        IReadOnlyList<PathEntry> userEntries,
        IReadOnlyList<PathEntry> systemEntries)
    {
        var others = systemEntries.Concat(userEntries)
            .Where(e => !ReferenceEquals(e, folderToRemove.Entry))
            .Select(e => (Entry: e, Expanded: Environment.ExpandEnvironmentVariables(e.Path)))
            .Where(x => Directory.Exists(x.Expanded)
                        && !PathsEqual(x.Expanded, folderToRemove.ExpandedFolder))
            .ToList();

        var result = new List<(string, string?)>();
        foreach (var file in EnumerateExecutables(folderToRemove.ExpandedFolder))
        {
            var name = Path.GetFileName(file);
            var coveringFolder = others
                .FirstOrDefault(o => File.Exists(Path.Combine(o.Expanded, name)));
            result.Add((name, coveringFolder.Entry?.Path));
        }
        return result;
    }

    private static IEnumerable<string> EnumerateExecutables(string folder)
    {
        IEnumerable<string> files;
        try
        {
            files = Directory.EnumerateFiles(folder);
        }
        catch (IOException)
        {
            yield break;
        }

        foreach (var file in files)
            if (ShadowCheckExtensions.Contains(Path.GetExtension(file)))
                yield return file;
    }

    private static bool PathsEqual(string a, string b) =>
        string.Equals(a.TrimEnd('\\'), b.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
}
