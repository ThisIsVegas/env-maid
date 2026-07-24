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
    /// Move the winner's folder directly above the loser's, so the loser's copy
    /// stops being shadowed. Only valid when both are in the same scope.
    /// </summary>
    [RelayCommand]
    private void PickWinnerOver(LoserItem? item)
    {
        if (SelectedGroup is null || item is null || !item.CanReorder)
            return;

        var loser = item.Location;
        var winner = SelectedGroup.Winner;
        var list = ScopeList(loser.Scope);
        var winnerIndex = list.Entries.IndexOf(winner.Entry);
        var loserIndex = list.Entries.IndexOf(loser.Entry);
        if (winnerIndex < 0 || loserIndex < 0 || winnerIndex < loserIndex)
            return; // already ahead or not found

        list.Entries.Move(winnerIndex, loserIndex);
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
