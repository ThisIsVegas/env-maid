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
    private readonly PathDiffService _diffService;
    private readonly CliToolListService _cliTools;
    private IReadOnlyList<string> _baselineUser = Array.Empty<string>();
    private IReadOnlyList<string> _baselineSystem = Array.Empty<string>();
    private bool _isRefreshing;

    /// <summary>Shows the save-diff confirm gate. Returns true to proceed with the
    /// write. Set by the view; if null, the save proceeds without confirmation.</summary>
    public Func<IReadOnlyList<ScopeDiff>, bool>? ConfirmSave { get; set; }

    /// <summary>Prompts for a file path to export the current PATH to (Save dialog).
    /// Returns null if cancelled. Set by the view.</summary>
    public Func<string?>? PickExportFile { get; set; }

    /// <summary>Prompts for a PATH profile file to import (Open dialog). Returns null if
    /// cancelled. Set by the view.</summary>
    public Func<string?>? PickImportFile { get; set; }

    public PathListViewModel UserPaths { get; }
    public PathListViewModel SystemPaths { get; }
    public ConflictsViewModel Conflicts { get; }
    public DashboardViewModel Dashboard { get; }

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private string _lastScannedLabel = string.Empty;

    [ObservableProperty]
    private bool _hasStagedChanges;

    public MainViewModel(
        EnvironmentPathService envService,
        OrphanDetectionService orphanService,
        ConflictAnalysisService conflictAnalysisService,
        BackupService backupService,
        PathDiffService diffService,
        CliToolListService cliTools)
    {
        _envService = envService;
        _orphanService = orphanService;
        _backupService = backupService;
        _diffService = diffService;
        _cliTools = cliTools;

        UserPaths = new PathListViewModel(PathScope.User);
        SystemPaths = new PathListViewModel(PathScope.System);
        Conflicts = new ConflictsViewModel(conflictAnalysisService, UserPaths, SystemPaths);
        Dashboard = new DashboardViewModel(UserPaths, SystemPaths, Conflicts);

        UserPaths.EntriesChanged += (_, _) => OnEntriesChanged();
        SystemPaths.EntriesChanged += (_, _) => OnEntriesChanged();

        Rescan();
    }

    private void OnEntriesChanged()
    {
        if (_isRefreshing)
            return;

        RefreshAnalysis();
        RefreshStagedState();
    }

    [RelayCommand]
    private void Rescan()
    {
        _isRefreshing = true;
        try
        {
            _baselineUser = _envService.GetEntries(PathScope.User).ToList();
            _baselineSystem = _envService.GetEntries(PathScope.System).ToList();
            UserPaths.LoadEntries(_baselineUser);
            SystemPaths.LoadEntries(_baselineSystem);

            RefreshAnalysis();
        }
        finally
        {
            _isRefreshing = false;
        }

        HasStagedChanges = false;
        LastScannedLabel = $"Last scanned {DateTime.Now:h:mm tt}";
        StatusMessage = string.Empty;
    }

    private void RefreshAnalysis()
    {
        _orphanService.ApplyFlags(UserPaths.Entries.ToList(), SystemPaths.Entries.ToList());
        UserPaths.RecalculateLength();
        SystemPaths.RecalculateLength();
        RecalculateGlobalRank();
        Conflicts.Refresh();
        Dashboard.Refresh();
    }

    private void RefreshStagedState()
    {
        // This is stored-state equality, not folder identity: normalization and
        // compression intentionally change the saved representation even when the
        // resulting folder is equivalent.
        HasStagedChanges =
            !_baselineUser.SequenceEqual(UserPaths.Entries.Select(entry => entry.Path), StringComparer.OrdinalIgnoreCase) ||
            !_baselineSystem.SequenceEqual(SystemPaths.Entries.Select(entry => entry.Path), StringComparer.OrdinalIgnoreCase);
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

    public string CliToolsFilePath => _cliTools.UserFilePath;

    public IReadOnlyCollection<string> BuiltInCliTools => _cliTools.BuiltInNames;

    /// <summary>Re-read the user CLI-tools file and re-rank conflicts against it.</summary>
    [RelayCommand]
    private void ReloadCliTools()
    {
        _cliTools.Reload();
        Rescan();
        StatusMessage = "CLI tool list reloaded.";
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

        var stagedUser = UserPaths.Entries.Select(e => e.Path).ToList();
        var stagedSystem = SystemPaths.Entries.Select(e => e.Path).ToList();
        var diffs = new[]
        {
            _diffService.Diff(PathScope.User, currentUser, stagedUser),
            _diffService.Diff(PathScope.System, currentSystem, stagedSystem),
        };

        if (diffs.All(d => !d.HasChanges))
        {
            StatusMessage = "No changes to save.";
            return;
        }

        if (ConfirmSave is not null && !ConfirmSave(diffs))
        {
            StatusMessage = "Save cancelled.";
            return;
        }

        var backupFile = _backupService.CreateBackup(currentUser, currentSystem);

        _envService.SetEntries(PathScope.User, UserPaths.Entries.Select(e => e.Path));

        var systemEntries = SystemPaths.Entries.Select(e => e.Path).ToList();
        var systemResult = ApplySystemPathIfChanged(currentSystem, systemEntries);

        _envService.BroadcastEnvironmentChange();

        var savedMessage = systemResult switch
        {
            SystemPathApplyResult.NotChanged => $"Saved. Backup: {Path.GetFileName(backupFile)}",
            SystemPathApplyResult.Applied => $"Saved (including System PATH). Backup: {Path.GetFileName(backupFile)}",
            _ => $"User PATH saved. System PATH not applied (elevation cancelled or failed). Backup: {Path.GetFileName(backupFile)}",
        };
        Rescan();
        StatusMessage = savedMessage;
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

    /// <summary>Write the current staged PATH (both scopes) to a user-chosen file.</summary>
    [RelayCommand]
    private void Export()
    {
        var target = PickExportFile?.Invoke();
        if (string.IsNullOrEmpty(target))
            return;

        _backupService.ExportTo(
            target,
            UserPaths.Entries.Select(e => e.Path),
            SystemPaths.Entries.Select(e => e.Path));
        StatusMessage = $"Exported to {Path.GetFileName(target)}.";
    }

    /// <summary>Load a PATH profile from a file, replacing the staged entries. Nothing is
    /// written to the environment yet — the user reviews the change through the Save gate.</summary>
    [RelayCommand]
    private void Import()
    {
        var source = PickImportFile?.Invoke();
        if (string.IsNullOrEmpty(source))
            return;

        PathBackup profile;
        try
        {
            profile = _backupService.ImportFrom(source);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or System.Text.Json.JsonException)
        {
            StatusMessage = "Import failed: file could not be read as a PATH profile.";
            return;
        }

        _isRefreshing = true;
        try
        {
            UserPaths.LoadEntries(profile.UserPath.Length > 0 ? profile.UserPath.Split(';') : Array.Empty<string>());
            SystemPaths.LoadEntries(profile.SystemPath.Length > 0 ? profile.SystemPath.Split(';') : Array.Empty<string>());

            RefreshAnalysis();
        }
        finally
        {
            _isRefreshing = false;
        }

        RefreshStagedState();
        StatusMessage = $"Imported {Path.GetFileName(source)}. Review and click Save to apply.";
    }

    public IReadOnlyList<string> GetBackupNames() =>
        _backupService.ListBackups().Select(f => f.Name).ToList();
}
