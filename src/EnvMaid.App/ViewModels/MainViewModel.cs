using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EnvMaid.App.Models;
using EnvMaid.App.Services;
using Microsoft.Win32;

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

    // The stored values behind those lists, kept unparsed. Splitting is lossy — a trailing ';'
    // and a doubled ';;' both vanish — so an external edit can survive a round-trip through the
    // parsed form undetected. Conflict detection compares these, not the lists (§14 step 1).
    private VariableValue _storedBaselineUser = VariableValue.Absent;
    private VariableValue _storedBaselineSystem = VariableValue.Absent;

    private bool _isRefreshing;

    /// <summary>Shows the save-diff confirm gate. Returns true to proceed with the
    /// write. Set by the view; if null, the save proceeds without confirmation.</summary>
    public Func<IReadOnlyList<ScopeDiff>, bool>? ConfirmSave { get; set; }

    /// <summary>
    /// Asks the user what to do about a scope that changed outside EnvMaid since the scan.
    /// Set by the view.
    /// </summary>
    /// <remarks>
    /// If null the scope is <b>cancelled</b>, not overwritten. The other delegates here proceed
    /// when unset because they confirm something the user asked for; this one guards a change
    /// the user has not seen yet.
    /// </remarks>
    public Func<ConflictPrompt, ConflictResolution>? ResolveConflict { get; set; }

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

    /// <summary>True when a scope's PATH could not be read because it is stored under an
    /// unsupported registry type. Save is refused while set, so the unreadable value is
    /// never overwritten by the (empty) working copy.</summary>
    [ObservableProperty]
    private bool _hasPathReadError;

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
        // A PATH stored under an unsupported registry type cannot be read safely. Surface it
        // and leave the scope empty rather than crashing — but note that Save is blocked too,
        // so nothing can overwrite the value we failed to read.
        string? unreadable = null;

        _isRefreshing = true;
        try
        {
            try
            {
                _storedBaselineUser = _envService.GetStoredValue(PathScope.User);
                _storedBaselineSystem = _envService.GetStoredValue(PathScope.System);
                _baselineUser = EnvironmentPathService.SplitEntries(_storedBaselineUser).ToList();
                _baselineSystem = EnvironmentPathService.SplitEntries(_storedBaselineSystem).ToList();
            }
            catch (UnsupportedPathValueTypeException ex)
            {
                unreadable = ex.Message;
                _storedBaselineUser = VariableValue.Absent;
                _storedBaselineSystem = VariableValue.Absent;
                _baselineUser = Array.Empty<string>();
                _baselineSystem = Array.Empty<string>();
            }

            UserPaths.LoadEntries(_baselineUser);
            SystemPaths.LoadEntries(_baselineSystem);

            RefreshAnalysis();
        }
        finally
        {
            _isRefreshing = false;
        }

        HasPathReadError = unreadable is not null;
        HasStagedChanges = false;
        LastScannedLabel = $"Last scanned {DateTime.Now:h:mm tt}";
        StatusMessage = unreadable ?? string.Empty;
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
        if (HasPathReadError)
        {
            StatusMessage = "Save is disabled: the stored PATH could not be read safely. " +
                            "Repair the registry value type, then rescan.";
            return;
        }

        IReadOnlyList<string> currentUser;
        IReadOnlyList<string> currentSystem;
        try
        {
            currentUser = _envService.GetEntries(PathScope.User);
            currentSystem = _envService.GetEntries(PathScope.System);
        }
        catch (UnsupportedPathValueTypeException ex)
        {
            // The value changed type since the last scan. Refuse rather than overwrite.
            HasPathReadError = true;
            StatusMessage = ex.Message;
            return;
        }

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

        // Back up the values actually being replaced — what is on disk right now, not the
        // baseline from the last scan. If something changed underneath us, that change is what
        // an undo has to be able to bring back.
        var backupFile = _backupService.CreateBackup(currentUser, currentSystem);

        var results = new List<ScopeSaveResult>
        {
            SaveScope(PathScope.User, _storedBaselineUser, currentUser, stagedUser),
            SaveScope(PathScope.System, _storedBaselineSystem, currentSystem, stagedSystem),
        };

        // One broadcast per save, after every scope is written (§14 step 7) — not one per scope.
        // The elevated helper deliberately does not broadcast for the System write it performs.
        var broadcast = results.Any(r => r.Status is ScopeSaveStatus.Written or ScopeSaveStatus.WrittenButUnverified)
            ? _envService.BroadcastEnvironmentChange()
            : null;

        var savedMessage = DescribeSave(results, backupFile, broadcast);
        Rescan();
        StatusMessage = savedMessage;
    }

    /// <summary>
    /// Re-reads one scope, resolves any external change, writes, and verifies the read-back.
    /// </summary>
    /// <remarks>
    /// Scopes are independent: a conflict in one never blocks the other, since the user may not
    /// even be editing it (§13 partial success).
    /// </remarks>
    private ScopeSaveResult SaveScope(
        PathScope scope,
        VariableValue baseline,
        IReadOnlyList<string> onDisk,
        IReadOnlyList<string> staged)
    {
        var current = _envService.GetStoredValue(scope);

        if (current != baseline)
        {
            var resolution = ResolveConflictFor(scope, baseline, onDisk, staged);
            if (resolution == ConflictResolution.Cancel)
                return new ScopeSaveResult(scope, ScopeSaveStatus.SkippedConflict,
                    $"{scope} PATH changed outside EnvMaid; left untouched.");

            if (resolution == ConflictResolution.Reload)
                // Rescan at the end of Save picks the new value up; nothing to write here.
                return new ScopeSaveResult(scope, ScopeSaveStatus.SkippedConflict,
                    $"{scope} PATH reloaded from disk; your edits to it were discarded.");
        }

        var currentEntries = EnvironmentPathService.SplitEntries(current);
        if (currentEntries.SequenceEqual(staged, StringComparer.Ordinal))
            return new ScopeSaveResult(scope, ScopeSaveStatus.Unchanged);

        var length = string.Join(';', staged.Where(e => !string.IsNullOrWhiteSpace(e))).Length;
        if (!PathLengthLimits.IsWritable(length))
            return new ScopeSaveResult(scope, ScopeSaveStatus.Failed,
                $"{scope} PATH would be {length:N0} characters, past the " +
                $"{PathLengthLimits.HardMaximum:N0} limit. It was not written.");

        try
        {
            if (scope == PathScope.System && !EnvironmentPathService.IsAdministrator())
                return ElevateSystemScope(current, currentEntries, staged);

            var verified = _envService.SetEntries(scope, staged);
            return verified
                ? new ScopeSaveResult(scope, ScopeSaveStatus.Written)
                : new ScopeSaveResult(scope, ScopeSaveStatus.WrittenButUnverified,
                    $"{scope} PATH was written but did not read back as written. " +
                    "It has been left as-is; rescan to see what is stored.");
        }
        catch (Exception ex)
        {
            return new ScopeSaveResult(scope, ScopeSaveStatus.Failed, $"{scope} PATH not saved: {ex.Message}");
        }
    }

    /// <summary>
    /// Hands the System-scope change to an elevated helper, retrying once if the helper found the
    /// value had moved and the user chose to proceed anyway.
    /// </summary>
    /// <remarks>
    /// The helper is windowless and cannot prompt, so a conflict comes back here to be resolved
    /// and then goes down again with a fresh baseline. The retry stops after one attempt rather
    /// than looping UAC prompts.
    /// </remarks>
    private ScopeSaveResult ElevateSystemScope(
        VariableValue baseline, IReadOnlyList<string> baselineEntries, IReadOnlyList<string> staged)
    {
        const PathScope scope = PathScope.System;

        var ops = PathOpService.Diff(baselineEntries, staged);
        var (code, result) = _envService.ElevateApply(baseline, ops);

        if (code == ElevatedExitCode.Conflict)
        {
            var onDisk = result?.OnDiskValue is null
                ? Array.Empty<string>()
                : result.OnDiskValue.Split(';');

            var resolution = ResolveConflict is null
                ? ConflictResolution.Cancel
                : ResolveConflict(new ConflictPrompt(
                    scope,
                    Summarize(_diffService.Diff(scope, baselineEntries, onDisk)),
                    Summarize(_diffService.Diff(scope, baselineEntries, staged))));

            if (resolution != ConflictResolution.Overwrite)
                return new ScopeSaveResult(scope, ScopeSaveStatus.SkippedConflict,
                    "System PATH changed outside EnvMaid; left untouched.");

            // Second and final attempt, against what the helper actually found.
            var freshBaseline = result?.OnDiskType is not null
                && Enum.TryParse<RegistryValueKind>(result.OnDiskType, out var kind)
                    ? VariableValue.Of(kind, result.OnDiskValue ?? string.Empty)
                    : baseline;

            (code, result) = _envService.ElevateApply(freshBaseline, PathOpService.Diff(onDisk, staged));

            if (code == ElevatedExitCode.Conflict)
                return new ScopeSaveResult(scope, ScopeSaveStatus.SkippedConflict,
                    "System PATH kept changing while saving; left untouched.");
        }

        return code switch
        {
            ElevatedExitCode.Applied when result?.Outcome.ReadBackVerified == false =>
                new ScopeSaveResult(scope, ScopeSaveStatus.WrittenButUnverified,
                    "System PATH was written but did not read back as written."),
            ElevatedExitCode.Applied => new ScopeSaveResult(scope, ScopeSaveStatus.Written),
            ElevatedExitCode.NotRun => new ScopeSaveResult(scope, ScopeSaveStatus.ElevationFailed,
                "System PATH not applied (elevation was declined)."),
            _ => new ScopeSaveResult(scope, ScopeSaveStatus.Failed,
                result?.Outcome.Notes ?? "System PATH not applied."),
        };
    }

    private ConflictResolution ResolveConflictFor(
        PathScope scope, VariableValue baseline, IReadOnlyList<string> onDisk, IReadOnlyList<string> staged)
    {
        // No delegate means Cancel, inverting the convention the other gates use. Those confirm
        // an action the user already asked for; this one guards a change they have not seen, and
        // an unconfigured view must not silently discard it.
        if (ResolveConflict is null)
            return ConflictResolution.Cancel;

        var baselineEntries = EnvironmentPathService.SplitEntries(baseline);
        var prompt = new ConflictPrompt(
            scope,
            Summarize(_diffService.Diff(scope, baselineEntries, onDisk)),
            Summarize(_diffService.Diff(scope, baselineEntries, staged)));

        return ResolveConflict(prompt);
    }

    private static IReadOnlyList<string> Summarize(ScopeDiff diff) =>
        diff.Changes.Select(c => c.Kind switch
        {
            PathChangeKind.Added => $"+ {c.DisplayPath}",
            PathChangeKind.Removed => $"- {c.DisplayPath}",
            PathChangeKind.Changed => $"~ {c.DisplayPreviousPath} -> {c.DisplayPath}",
            _ => $"= {c.DisplayPath} (moved)",
        }).ToList();

    private static string DescribeSave(
        IReadOnlyList<ScopeSaveResult> results, string backupFile, BroadcastResult? broadcast)
    {
        var notes = results.Where(r => r.Detail is not null).Select(r => r.Detail!).ToList();
        var wrote = results.Any(r => r.Status is ScopeSaveStatus.Written or ScopeSaveStatus.WrittenButUnverified);

        if (wrote)
            // Never "applied everywhere", and never "without a reboot": already-running programs
            // keep their old environment permanently, and only a restart changes that.
            notes.Add("Open apps keep the old PATH until restarted.");

        if (broadcast is { Succeeded: false })
            // A failed broadcast is a caveat on a successful save, not an error. Saying nothing
            // would turn a stale PATH in a fresh terminal into a debugging session.
            notes.Add("The shell did not confirm the change — new terminals may show the old PATH until you sign out.");

        var headline = wrote ? "Saved." : "Nothing was written.";
        var backup = $"Backup: {Path.GetFileName(backupFile)}";

        return notes.Count == 0
            ? $"{headline} {backup}"
            : $"{headline} {string.Join(" ", notes)} {backup}";
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

        IReadOnlyList<string> currentUser;
        IReadOnlyList<string> currentSystem;
        try
        {
            // Read both scopes *before* writing either, so an unreadable value aborts the whole
            // restore rather than leaving User written and System not.
            currentUser = _envService.GetEntries(PathScope.User);
            currentSystem = _envService.GetEntries(PathScope.System);
        }
        catch (UnsupportedPathValueTypeException ex)
        {
            HasPathReadError = true;
            StatusMessage = ex.Message;
            return;
        }

        // Restore writes to the environment immediately, exactly like Save, so it needs the same
        // conflict gate: restoring a stale backup over an installer's change is §14 verbatim.
        var results = new List<ScopeSaveResult>
        {
            SaveScope(PathScope.User, _storedBaselineUser, currentUser, userEntries),
            SaveScope(PathScope.System, _storedBaselineSystem, currentSystem, systemEntries),
        };

        if (results.Any(r => r.Status is ScopeSaveStatus.Written or ScopeSaveStatus.WrittenButUnverified))
            _envService.BroadcastEnvironmentChange();

        var notes = results.Where(r => r.Detail is not null).Select(r => r.Detail!).ToList();
        var message = notes.Count == 0
            ? $"Restored from {backupFileName}."
            : $"Restored from {backupFileName}. {string.Join(" ", notes)}";

        Rescan();
        StatusMessage = message;
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
