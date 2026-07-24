using System.Windows;
using System.Windows.Media;
using EnvMaid.App.Models;
using EnvMaid.App.Services;

namespace EnvMaid.App.Views;

public partial class SaveDiffDialog : Window
{
    private static readonly Brush AddBrush = new SolidColorBrush(Color.FromRgb(0xA6, 0xE3, 0xA1));
    private static readonly Brush RemoveBrush = new SolidColorBrush(Color.FromRgb(0xF3, 0x8B, 0xA8));

    public SaveDiffDialog(IReadOnlyList<ScopeDiff> diffs)
    {
        InitializeComponent();
        ScopeList.ItemsSource = diffs.Select(ToViewModel).ToList();
    }

    private static ScopeDiffView ToViewModel(ScopeDiff diff)
    {
        var lines = diff.Changes
            .OrderBy(c => c.Kind)
            .Select(c => c.Kind == PathChangeKind.Added
                ? new DiffLine($"+ {c.DisplayPath}", AddBrush)
                : new DiffLine(
                    c.Reason is null ? $"- {c.DisplayPath}" : $"- {c.DisplayPath}  ({c.Reason})",
                    RemoveBrush))
            .ToList();

        var requiresElevation = diff.Scope == PathScope.System && diff.HasChanges;

        return new ScopeDiffView(
            $"{diff.Scope} PATH",
            lines,
            Vis(requiresElevation),
            Vis(!diff.HasChanges),
            Vis(diff.OrderChanged));
    }

    private static Visibility Vis(bool on) => on ? Visibility.Visible : Visibility.Collapsed;

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

    private record DiffLine(string Text, Brush Brush);

    private record ScopeDiffView(
        string ScopeTitle,
        IReadOnlyList<DiffLine> Lines,
        Visibility RequiresElevationVisibility,
        Visibility NoChangesVisibility,
        Visibility OrderChangedVisibility);
}
