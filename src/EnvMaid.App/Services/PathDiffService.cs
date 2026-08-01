using System.IO;
using EnvMaid.App.Models;

namespace EnvMaid.App.Services;

public enum PathChangeKind
{
    Added,
    Removed,
    Changed,
    Moved,
}

public record PathChange(
    PathChangeKind Kind,
    string Path,
    string? Reason = null,
    string? PreviousPath = null,
    int? PreviousPosition = null,
    int? NewPosition = null)
{
    public string DisplayPath => string.IsNullOrWhiteSpace(Path) ? "(empty entry)" : Path;
    public string? DisplayPreviousPath =>
        PreviousPath is null
            ? null
            : string.IsNullOrWhiteSpace(PreviousPath) ? "(empty entry)" : PreviousPath;
}

public record ScopeDiff(
    PathScope Scope,
    IReadOnlyList<PathChange> Changes,
    bool OrderChanged)
{
    public bool HasChanges => Changes.Count > 0;
}

/// <summary>
/// Computes the exact stored-value changes for one PATH scope while using normalized
/// path equality to recognize formatting/compression edits as changes to one location.
/// </summary>
public class PathDiffService
{
    private readonly PathNormalizer _normalizer = new();

    public ScopeDiff Diff(
        PathScope scope,
        IReadOnlyList<string> current,
        IReadOnlyList<string> staged)
    {
        var currentMatched = new bool[current.Count];
        var stagedMatched = new bool[staged.Count];
        var matchedPairs = new List<(int CurrentIndex, int StagedIndex, string Path)>();
        var changes = new List<PathChange>();

        // Match identical stored values first so genuine reorders stay visible.
        for (var currentIndex = 0; currentIndex < current.Count; currentIndex++)
        {
            var stagedIndex = FindMatch(
                staged,
                stagedMatched,
                path => string.Equals(current[currentIndex], path, StringComparison.OrdinalIgnoreCase));
            if (stagedIndex < 0)
                continue;

            currentMatched[currentIndex] = true;
            stagedMatched[stagedIndex] = true;
            matchedPairs.Add((currentIndex, stagedIndex, current[currentIndex]));
        }

        // Pair normalized equivalents next. These are stored-value edits such as
        // trailing-slash normalization or environment-variable compression.
        for (var currentIndex = 0; currentIndex < current.Count; currentIndex++)
        {
            if (currentMatched[currentIndex])
                continue;

            var stagedIndex = FindMatch(
                staged,
                stagedMatched,
                path => _normalizer.AreEquivalent(current[currentIndex], path));
            if (stagedIndex < 0)
                continue;

            currentMatched[currentIndex] = true;
            stagedMatched[stagedIndex] = true;
            matchedPairs.Add((currentIndex, stagedIndex, staged[stagedIndex]));
            changes.Add(new PathChange(
                PathChangeKind.Changed,
                staged[stagedIndex],
                "Stored form changed; the location remains the same.",
                current[currentIndex],
                currentIndex + 1,
                stagedIndex + 1));
        }

        for (var stagedIndex = 0; stagedIndex < staged.Count; stagedIndex++)
            if (!stagedMatched[stagedIndex])
                changes.Add(new PathChange(
                    PathChangeKind.Added,
                    staged[stagedIndex],
                    PreviousPosition: null,
                    NewPosition: stagedIndex + 1));

        for (var currentIndex = 0; currentIndex < current.Count; currentIndex++)
            if (!currentMatched[currentIndex])
                changes.Add(new PathChange(
                    PathChangeKind.Removed,
                    current[currentIndex],
                    RemovalReason(current[currentIndex]),
                    PreviousPosition: currentIndex + 1));

        var currentOrder = matchedPairs.OrderBy(pair => pair.CurrentIndex).ToList();
        var stagedOrder = matchedPairs.OrderBy(pair => pair.StagedIndex).ToList();
        var currentRanks = currentOrder
            .Select((pair, rank) => (pair, rank))
            .ToDictionary(item => item.pair.CurrentIndex, item => item.rank);
        var stagedRanks = stagedOrder
            .Select((pair, rank) => (pair, rank))
            .ToDictionary(item => item.pair.CurrentIndex, item => item.rank);

        foreach (var pair in matchedPairs)
        {
            if (currentRanks[pair.CurrentIndex] == stagedRanks[pair.CurrentIndex])
                continue;

            changes.Add(new PathChange(
                PathChangeKind.Moved,
                pair.Path,
                "Priority changed.",
                PreviousPosition: pair.CurrentIndex + 1,
                NewPosition: pair.StagedIndex + 1));
        }

        return new ScopeDiff(
            scope,
            changes,
            changes.Any(change => change.Kind == PathChangeKind.Moved));
    }

    private static int FindMatch(
        IReadOnlyList<string> paths,
        IReadOnlyList<bool> matched,
        Func<string, bool> predicate)
    {
        for (var index = 0; index < paths.Count; index++)
            if (!matched[index] && predicate(paths[index]))
                return index;
        return -1;
    }

    private static string? RemovalReason(string path)
    {
        var expanded = Environment.ExpandEnvironmentVariables(path);
        if (string.IsNullOrWhiteSpace(expanded))
            return "Empty entry.";
        if (!Directory.Exists(expanded))
            return "Folder did not exist.";
        return null;
    }
}
