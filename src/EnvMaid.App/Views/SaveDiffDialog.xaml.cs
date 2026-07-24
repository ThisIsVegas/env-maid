using System.Windows;
using EnvMaid.App.Models;
using EnvMaid.App.Services;

namespace EnvMaid.App.Views;

public partial class SaveDiffDialog : Window
{
    public SaveDiffDialog(IReadOnlyList<ScopeDiff> diffs)
    {
        InitializeComponent();

        var changedScopes = diffs.Where(diff => diff.HasChanges).ToList();
        var totalChanges = changedScopes.Sum(ChangeCount);
        ScopeList.ItemsSource = changedScopes.Select(ToViewModel).ToList();
        SummaryText.Text =
            $"{totalChanges} staged {(totalChanges == 1 ? "change" : "changes")} will be saved.";

        var changedScopeNames = changedScopes.Select(diff => diff.Scope.ToString()).ToList();
        ConfirmButton.Content = changedScopeNames.Count == 2
            ? $"Save {totalChanges} changes"
            : $"Save {changedScopeNames[0]} changes";
    }

    private static int ChangeCount(ScopeDiff diff) =>
        diff.Changes.Count;

    private static ScopeDiffView ToViewModel(ScopeDiff diff)
    {
        var changes = diff.Changes
            .OrderBy(change => change.Kind)
            .Select(ToChangeView)
            .ToList();

        var technicalLines = diff.Changes
            .Select(TechnicalLine)
            .ToList();

        var count = ChangeCount(diff);
        var requiresElevation = diff.Scope == PathScope.System;
        return new ScopeDiffView(
            $"{diff.Scope} PATH",
            $"{count} {(count == 1 ? "change" : "changes")}",
            changes,
            technicalLines,
            requiresElevation
                ? "Windows will request administrator approval for these changes."
                : string.Empty,
            Vis(requiresElevation),
            Vis(diff.OrderChanged));
    }

    private static ChangeView ToChangeView(PathChange change)
    {
        var label = change.Kind switch
        {
            PathChangeKind.Added => "ADD",
            PathChangeKind.Removed => "REMOVE",
            PathChangeKind.Changed => "CHANGE",
            PathChangeKind.Moved => "MOVE",
            _ => string.Empty,
        };
        var path = change.Kind == PathChangeKind.Changed
            ? $"{change.DisplayPreviousPath}{Environment.NewLine}→ {change.DisplayPath}"
            : change.DisplayPath;
        var detail = change.Kind == PathChangeKind.Moved
            ? $"Position {change.PreviousPosition} → {change.NewPosition}"
            : change.Reason is null ? ChangeReason(change.Kind) : EnsureSentence(change.Reason);
        return new ChangeView(label, path, detail);
    }

    private static string TechnicalLine(PathChange change) => change.Kind switch
    {
        PathChangeKind.Added => $"+ {change.DisplayPath}",
        PathChangeKind.Removed => $"- {change.DisplayPath}",
        PathChangeKind.Changed => $"~ {change.DisplayPreviousPath} -> {change.DisplayPath}",
        PathChangeKind.Moved =>
            $"↕ {change.DisplayPath}  ({change.PreviousPosition} -> {change.NewPosition})",
        _ => change.DisplayPath,
    };

    private static string ChangeReason(PathChangeKind kind) =>
        kind switch
        {
            PathChangeKind.Added => "This location will be added to PATH.",
            PathChangeKind.Removed =>
                "This location will be removed from PATH. No files or folders will be deleted.",
            PathChangeKind.Changed => "The stored PATH value will change.",
            PathChangeKind.Moved => "This location's priority will change.",
            _ => string.Empty,
        };

    private static string EnsureSentence(string value)
    {
        if (string.IsNullOrEmpty(value))
            return value;
        var sentence = char.ToUpperInvariant(value[0]) + value[1..];
        return sentence.EndsWith('.') ? sentence : sentence + ".";
    }

    private static Visibility Vis(bool value) =>
        value ? Visibility.Visible : Visibility.Collapsed;

    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private record ChangeView(string KindLabel, string Path, string Reason);

    private record ScopeDiffView(
        string ScopeTitle,
        string ChangeCountLabel,
        IReadOnlyList<ChangeView> Changes,
        IReadOnlyList<string> TechnicalLines,
        string ElevationText,
        Visibility RequiresElevationVisibility,
        Visibility OrderChangedVisibility);
}
