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

    public PathScope Scope { get; }

    public PathEntry(string path, PathScope scope)
    {
        _path = path;
        Scope = scope;
    }
}
