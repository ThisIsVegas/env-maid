using CommunityToolkit.Mvvm.ComponentModel;
using EnvMaid.App.Models;

namespace EnvMaid.App.ViewModels;

public enum DashboardScope { Combined, User, System }

/// <summary>
/// The always-visible summary strip: worst-severity health label plus counts of
/// broken/duplicate entries, conflicts, and PATH length, filtered by a scope toggle.
/// Recomputes from the current (staged) entries on demand.
/// </summary>
public partial class DashboardViewModel : ObservableObject
{
    private const int PathLimit = 2047;

    private readonly PathListViewModel _userPaths;
    private readonly PathListViewModel _systemPaths;
    private readonly ConflictsViewModel _conflicts;

    [ObservableProperty]
    private DashboardScope _scope = DashboardScope.Combined;

    [ObservableProperty] private string _health = "Healthy";
    [ObservableProperty] private int _brokenCount;
    [ObservableProperty] private int _duplicateCount;
    [ObservableProperty] private int _conflictCount;
    [ObservableProperty] private int _totalLength;
    [ObservableProperty] private string _lengthLabel = "0 / 2047";
    [ObservableProperty] private bool _lengthOverLimit;

    public DashboardViewModel(
        PathListViewModel userPaths, PathListViewModel systemPaths, ConflictsViewModel conflicts)
    {
        _userPaths = userPaths;
        _systemPaths = systemPaths;
        _conflicts = conflicts;
    }

    partial void OnScopeChanged(DashboardScope value) => Refresh();

    public void Refresh()
    {
        var entries = ScopedEntries().ToList();

        BrokenCount = entries.Count(e => e.Flags.HasFlag(PathFlag.Missing) || e.Flags.HasFlag(PathFlag.Empty));
        DuplicateCount = entries.Count(e => e.Flags.HasFlag(PathFlag.Duplicate));
        ConflictCount = _conflicts.ConflictCount;

        TotalLength = ScopedLength();
        LengthLabel = $"{TotalLength} / {PathLimit}";
        LengthOverLimit = TotalLength >= PathLimit;

        Health = ComputeHealth(entries);
    }

    // Worst severity wins: any High-confidence flag -> "Needs attention";
    // else any Low flag -> "Minor issues"; else "Healthy".
    private static string ComputeHealth(IReadOnlyList<PathEntry> entries)
    {
        if (entries.Any(e => e.Confidence == FlagConfidence.High))
            return "Needs attention";
        if (entries.Any(e => e.Confidence == FlagConfidence.Low))
            return "Minor issues";
        return "Healthy";
    }

    private IEnumerable<PathEntry> ScopedEntries() => Scope switch
    {
        DashboardScope.User => _userPaths.Entries,
        DashboardScope.System => _systemPaths.Entries,
        _ => _userPaths.Entries.Concat(_systemPaths.Entries),
    };

    private int ScopedLength() => Scope switch
    {
        DashboardScope.User => _userPaths.TotalLength,
        DashboardScope.System => _systemPaths.TotalLength,
        _ => _userPaths.TotalLength + _systemPaths.TotalLength,
    };
}
