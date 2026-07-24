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

    [ObservableProperty] private int _brokenCount;
    [ObservableProperty] private int _duplicateCount;
    [ObservableProperty] private int _conflictCount;
    [ObservableProperty] private int _totalLength;
    [ObservableProperty] private string _lengthLabel = "0 / 2047";
    [ObservableProperty] private bool _lengthOverLimit;

    // Length for the combined scope is meaningless (User and System are separate
    // variables), so the tile is hidden unless a single scope is selected.
    public bool ShowLength => Scope != DashboardScope.Combined;

    public DashboardViewModel(
        PathListViewModel userPaths, PathListViewModel systemPaths, ConflictsViewModel conflicts)
    {
        _userPaths = userPaths;
        _systemPaths = systemPaths;
        _conflicts = conflicts;
    }

    partial void OnScopeChanged(DashboardScope value)
    {
        OnPropertyChanged(nameof(ShowLength));
        Refresh();
    }

    public void Refresh()
    {
        var entries = ScopedEntries().ToList();

        BrokenCount = entries.Count(e => e.Flags.HasFlag(PathFlag.Missing) || e.Flags.HasFlag(PathFlag.Empty));
        DuplicateCount = entries.Count(e => e.Flags.HasFlag(PathFlag.Duplicate));
        ConflictCount = Scope switch
        {
            DashboardScope.User => _conflicts.ConflictCountForScope(PathScope.User),
            DashboardScope.System => _conflicts.ConflictCountForScope(PathScope.System),
            _ => _conflicts.ConflictCount,
        };

        TotalLength = ScopedLength();
        LengthLabel = $"{TotalLength} / {PathLimit}";
        LengthOverLimit = TotalLength >= PathLimit;
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
