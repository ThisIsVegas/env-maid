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
        var mainViewModel = new MainViewModel(envService, orphanService, conflictAnalysisService, backupService, diffService, cliToolListService);

        var window = new MainWindow(mainViewModel);
        window.Show();
    }

    /// <summary>
    /// When relaunched elevated, applies the intent file it was handed and exits without showing
    /// any window. Returns true if this process was such a relaunch.
    /// </summary>
    /// <remarks>
    /// The whole read → verify → write → read-back cycle happens here, inside the privilege
    /// boundary, so nothing can change the value between the read and the write. It deliberately
    /// does not broadcast — the parent does that once for the whole save.
    /// </remarks>
    private static bool TryRunElevatedHelper(string[] args)
    {
        if (args.Length < 2 || args[0] != EnvironmentPathService.ElevatedApplyArg)
            return false;

        var intentPath = args[1];

        try
        {
            var intent = ElevatedIntentFile.Read(intentPath);
            var (code, result) = new ElevatedApplyService().Apply(intent);

            // The outcome goes back through the file, not the exit code: three booleans plus a
            // note do not fit in an exit status without a code per combination.
            ElevatedIntentFile.WriteResult(intentPath, intent, result);
            Environment.ExitCode = (int)code;
        }
        catch (Exception ex)
        {
            // The parent cannot see a stack trace, so record what went wrong where it can.
            TryRecordFailure(intentPath, ex);
            Environment.ExitCode = (int)ElevatedExitCode.Failed;
        }

        return true;
    }

    private static void TryRecordFailure(string intentPath, Exception ex)
    {
        try
        {
            ElevatedIntentFile.WriteResult(intentPath, ElevatedIntentFile.Read(intentPath), new ElevatedResult
            {
                Outcome = new ElevatedOutcome(false, false, false, ex.Message),
            });
        }
        catch
        {
            // The file itself is unreadable or unwritable — the exit code is all the parent gets.
        }
    }
}
