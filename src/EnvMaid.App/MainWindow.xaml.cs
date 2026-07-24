using System.Windows;
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
        DataContext = _viewModel;
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
