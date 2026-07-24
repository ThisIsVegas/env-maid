using EnvMaid.App.Models;

namespace EnvMaid.App.Services;

public enum PathChangeKind { Added, Removed }

public record PathChange(PathChangeKind Kind, string Path);

/// <summary>Changes to one scope's PATH: additions, removals, and whether the
/// surviving entries were reordered.</summary>
public record ScopeDiff(
    PathScope Scope,
    IReadOnlyList<PathChange> Changes,
    bool OrderChanged)
{
    public bool HasChanges => Changes.Count > 0 || OrderChanged;
}

/// <summary>
/// Computes what a save would do: compares the current (real) PATH against the
/// staged entries per scope. Order comparison ignores added/removed entries and
/// only asks whether the entries present in both kept their relative order.
/// </summary>
public class PathDiffService
{
    public ScopeDiff Diff(PathScope scope, IReadOnlyList<string> current, IReadOnlyList<string> staged)
    {
        var currentSet = new HashSet<string>(current, StringComparer.OrdinalIgnoreCase);
        var stagedSet = new HashSet<string>(staged, StringComparer.OrdinalIgnoreCase);

        var changes = new List<PathChange>();
        foreach (var path in staged)
            if (!currentSet.Contains(path))
                changes.Add(new PathChange(PathChangeKind.Added, path));
        foreach (var path in current)
            if (!stagedSet.Contains(path))
                changes.Add(new PathChange(PathChangeKind.Removed, path));

        // Order changed: the entries present in both, compared in each list's order.
        var currentSurviving = current.Where(stagedSet.Contains).ToList();
        var stagedSurviving = staged.Where(currentSet.Contains).ToList();
        var orderChanged = !currentSurviving.SequenceEqual(stagedSurviving, StringComparer.OrdinalIgnoreCase);

        return new ScopeDiff(scope, changes, orderChanged);
    }
}
