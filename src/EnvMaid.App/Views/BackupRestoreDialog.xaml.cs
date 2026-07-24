using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace EnvMaid.App.Views;

public partial class BackupRestoreDialog : Window
{
    public string? SelectedBackupName { get; private set; }

    public BackupRestoreDialog(IEnumerable<string> backupNames)
    {
        InitializeComponent();
        foreach (var name in backupNames)
            BackupList.Items.Add(name);

        if (BackupList.Items.Count > 0)
            BackupList.SelectedIndex = 0;
    }

    private void Restore_Click(object sender, RoutedEventArgs e) => Confirm();

    private void BackupList_MouseDoubleClick(object sender, MouseButtonEventArgs e) => Confirm();

    private void Confirm()
    {
        if (BackupList.SelectedItem is string name)
        {
            SelectedBackupName = name;
            DialogResult = true;
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
