using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EnvMaid.App.Models;
using EnvMaid.App.Services;

namespace EnvMaid.App.ViewModels;

public partial class PathListViewModel : ObservableObject
{
    private readonly PathNormalizer _normalizer;
    private readonly PathCompressor _compressor;
    private bool _isLoading;
    private int _changeBatchDepth;
    private bool _hasPendingChangeNotification;

    public Func<MaintenancePreview, bool>? ConfirmMaintenance { get; set; }

    public event EventHandler? EntriesChanged;

    public PathScope Scope { get; }

    public ObservableCollection<PathEntry> Entries { get; } = new();

    [ObservableProperty]
    private PathEntry? _selectedEntry;

    [ObservableProperty]
    private bool _isBulkSelectionMode;

    [ObservableProperty]
    private int _totalLength;

    [ObservableProperty]
    private string _lengthLabel = "Length: 0 characters";

    [ObservableProperty]
    private bool _lengthOverLimit;

    private static readonly SolidColorBrush GreenBrush = new(Color.FromRgb(0xA6, 0xE3, 0xA1));
    private static readonly SolidColorBrush OrangeBrush = new(Color.FromRgb(0xFA, 0xB3, 0x87));
    private static readonly SolidColorBrush RedBrush = new(Color.FromRgb(0xF3, 0x8B, 0xA8));

    [ObservableProperty]
    private Brush _barColor = GreenBrush;

    public PathListViewModel(PathScope scope)
        : this(scope, new PathNormalizer(), new PathCompressor())
    {
    }

    public PathListViewModel(PathScope scope, PathNormalizer normalizer, PathCompressor compressor)
    {
        Scope = scope;
        _normalizer = normalizer;
        _compressor = compressor;
        Entries.CollectionChanged += (_, _) =>
        {
            RecalculateLength();
            NotifyEntriesChanged();
        };
    }

    public void LoadEntries(IEnumerable<string> paths)
    {
        _isLoading = true;
        try
        {
            Entries.Clear();
            foreach (var path in paths)
            {
                Entries.Add(CreateEntry(path));
            }
        }
        finally
        {
            _isLoading = false;
        }

        RecalculateLength();
        EntriesChanged?.Invoke(this, EventArgs.Empty);
    }

    private void Entry_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(PathEntry.Path))
            return;

        RecalculateLength();
        NotifyEntriesChanged();
    }

    private PathEntry CreateEntry(string path)
    {
        var entry = new PathEntry(path, Scope);
        entry.PropertyChanged += Entry_PropertyChanged;
        return entry;
    }

    private void NotifyEntriesChanged()
    {
        if (_isLoading)
            return;
        if (_changeBatchDepth > 0)
        {
            _hasPendingChangeNotification = true;
            return;
        }

        EntriesChanged?.Invoke(this, EventArgs.Empty);
    }

    private void RunChangeBatch(Action action)
    {
        _changeBatchDepth++;
        try
        {
            action();
        }
        finally
        {
            _changeBatchDepth--;
            if (_changeBatchDepth == 0 && _hasPendingChangeNotification)
            {
                _hasPendingChangeNotification = false;
                EntriesChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    /// <summary>
    /// Recomputes the scope readout and the two per-entry length markers.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The caution boundary is not a ceiling — nothing is truncated there, and editing past it in
    /// EnvMaid is fine. It marks where <em>other</em> PATH-writing tools start mishandling the
    /// value, which is what a user who also edits PATH from PowerShell needs to know.
    /// </para>
    /// <para>
    /// Colour is reserved for the band that actually blocks a save. A PATH in the caution band is
    /// common and permanent on many machines; a warning that is always on gets tuned out.
    /// </para>
    /// </remarks>
    public void RecalculateLength()
    {
        TotalLength = string.Join(';', Entries.Select(e => e.Path)).Length;
        var band = PathLengthLimits.BandFor(TotalLength);

        LengthLabel = $"Length: {TotalLength:N0} characters";
        LengthOverLimit = band == PathLengthBand.TooLong;
        BarColor = band switch
        {
            PathLengthBand.TooLong => RedBrush,
            PathLengthBand.Caution => OrangeBrush,
            _ => GreenBrush,
        };

        var cumulative = 0;
        PathEntry? lastBeforeCaution = null;
        PathEntry? lastBeforeHardLimit = null;

        foreach (var entry in Entries)
        {
            entry.IsLengthLimitBoundary = false;
            entry.IsWriteLimitBoundary = false;

            var before = cumulative;
            cumulative += entry.Path.Length;

            entry.IsPastLengthLimit = cumulative > PathLengthLimits.CautionThreshold;
            entry.IsPastWriteLimit = cumulative > PathLengthLimits.HardMaximum;

            if (before <= PathLengthLimits.CautionThreshold && !entry.IsPastLengthLimit)
                lastBeforeCaution = entry;
            if (before <= PathLengthLimits.HardMaximum && !entry.IsPastWriteLimit)
                lastBeforeHardLimit = entry;

            cumulative += 1; // separator
        }

        if (lastBeforeCaution is not null && Entries.Any(e => e.IsPastLengthLimit))
            lastBeforeCaution.IsLengthLimitBoundary = true;

        if (lastBeforeHardLimit is not null && Entries.Any(e => e.IsPastWriteLimit))
            lastBeforeHardLimit.IsWriteLimitBoundary = true;
    }

    [RelayCommand]
    private void RemoveChecked()
    {
        RunChangeBatch(() =>
        {
            foreach (var entry in Entries.Where(e => e.IsChecked).ToList())
                Entries.Remove(entry);
        });
        IsBulkSelectionMode = false;
    }

    [RelayCommand]
    private void ToggleBulkSelection()
    {
        IsBulkSelectionMode = !IsBulkSelectionMode;
        if (!IsBulkSelectionMode)
            foreach (var entry in Entries)
                entry.IsChecked = false;
    }

    /// <summary>Rewrite every entry to its canonical form (trailing slash / redundant
    /// segments), leaving %VAR% references intact. Staged only — committed on Save.</summary>
    [RelayCommand]
    private void Normalize()
    {
        var candidates = Entries
            .Select(entry => (Entry: entry, After: _normalizer.Normalize(entry.Path)))
            .Where(item => item.After != item.Entry.Path)
            .ToList();
        var changes = candidates
            .Select(item => new MaintenanceChange(
                MaintenanceChangeKind.Change,
                item.Entry.Path,
                item.After,
                "The location resolves to the same folder."))
            .ToList();
        var operations = candidates.Zip(changes).ToList();

        var preview = new MaintenancePreview(
            Scope,
            $"Normalize {changes.Count} PATH {(changes.Count == 1 ? "entry" : "entries")}?",
            changes.Count == 0
                ? "Every entry is already normalized."
                : "These changes alter how paths are written, not which folders they resolve to.",
            $"Stage {changes.Count} {(changes.Count == 1 ? "change" : "changes")}",
            changes);
        if (!Confirm(preview))
            return;

        RunChangeBatch(() =>
        {
            foreach (var operation in operations.Where(operation => operation.Second.IsSelected))
                operation.First.Entry.Path = operation.First.After;
        });
    }

    /// <summary>Remove entries that repeat an earlier one in THIS scope (first kept),
    /// comparing by canonical form so trailing-slash/case variants collapse together.</summary>
    [RelayCommand]
    private void RemoveDuplicates()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var firstPositions = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var duplicates = new List<(PathEntry Entry, MaintenanceChange Change)>();
        var changes = new List<MaintenanceChange>();

        for (var index = 0; index < Entries.Count; index++)
        {
            var entry = Entries[index];
            var normalized = _normalizer.Normalize(entry.Path);
            if (seen.Add(normalized))
            {
                firstPositions[normalized] = index + 1;
                continue;
            }

            var change = new MaintenanceChange(
                MaintenanceChangeKind.Remove,
                entry.Path,
                null,
                $"Duplicate of position {firstPositions[normalized]}; the first entry will be kept.");
            duplicates.Add((entry, change));
            changes.Add(change);
        }

        var preview = new MaintenancePreview(
            Scope,
            $"Remove {changes.Count} duplicate {(changes.Count == 1 ? "entry" : "entries")}?",
            changes.Count == 0
                ? "No duplicate entries were found."
                : "The first occurrence of each location will be kept.",
            $"Stage {changes.Count} {(changes.Count == 1 ? "removal" : "removals")}",
            changes);
        if (!Confirm(preview))
            return;

        RunChangeBatch(() =>
        {
            foreach (var item in duplicates.Where(item => item.Change.IsSelected))
                Entries.Remove(item.Entry);
        });
    }

    /// <summary>Remove entries whose folder is missing or empty (the High-confidence
    /// broken flags). Leaves NoExecutable (Low-confidence) entries alone.</summary>
    [RelayCommand]
    private void RemoveBroken()
    {
        var candidates = Entries.Where(e =>
                e.Flags.HasFlag(PathFlag.Missing) || e.Flags.HasFlag(PathFlag.Empty))
            .ToList();
        var changes = candidates
            .Select(entry => new MaintenanceChange(
                MaintenanceChangeKind.Remove,
                entry.Path,
                null,
                entry.Flags.HasFlag(PathFlag.Empty)
                    ? "The entry is empty."
                    : "The folder no longer exists."))
            .ToList();
        var operations = candidates.Zip(changes).ToList();

        var preview = new MaintenancePreview(
            Scope,
            $"Remove {changes.Count} missing {(changes.Count == 1 ? "location" : "locations")}?",
            changes.Count == 0
                ? "No missing or empty locations were found."
                : "Only PATH entries will be removed. No files or folders will be deleted.",
            $"Stage {changes.Count} {(changes.Count == 1 ? "removal" : "removals")}",
            changes);
        if (!Confirm(preview))
            return;

        RunChangeBatch(() =>
        {
            foreach (var operation in operations.Where(operation => operation.Second.IsSelected))
                Entries.Remove(operation.First);
        });
    }

    /// <summary>Fold known Windows variables back into literal entries (e.g. %LOCALAPPDATA%)
    /// to reclaim room under the 2047-character limit. Staged only — committed on Save.</summary>
    [RelayCommand]
    private void Compress()
    {
        var candidates = Entries
            .Select(entry => (Entry: entry, After: _compressor.Compress(entry.Path)))
            .Where(item => item.After != item.Entry.Path)
            .ToList();
        var changes = candidates
            .Select(item => new MaintenanceChange(
                MaintenanceChangeKind.Change,
                item.Entry.Path,
                item.After,
                "The stored value becomes shorter while resolving to the same folder."))
            .ToList();
        var operations = candidates.Zip(changes).ToList();

        var preview = new MaintenancePreview(
            Scope,
            $"Compress {changes.Count} PATH {(changes.Count == 1 ? "entry" : "entries")}?",
            changes.Count == 0
                ? "No entries can be shortened with known environment variables."
                : "Environment variables will shorten the stored values without changing their locations.",
            $"Stage {changes.Count} {(changes.Count == 1 ? "change" : "changes")}",
            changes);
        if (!Confirm(preview))
            return;

        RunChangeBatch(() =>
        {
            foreach (var operation in operations.Where(operation => operation.Second.IsSelected))
                operation.First.Entry.Path = operation.First.After;
        });
    }

    private bool Confirm(MaintenancePreview preview)
    {
        if (!preview.HasChanges)
        {
            ConfirmMaintenance?.Invoke(preview);
            return false;
        }

        return ConfirmMaintenance?.Invoke(preview) ?? true;
    }

    [RelayCommand]
    private void Add()
    {
        var input = PromptForPath("Add Path", string.Empty);
        if (!string.IsNullOrWhiteSpace(input))
            Entries.Add(CreateEntry(input));
    }

    [RelayCommand]
    private void Edit()
    {
        if (SelectedEntry is null) return;
        var input = PromptForPath("Edit Path", SelectedEntry.Path);
        if (!string.IsNullOrWhiteSpace(input))
            SelectedEntry.Path = input;
    }

    [RelayCommand]
    private void Remove()
    {
        if (SelectedEntry is null) return;
        Entries.Remove(SelectedEntry);
    }

    [RelayCommand]
    private void MoveUp() => MoveSelected(-1);

    [RelayCommand]
    private void MoveDown() => MoveSelected(1);

    public void MoveSelected(int direction)
    {
        if (SelectedEntry is null) return;
        var index = Entries.IndexOf(SelectedEntry);
        var newIndex = index + direction;
        if (newIndex < 0 || newIndex >= Entries.Count) return;
        Entries.Move(index, newIndex);
    }

    [RelayCommand]
    private void OpenInExplorer(PathEntry? entry)
    {
        if (entry is null) return;
        var expanded = Environment.ExpandEnvironmentVariables(entry.Path);
        if (!Directory.Exists(expanded)) return;
        Process.Start(new ProcessStartInfo { FileName = expanded, UseShellExecute = true });
    }

    [RelayCommand]
    private void CopyPath(PathEntry? entry)
    {
        if (entry is null) return;
        TryCopyToClipboard(entry.Path);
    }

    private static void TryCopyToClipboard(string text)
    {
        try
        {
            Clipboard.SetText(text);
        }
        catch (System.Runtime.InteropServices.COMException)
        {
        }
    }

    private static string? PromptForPath(string title, string initialValue)
    {
        var dialog = new Views.PathInputDialog(title, initialValue);
        return dialog.ShowDialog() == true ? dialog.ResultPath : null;
    }
}
