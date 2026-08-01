using CommunityToolkit.Mvvm.ComponentModel;

namespace EnvMaid.App.Models;

public enum MaintenanceChangeKind
{
    Change,
    Remove,
}

public partial class MaintenanceChange : ObservableObject
{
    public MaintenanceChangeKind Kind { get; }
    public string Before { get; }
    public string? After { get; }
    public string Explanation { get; }

    [ObservableProperty]
    private bool _isSelected = true;

    /// <param name="isSelected">
    /// Whether the row starts checked. Rows whose consequence the user needs to weigh — a
    /// duplicate that is only a duplicate today — start unchecked, so acting on them is a
    /// decision rather than the default.
    /// </param>
    public MaintenanceChange(
        MaintenanceChangeKind kind,
        string before,
        string? after,
        string explanation,
        bool isSelected = true)
    {
        Kind = kind;
        Before = before;
        After = after;
        Explanation = explanation;
        _isSelected = isSelected;
    }
}

public record MaintenancePreview(
    PathScope Scope,
    string Title,
    string Summary,
    string ConfirmLabel,
    IReadOnlyList<MaintenanceChange> Changes)
{
    public bool HasChanges => Changes.Count > 0;
    public int SelectedCount => Changes.Count(change => change.IsSelected);
}
