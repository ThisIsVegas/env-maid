using System.Windows;
using EnvMaid.App.Models;
using EnvMaid.App.Services;
using EnvMaid.App.ViewModels;

namespace EnvMaid.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var envService = new EnvironmentPathService();
        var orphanService = new OrphanDetectionService();
        var backupService = new BackupService();
        var mainViewModel = new MainViewModel(envService, orphanService, backupService);

        var window = new MainWindow(mainViewModel);
        window.Show();
    }
}
