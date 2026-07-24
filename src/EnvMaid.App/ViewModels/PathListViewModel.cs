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
    {
        Scope = scope;
        Entries.CollectionChanged += (_, _) => RecalculateLength();
    }

    public void LoadEntries(IEnumerable<string> paths)
    {
        Entries.Clear();
        foreach (var p in paths)
            Entries.Add(new PathEntry(p, Scope));

        foreach (var entry in Entries)
            entry.PropertyChanged += (_, _) => RecalculateLength();

        RecalculateLength();
    }

    public void RecalculateLength()
    {
        TotalLength = string.Join(';', Entries.Select(e => e.Path)).Length;
        LengthLabel = $"Length: {TotalLength} / {PathLimit}";
        BarColor = TotalLength >= PathLimit ? RedBrush : TotalLength >= WarningThreshold ? OrangeBrush : GreenBrush;
    }

    [RelayCommand]
    private void RemoveChecked()
    {
        foreach (var entry in Entries.Where(e => e.IsChecked).ToList())
            Entries.Remove(entry);
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

    [RelayCommand]
    private void OpenShadowFolder(ShadowConflict? conflict)
    {
        if (conflict is null || !Directory.Exists(conflict.ShadowedFolderPath)) return;
        Process.Start(new ProcessStartInfo { FileName = conflict.ShadowedFolderPath, UseShellExecute = true });
    }

    [RelayCommand]
    private void CopyExeName(ShadowConflict? conflict)
    {
        if (conflict is null) return;
        TryCopyToClipboard(conflict.ExeName);
    }

    [RelayCommand]
    private void CopyShadowedFolderPath(ShadowConflict? conflict)
    {
        if (conflict is null) return;
        TryCopyToClipboard(conflict.ShadowedFolderPath);
    }

    [RelayCommand]
    private void SearchMultipleVersions(ShadowConflict? conflict)
    {
        if (conflict is null) return;
        var url = SearchUrlBuilder.BuildMultipleVersionsQuery(conflict.ExeName);
        Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
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
