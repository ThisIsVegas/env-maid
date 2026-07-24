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
    private const int PathLimit = 2047;
    private const int WarningThreshold = 1800;

    private readonly PathNormalizer _normalizer;
    private readonly PathCompressor _compressor;

    public PathScope Scope { get; }

    public ObservableCollection<PathEntry> Entries { get; } = new();

    [ObservableProperty]
    private PathEntry? _selectedEntry;

    [ObservableProperty]
    private int _totalLength;

    [ObservableProperty]
    private string _lengthLabel = "Length: 0 / 2047";

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
        Entries.CollectionChanged += (_, _) => RecalculateLength();
    }

    public void LoadEntries(IEnumerable<string> paths)
    {
        Entries.Clear();
        foreach (var p in paths)
            Entries.Add(new PathEntry(p, Scope));

        foreach (var entry in Entries)
            entry.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(PathEntry.Path))
                    RecalculateLength();
            };

        RecalculateLength();
    }

    public void RecalculateLength()
    {
        TotalLength = string.Join(';', Entries.Select(e => e.Path)).Length;
        LengthLabel = $"Length: {TotalLength} / {PathLimit}";
        BarColor = TotalLength >= PathLimit ? RedBrush : TotalLength >= WarningThreshold ? OrangeBrush : GreenBrush;

        var cumulative = 0;
        PathEntry? lastBeforeCutoff = null;
        foreach (var entry in Entries)
        {
            entry.IsLengthLimitBoundary = false;

            var wasPastLimit = cumulative > PathLimit;
            cumulative += entry.Path.Length;
            entry.IsPastLengthLimit = cumulative > PathLimit;
            cumulative += 1; // separator

            if (!wasPastLimit && !entry.IsPastLengthLimit)
                lastBeforeCutoff = entry;
        }

        if (lastBeforeCutoff is not null && Entries.Any(e => e.IsPastLengthLimit))
            lastBeforeCutoff.IsLengthLimitBoundary = true;
    }

    [RelayCommand]
    private void RemoveChecked()
    {
        foreach (var entry in Entries.Where(e => e.IsChecked).ToList())
            Entries.Remove(entry);
    }

    /// <summary>Rewrite every entry to its canonical form (trailing slash / redundant
    /// segments), leaving %VAR% references intact. Staged only — committed on Save.</summary>
    [RelayCommand]
    private void Normalize()
    {
        foreach (var entry in Entries)
        {
            var normalized = _normalizer.Normalize(entry.Path);
            if (normalized != entry.Path)
                entry.Path = normalized;
        }
    }

    /// <summary>Remove entries that repeat an earlier one in THIS scope (first kept),
    /// comparing by canonical form so trailing-slash/case variants collapse together.</summary>
    [RelayCommand]
    private void RemoveDuplicates()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in Entries.ToList())
            if (!seen.Add(_normalizer.Normalize(entry.Path)))
                Entries.Remove(entry);
    }

    /// <summary>Remove entries whose folder is missing or empty (the High-confidence
    /// broken flags). Leaves NoExecutable (Low-confidence) entries alone.</summary>
    [RelayCommand]
    private void RemoveBroken()
    {
        foreach (var entry in Entries.Where(e =>
                     e.Flags.HasFlag(PathFlag.Missing) || e.Flags.HasFlag(PathFlag.Empty)).ToList())
            Entries.Remove(entry);
    }

    /// <summary>Fold known Windows variables back into literal entries (e.g. %LOCALAPPDATA%)
    /// to reclaim room under the 2047-character limit. Staged only — committed on Save.</summary>
    [RelayCommand]
    private void Compress()
    {
        foreach (var entry in Entries)
        {
            var compressed = _compressor.Compress(entry.Path);
            if (compressed != entry.Path)
                entry.Path = compressed;
        }
    }

    [RelayCommand]
    private void Add()
    {
        var input = PromptForPath("Add Path", string.Empty);
        if (!string.IsNullOrWhiteSpace(input))
            Entries.Add(new PathEntry(input, Scope));
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
