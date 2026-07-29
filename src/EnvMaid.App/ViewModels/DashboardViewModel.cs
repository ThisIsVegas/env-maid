using CommunityToolkit.Mvvm.ComponentModel;
using EnvMaid.App.Models;
using EnvMaid.App.Services;

namespace EnvMaid.App.ViewModels;

public enum DashboardScope { Combined, User, System }

/// <summary>
/// Plain-language health summary plus issue counts for the overview.
/// Recomputes from the current staged entries on demand.
/// </summary>
public partial class DashboardViewModel : ObservableObject
{

    private readonly PathListViewModel _userPaths;
    private readonly PathListViewModel _systemPaths;
    private readonly ConflictsViewModel _conflicts;

    [ObservableProperty]
    private DashboardScope _scope = DashboardScope.Combined;

    [ObservableProperty] private int _brokenCount;
    [ObservableProperty] private int _duplicateCount;
    [ObservableProperty] private int _conflictCount;
    [ObservableProperty] private int _totalLength;
    [ObservableProperty] private string _lengthLabel = "0 characters";
    [ObservableProperty] private bool _lengthOverLimit;

    public int AttentionCount => BrokenCount + DuplicateCount + ConflictCount;
    public bool HasAttention => AttentionCount > 0;
    public int EntryCount => _userPaths.Entries.Count + _systemPaths.Entries.Count;
    public string HealthTitle => HasAttention
        ? $"{AttentionCount} {(AttentionCount == 1 ? "finding needs" : "findings need")} attention"
        : "Your PATH looks good";
    public string HealthSummary
    {
        get
        {
            if (!HasAttention)
                return $"{EntryCount} locations checked. No action is needed.";

            var parts = new List<string>();
            if (BrokenCount > 0)
                parts.Add($"{BrokenCount} missing or empty {(BrokenCount == 1 ? "location" : "locations")}");
            if (DuplicateCount > 0)
                parts.Add($"{DuplicateCount} duplicate {(DuplicateCount == 1 ? "entry" : "entries")}");
            if (ConflictCount > 0)
                parts.Add($"{ConflictCount} command-priority {(ConflictCount == 1 ? "concern" : "concerns")}");
            return SentenceList(parts) + ".";
        }
    }

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

        BrokenCount = entries.Count(e => e.IsBroken);
        DuplicateCount = entries.Count(e => e.IsDuplicate);
        ConflictCount = Scope switch
        {
            DashboardScope.User => _conflicts.ConflictCountForScope(PathScope.User),
            DashboardScope.System => _conflicts.ConflictCountForScope(PathScope.System),
            _ => _conflicts.ConflictCount,
        };

        TotalLength = ScopedLength();
        LengthLabel = $"{TotalLength:N0} characters";
        // Red only for the band that blocks a save; the caution band is common and permanent.
        LengthOverLimit = PathLengthLimits.BandFor(TotalLength) == PathLengthBand.TooLong;

        OnPropertyChanged(nameof(AttentionCount));
        OnPropertyChanged(nameof(HasAttention));
        OnPropertyChanged(nameof(EntryCount));
        OnPropertyChanged(nameof(HealthTitle));
        OnPropertyChanged(nameof(HealthSummary));
    }

    private static string SentenceList(IReadOnlyList<string> parts) => parts.Count switch
    {
        0 => "No issues detected",
        1 => Capitalize(parts[0]),
        2 => $"{Capitalize(parts[0])} and {parts[1]}",
        _ => $"{Capitalize(parts[0])}, {string.Join(", ", parts.Skip(1).Take(parts.Count - 2))}, and {parts[^1]}",
    };

    private static string Capitalize(string value) =>
        string.IsNullOrEmpty(value) ? value : char.ToUpperInvariant(value[0]) + value[1..];

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
