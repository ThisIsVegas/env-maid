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

        StatusMessage = "Scan complete.";
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
        _envService.SetEntries(PathScope.System, SystemPaths.Entries.Select(e => e.Path));
        _envService.BroadcastEnvironmentChange();

        StatusMessage = $"Saved. Backup: {Path.GetFileName(backupFile)}";
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
        _envService.SetEntries(PathScope.System, systemEntries);
        _envService.BroadcastEnvironmentChange();

        StatusMessage = $"Restored from {backupFileName}.";
        Rescan();
    }

    public IReadOnlyList<string> GetBackupNames() =>
        _backupService.ListBackups().Select(f => f.Name).ToList();
}
