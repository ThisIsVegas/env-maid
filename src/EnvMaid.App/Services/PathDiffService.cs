using System.IO;
using EnvMaid.App.Models;

namespace EnvMaid.App.Services;

public enum PathChangeKind { Added, Removed }

/// <summary><see cref="Reason"/> annotates a removal (e.g. "Folder does not exist")
/// so the review dialog explains why the entry was dropped; null for additions.</summary>
public record PathChange(PathChangeKind Kind, string Path, string? Reason = null)
{
    /// <summary>Human-readable path text, so an empty PATH entry doesn't render as a bare dash.</summary>
    public string DisplayPath => string.IsNullOrWhiteSpace(Path) ? "(empty entry)" : Path;
}

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
                changes.Add(new PathChange(PathChangeKind.Removed, path, RemovalReason(path)));

        // Order changed: the entries present in both, compared in each list's order.
        var currentSurviving = current.Where(stagedSet.Contains).ToList();
        var stagedSurviving = staged.Where(currentSet.Contains).ToList();
        var orderChanged = !currentSurviving.SequenceEqual(stagedSurviving, StringComparer.OrdinalIgnoreCase);

        return new ScopeDiff(scope, changes, orderChanged);
    }

    // Best-effort explanation for why a removed entry was likely flagged. Covers the
    // unambiguous cases (empty / missing folder); other removals just show no reason.
    private static string? RemovalReason(string path)
    {
        var expanded = Environment.ExpandEnvironmentVariables(path);
        if (string.IsNullOrWhiteSpace(expanded))
            return "empty entry";
        if (!Directory.Exists(expanded))
            return "folder did not exist";
        return null;
    }
}
