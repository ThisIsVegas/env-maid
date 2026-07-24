using System.Windows;
using System.Windows.Input;
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
        DataContext = _viewModel;
    }

    private void ConflictsTile_Click(object sender, MouseButtonEventArgs e)
    {
        MainTabs.SelectedIndex = 2; // Conflicts tab
    }

    private void PathPanel_ConflictActivated(object? sender, EventArgs e)
    {
        MainTabs.SelectedIndex = 2; // Conflicts tab
    }

    private void RestoreBackup_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new BackupRestoreDialog(_viewModel.GetBackupNames()) { Owner = this };
        if (dialog.ShowDialog() == true && dialog.SelectedBackupName is not null)
        {
            _viewModel.RestoreCommand.Execute(dialog.SelectedBackupName);
        }
    }
}
