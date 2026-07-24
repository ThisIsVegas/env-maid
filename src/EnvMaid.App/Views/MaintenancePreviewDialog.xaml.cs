using System.ComponentModel;
using System.Windows;
using EnvMaid.App.Models;

namespace EnvMaid.App.Views;

public partial class MaintenancePreviewDialog : Window
{
    public MaintenancePreviewDialog(MaintenancePreview preview)
    {
        InitializeComponent();
        TitleText.Text = preview.Title;
        SummaryText.Text = preview.Summary;
        ScopeText.Text = $"{preview.Scope.ToString().ToUpperInvariant()} PATH";
        ChangeList.ItemsSource = preview.Changes;

        if (preview.HasChanges)
        {
            ConfirmButton.Content = preview.ConfirmLabel;
            foreach (var change in preview.Changes)
                change.PropertyChanged += Change_PropertyChanged;
            return;
        }

        CancelButton.Content = "Close";
        ConfirmButton.Visibility = Visibility.Collapsed;
    }

    private void Change_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(MaintenanceChange.IsSelected) ||
            ChangeList.ItemsSource is not IEnumerable<MaintenanceChange> changes)
            return;

        var selectedCount = changes.Count(change => change.IsSelected);
        ConfirmButton.Content =
            $"Stage {selectedCount} {(selectedCount == 1 ? "change" : "changes")}";
        ConfirmButton.IsEnabled = selectedCount > 0;
    }

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
}
