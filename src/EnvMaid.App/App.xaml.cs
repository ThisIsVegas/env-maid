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

        if (TryRunElevatedHelper(e.Args))
        {
            Shutdown();
            return;
        }

        var envService = new EnvironmentPathService();
        var cliToolListService = new CliToolListService();
        var ranker = new ConflictRanker(cliToolListService);
        var orphanService = new OrphanDetectionService(ranker);
        var conflictAnalysisService = new ConflictAnalysisService(ranker);
        var backupService = new BackupService();
        var diffService = new PathDiffService();
        var mainViewModel = new MainViewModel(envService, orphanService, conflictAnalysisService, backupService, diffService);

        var window = new MainWindow(mainViewModel);
        window.Show();
    }

    /// <summary>
    /// When relaunched elevated to write the System PATH, does just that and exits
    /// without showing any window. Returns true if this process was such a relaunch.
    /// </summary>
    private static bool TryRunElevatedHelper(string[] args)
    {
        if (args.Length < 2 || args[0] != EnvironmentPathService.ElevatedSetSystemPathArg)
            return false;

        try
        {
            var envService = new EnvironmentPathService();
            var entries = args[1].Length > 0 ? args[1].Split(';') : Array.Empty<string>();
            envService.SetEntries(PathScope.System, entries);
            envService.BroadcastEnvironmentChange();
            Environment.ExitCode = 0;
        }
        catch
        {
            Environment.ExitCode = 1;
        }

        return true;
    }
}
