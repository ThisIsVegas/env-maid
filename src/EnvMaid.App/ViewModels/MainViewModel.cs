using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EnvMaid.App.Models;
using EnvMaid.App.Services;

namespace EnvMaid.App.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly EnvironmentPathService _envService;
    private readonly OrphanDetectionService _orphanService;
    private readonly BackupService _backupService;

    public PathListViewModel UserPaths { get; }
    public PathListViewModel SystemPaths { get; }

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    public MainViewModel(EnvironmentPathService envService, OrphanDetectionService orphanService, BackupService backupService)
    {
        _envService = envService;
        _orphanService = orphanService;
        _backupService = backupService;

        UserPaths = new PathListViewModel(PathScope.User);
        SystemPaths = new PathListViewModel(PathScope.System);

        UserPaths.Entries.CollectionChanged += (_, _) => RecalculateGlobalRank();
        SystemPaths.Entries.CollectionChanged += (_, _) => RecalculateGlobalRank();

        Rescan();
    }

    [RelayCommand]
    private void Rescan()
    {
        UserPaths.LoadEntries(_envService.GetEntries(PathScope.User));
        SystemPaths.LoadEntries(_envService.GetEntries(PathScope.System));

        _orphanService.ApplyFlags(UserPaths.Entries.ToList(), SystemPaths.Entries.ToList());
        UserPaths.RecalculateLength();
        SystemPaths.RecalculateLength();
        RecalculateGlobalRank();

        StatusMessage = "Scan complete.";
    }

    private void RecalculateGlobalRank()
    {
        // Real PATH resolution order: System entries first, then User entries appended.
        var rank = 1;
        foreach (var entry in SystemPaths.Entries)
            entry.GlobalRank = rank++;
        foreach (var entry in UserPaths.Entries)
            entry.GlobalRank = rank++;
    }

    [RelayCommand]
    private void RemoveAllChecked()
    {
        UserPaths.RemoveCheckedCommand.Execute(null);
        SystemPaths.RemoveCheckedCommand.Execute(null);
        StatusMessage = "Checked entries removed. Click Save to commit.";
    }

    [RelayCommand]
    private void Save()
    {
        var currentUser = _envService.GetEntries(PathScope.User);
        var currentSystem = _envService.GetEntries(PathScope.System);
        var backupFile = _backupService.CreateBackup(currentUser, currentSystem);

        _envService.SetEntries(PathScope.User, UserPaths.Entries.Select(e => e.Path));

        var systemEntries = SystemPaths.Entries.Select(e => e.Path).ToList();
        var systemResult = ApplySystemPathIfChanged(currentSystem, systemEntries);

        _envService.BroadcastEnvironmentChange();

        StatusMessage = systemResult switch
        {
            SystemPathApplyResult.NotChanged => $"Saved. Backup: {Path.GetFileName(backupFile)}",
            SystemPathApplyResult.Applied => $"Saved (including System PATH). Backup: {Path.GetFileName(backupFile)}",
            _ => $"User PATH saved. System PATH not applied (elevation cancelled or failed). Backup: {Path.GetFileName(backupFile)}",
        };
        Rescan();
    }

    [RelayCommand]
    private void Restore(string backupFileName)
    {
        var backups = _backupService.ListBackups();
        var match = backups.FirstOrDefault(f => f.Name == backupFileName);
        if (match is null)
        {
            StatusMessage = "Backup file not found.";
            return;
        }

        var backup = _backupService.LoadBackup(match.FullName);
        var userEntries = backup.UserPath.Length > 0 ? backup.UserPath.Split(';') : Array.Empty<string>();
        var systemEntries = backup.SystemPath.Length > 0 ? backup.SystemPath.Split(';') : Array.Empty<string>();

        _envService.SetEntries(PathScope.User, userEntries);

        var currentSystem = _envService.GetEntries(PathScope.System);
        var systemResult = ApplySystemPathIfChanged(currentSystem, systemEntries);

        _envService.BroadcastEnvironmentChange();

        StatusMessage = systemResult switch
        {
            SystemPathApplyResult.Failed => $"User PATH restored. System PATH not applied (elevation cancelled or failed).",
            _ => $"Restored from {backupFileName}.",
        };
        Rescan();
    }

    private enum SystemPathApplyResult { NotChanged, Applied, Failed }

    private SystemPathApplyResult ApplySystemPathIfChanged(IReadOnlyList<string> currentSystem, IReadOnlyList<string> newSystem)
    {
        if (currentSystem.SequenceEqual(newSystem))
            return SystemPathApplyResult.NotChanged;

        if (EnvironmentPathService.IsAdministrator())
        {
            _envService.SetEntries(PathScope.System, newSystem);
            return SystemPathApplyResult.Applied;
        }

        var joined = string.Join(';', newSystem);
        return _envService.TryElevateSetSystemPath(joined)
            ? SystemPathApplyResult.Applied
            : SystemPathApplyResult.Failed;
    }

    public IReadOnlyList<string> GetBackupNames() =>
        _backupService.ListBackups().Select(f => f.Name).ToList();
}
