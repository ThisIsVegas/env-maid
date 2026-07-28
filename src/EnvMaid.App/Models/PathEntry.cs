using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace EnvMaid.App.Models;

public partial class PathEntry : ObservableObject
{
    [ObservableProperty]
    private string _path;

    [ObservableProperty]
    private string _reason = string.Empty;

    [ObservableProperty]
    private FlagConfidence _confidence = FlagConfidence.None;

    [ObservableProperty]
    private PathFlag _flags = PathFlag.None;

    [ObservableProperty]
    private bool _isChecked;

    [ObservableProperty]
    private int _globalRank;

    /// <summary>
    /// This entry sits past the point where some other PATH-writing tools mishandle the value.
    /// Not a truncation point — nothing is dropped here, and editing it in EnvMaid is safe.
    /// </summary>
    [ObservableProperty]
    private bool _isPastLengthLimit;

    /// <summary>The last entry before the caution boundary; where the marker row is drawn.</summary>
    [ObservableProperty]
    private bool _isLengthLimitBoundary;

    /// <summary>
    /// This entry sits past what a single environment variable can hold. A PATH reaching here
    /// cannot be written at all, and Save is refused until it is shortened.
    /// </summary>
    [ObservableProperty]
    private bool _isPastWriteLimit;

    /// <summary>The last entry before the write limit; where the second marker row is drawn.</summary>
    [ObservableProperty]
    private bool _isWriteLimitBoundary;

    public ObservableCollection<ShadowConflict> ShadowConflicts { get; } = new();

    public bool HasShadowConflicts => ShadowConflicts.Count > 0;

    public bool HasAttention => Flags != PathFlag.None || HasShadowConflicts;

    public string StatusLabel
    {
        get
        {
            if (Flags.HasFlag(PathFlag.Missing)) return "Missing location";
            if (Flags.HasFlag(PathFlag.Empty)) return "Empty entry";
            if (Flags.HasFlag(PathFlag.Duplicate)) return "Duplicate";
            if (HasShadowConflicts) return "Command priority";
            if (Flags.HasFlag(PathFlag.NoExecutable)) return "Review";
            return string.Empty;
        }
    }

    /// <summary>Worst (most-real) confidence among this entry's shadow conflicts,
    /// used to color the grid's conflict marker. Null when there are none.</summary>
    public ConflictConfidence? ShadowConfidence =>
        ShadowConflicts.Count == 0 ? null : ShadowConflicts.Max(c => c.Confidence);

    public PathScope Scope { get; }

    public PathEntry(string path, PathScope scope)
    {
        _path = path;
        Scope = scope;
        ShadowConflicts.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasShadowConflicts));
            OnPropertyChanged(nameof(HasAttention));
            OnPropertyChanged(nameof(StatusLabel));
            OnPropertyChanged(nameof(ShadowConfidence));
        };
    }

    partial void OnFlagsChanged(PathFlag value)
    {
        OnPropertyChanged(nameof(HasAttention));
        OnPropertyChanged(nameof(StatusLabel));
    }
}
