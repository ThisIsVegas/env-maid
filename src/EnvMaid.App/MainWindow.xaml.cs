using System.Windows;
using System.Diagnostics;
using System.Windows.Input;
using EnvMaid.App.Models;
using EnvMaid.App.ViewModels;
using EnvMaid.App.Views;

namespace EnvMaid.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _viewModel.ConfirmSave = diffs =>
        {
            var dialog = new SaveDiffDialog(diffs) { Owner = this };
            return dialog.ShowDialog() == true;
        };
        _viewModel.ResolveConflict = prompt =>
        {
            var dialog = new ConflictDialog(prompt) { Owner = this };
            dialog.ShowDialog();
            return dialog.Resolution;
        };
        _viewModel.UserPaths.ConfirmMaintenance = ShowMaintenancePreview;
        _viewModel.SystemPaths.ConfirmMaintenance = ShowMaintenancePreview;
        _viewModel.PickExportFile = () =>
        {
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = "Export PATH profile",
                Filter = "PATH profile (*.json)|*.json|All files (*.*)|*.*",
                FileName = $"envmaid-path-{DateTime.Now:yyyyMMdd}.json",
                DefaultExt = ".json",
            };
            return dialog.ShowDialog(this) == true ? dialog.FileName : null;
        };
        _viewModel.PickImportFile = () =>
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Import PATH profile",
                Filter = "PATH profile (*.json)|*.json|All files (*.*)|*.*",
                CheckFileExists = true,
            };
            return dialog.ShowDialog(this) == true ? dialog.FileName : null;
        };
        DataContext = _viewModel;
    }

    private bool ShowMaintenancePreview(MaintenancePreview preview)
    {
        var dialog = new MaintenancePreviewDialog(preview) { Owner = this };
        return dialog.ShowDialog() == true;
    }

    private void PathPanel_ConflictActivated(object? sender, EventArgs e)
    {
        OpenEditor(2);
    }

    private void ReviewItems_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.Dashboard.BrokenCount > 0)
            ReviewBroken_Click(sender, e);
        else if (_viewModel.Dashboard.DuplicateCount > 0)
            ReviewDuplicates_Click(sender, e);
        else
            ReviewConflicts_Click(sender, e);
    }

    private void ReviewBroken_Click(object sender, RoutedEventArgs e)
    {
        ReviewFirst(entry => entry.IsBroken);
    }

    private void ReviewDuplicates_Click(object sender, RoutedEventArgs e)
    {
        ReviewFirst(entry => entry.IsDuplicate);
    }

    private void ReviewFirst(Func<PathEntry, bool> predicate)
    {
        var entry = _viewModel.UserPaths.Entries.FirstOrDefault(predicate);
        if (entry is not null)
        {
            _viewModel.UserPaths.SelectedEntry = entry;
            OpenEditor(0);
            return;
        }

        entry = _viewModel.SystemPaths.Entries.FirstOrDefault(predicate);
        if (entry is not null)
        {
            _viewModel.SystemPaths.SelectedEntry = entry;
            OpenEditor(1);
        }
    }

    private void ReviewConflicts_Click(object sender, RoutedEventArgs e)
    {
        OpenEditor(2);
    }

    private void OpenEditor(int tabIndex)
    {
        MainTabs.SelectedIndex = 1;
        EditorTabs.SelectedIndex = tabIndex;
    }

    private void RestoreBackup_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new BackupRestoreDialog(_viewModel.GetBackupNames()) { Owner = this };
        if (dialog.ShowDialog() == true && dialog.SelectedBackupName is not null)
        {
            _viewModel.RestoreCommand.Execute(dialog.SelectedBackupName);
        }
    }


    private void Exit_Click(object sender, RoutedEventArgs e) => Close();

    private void ReportIssue_Click(object sender, RoutedEventArgs e) =>
        OpenUrl("https://github.com/ThisIsVegas/env-maid/issues/new");

    private void ViewSource_Click(object sender, RoutedEventArgs e) =>
        OpenUrl("https://github.com/ThisIsVegas/env-maid");

    private void About_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new AboutDialog { Owner = this };
        dialog.ShowDialog();
    }

    private static void OpenUrl(string url) =>
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
}
