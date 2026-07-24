using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EnvMaid.App.Models;
using EnvMaid.App.Services;

namespace EnvMaid.App.ViewModels;

public partial class ConflictsViewModel : ObservableObject
{
    private readonly ConflictAnalysisService _analysis;
    private readonly PathListViewModel _userPaths;
    private readonly PathListViewModel _systemPaths;

    /// <summary>Asks the user to confirm a loser-folder deletion, given its coverage
    /// report. Returns true to proceed. Set by the view.</summary>
    public Func<ConflictLocation, IReadOnlyList<CoverageItem>, bool>? ConfirmDelete { get; set; }

    public ObservableCollection<ConflictGroup> Groups { get; } = new();

    [ObservableProperty]
    private ConflictGroup? _selectedGroup;

    /// <summary>Losers of the selected group, each aware of the winner's scope so the
    /// view can bind reorder/advisory visibility without custom converters.</summary>
    public ObservableCollection<LoserItem> SelectedLosers { get; } = new();

    partial void OnSelectedGroupChanged(ConflictGroup? value)
    {
        SelectedLosers.Clear();
        if (value is null)
            return;
        foreach (var loser in value.Losers)
            SelectedLosers.Add(new LoserItem(loser, value.Winner.Scope));
    }

    public bool HasConflicts => Groups.Count > 0;

    public int ConflictCount => Groups.Count;

    /// <summary>Conflict groups touching <paramref name="scope"/> — i.e. whose winner
    /// or any loser folder lives in that scope. A cross-scope group counts for both.</summary>
    public int ConflictCountForScope(PathScope scope) =>
        Groups.Count(g => g.Winner.Scope == scope || g.Losers.Any(l => l.Scope == scope));

    public ConflictsViewModel(
        ConflictAnalysisService analysis,
        PathListViewModel userPaths,
        PathListViewModel systemPaths)
    {
        _analysis = analysis;
        _userPaths = userPaths;
        _systemPaths = systemPaths;
    }

    /// <summary>Recompute conflict groups from the current (staged) entries.</summary>
    public void Refresh()
    {
        var previouslySelected = SelectedGroup?.ExeName;

        Groups.Clear();
        foreach (var group in _analysis.Analyze(
                     _userPaths.Entries.ToList(), _systemPaths.Entries.ToList()))
            Groups.Add(group);

        SelectedGroup = Groups.FirstOrDefault(g => g.ExeName == previouslySelected)
            ?? Groups.FirstOrDefault();

        OnPropertyChanged(nameof(ConflictCount));
        OnPropertyChanged(nameof(HasConflicts));
    }

    /// <summary>
    /// Move the selected alternative directly above the current winner so the
    /// selected copy becomes the version Windows resolves first.
    /// </summary>
    [RelayCommand]
    private void UseVersion(LoserItem? item)
    {
        if (SelectedGroup is null || item is null || !item.CanReorder)
            return;

        var selected = item.Location;
        var current = SelectedGroup.Winner;
        var list = ScopeList(selected.Scope);
        var currentIndex = list.Entries.IndexOf(current.Entry);
        var selectedIndex = list.Entries.IndexOf(selected.Entry);
        if (currentIndex < 0 || selectedIndex < 0 || selectedIndex < currentIndex)
            return;

        list.Entries.Move(selectedIndex, currentIndex);
        Refresh();
    }

    /// <summary>
    /// Remove a loser folder from PATH after confirming what commands it would cost.
    /// </summary>
    [RelayCommand]
    private void DeleteLoser(LoserItem? item)
    {
        if (item is null)
            return;

        var loser = item.Location;
        var coverage = _analysis
            .CoverageAfterRemoving(loser, _userPaths.Entries.ToList(), _systemPaths.Entries.ToList())
            .Select(c => new CoverageItem(c.ExeName, c.CoveredBy))
            .ToList();

        if (ConfirmDelete is not null && !ConfirmDelete(loser, coverage))
            return;

        ScopeList(loser.Scope).Entries.Remove(loser.Entry);
        Refresh();
    }

    private PathListViewModel ScopeList(PathScope scope) =>
        scope == PathScope.User ? _userPaths : _systemPaths;
}

/// <summary>A shadowed loser folder plus the winner's scope, so the view knows
/// whether a same-scope reorder is possible or a cross-scope advisory applies.</summary>
public record LoserItem(ConflictLocation Location, PathScope WinnerScope)
{
    public string DisplayPath => Location.DisplayPath;
    public PathScope Scope => Location.Scope;
    public bool CanReorder => Location.Scope == WinnerScope;
    public bool IsCrossScope => Location.Scope != WinnerScope;
}

/// <summary>One executable's fate if a loser folder is removed: covered elsewhere
/// (<see cref="CoveredBy"/> is the surviving folder) or lost (null).</summary>
public record CoverageItem(string ExeName, string? CoveredBy)
{
    public bool IsCovered => CoveredBy is not null;
}
