using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using EnvMaid.App.ViewModels;

namespace EnvMaid.App.Views;

public partial class ConflictsView : UserControl
{
    private const string StarterFile =
        "# Your custom CLI tools (one exe name per line, without extension).\r\n" +
        "# These are treated as known CLI tools, raising conflict confidence.\r\n" +
        "#   mytool        add a tool\r\n" +
        "#   !node         ignore a built-in you disagree with\r\n\r\n";

    public ConflictsView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private MainViewModel? Main => (Window.GetWindow(this)?.DataContext) as MainViewModel;

    private void OpenToolsFile_Click(object sender, RoutedEventArgs e)
    {
        if (Main is null) return;
        var path = Main.CliToolsFilePath;
        try
        {
            if (!File.Exists(path))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, StarterFile);
            }
            Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
        }
        catch (IOException)
        {
        }
    }

    private void ReloadTools_Click(object sender, RoutedEventArgs e) =>
        Main?.ReloadCliToolsCommand.Execute(null);

    private void ViewBuiltIn_Click(object sender, RoutedEventArgs e)
    {
        if (Main is null) return;
        var list = string.Join(Environment.NewLine, Main.BuiltInCliTools.OrderBy(x => x, StringComparer.OrdinalIgnoreCase));
        var dialog = new TextListDialog("Built-in CLI tools", list) { Owner = Window.GetWindow(this) };
        dialog.ShowDialog();
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is ConflictsViewModel vm)
            vm.ConfirmDelete = (folder, coverage) =>
            {
                var dialog = new DeleteCoverageDialog(folder, coverage)
                {
                    Owner = Window.GetWindow(this),
                };
                return dialog.ShowDialog() == true;
            };
    }
}
