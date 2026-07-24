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
    private bool _isChecked;

    [ObservableProperty]
    private bool _isExpanded;

    [ObservableProperty]
    private int _globalRank;

    [ObservableProperty]
    private bool _isPastLengthLimit;

    [ObservableProperty]
    private bool _isLengthLimitBoundary;

    public ObservableCollection<ShadowConflict> ShadowConflicts { get; } = new();

    public bool HasShadowConflicts => ShadowConflicts.Count > 0;

    public PathScope Scope { get; }

    public PathEntry(string path, PathScope scope)
    {
        _path = path;
        Scope = scope;
        ShadowConflicts.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasShadowConflicts));
    }
}
