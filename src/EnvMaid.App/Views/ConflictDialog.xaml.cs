using System.Windows;
using EnvMaid.App.Models;

namespace EnvMaid.App.Views;

/// <summary>
/// Asks which version of a scope to keep when it changed outside EnvMaid mid-edit.
/// </summary>
/// <remarks>
/// Two short change lists rather than three parallel PATH columns: on a real machine the columns
/// would be a hundred near-identical rows hiding a handful of actual differences. There is no
/// merge option — merging two ordered lists means guessing about position and duplicates, and a
/// wrong guess silently produces a PATH neither side wanted.
/// </remarks>
public partial class ConflictDialog : Window
{
    public ConflictResolution Resolution { get; private set; } = ConflictResolution.Cancel;

    public ConflictDialog(ConflictPrompt prompt)
    {
        InitializeComponent();

        TitleText.Text = $"{prompt.Scope} PATH changed outside EnvMaid";

        ExternalList.ItemsSource = prompt.ExternalChangeSummary;
        PendingList.ItemsSource = prompt.PendingChangeSummary;

        // An empty external list is not "nothing happened" — the conflict was detected on the raw
        // stored value, so a trailing ';' or a changed registry type lands here with no entry diff.
        NoExternalText.Visibility = Vis(prompt.ExternalChangeSummary.Count == 0);
        NoPendingText.Visibility = Vis(prompt.PendingChangeSummary.Count == 0);
    }

    private static Visibility Vis(bool value) => value ? Visibility.Visible : Visibility.Collapsed;

    private void Overwrite_Click(object sender, RoutedEventArgs e) => Close(ConflictResolution.Overwrite);

    private void Reload_Click(object sender, RoutedEventArgs e) => Close(ConflictResolution.Reload);

    private void Cancel_Click(object sender, RoutedEventArgs e) => Close(ConflictResolution.Cancel);

    private void Close(ConflictResolution resolution)
    {
        Resolution = resolution;
        // Closing via the title bar leaves the default Cancel in place, which is the safe answer.
        DialogResult = resolution != ConflictResolution.Cancel;
        Close();
    }
}
