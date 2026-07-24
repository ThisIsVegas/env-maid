using System.Windows;
using EnvMaid.App.Models;
using EnvMaid.App.ViewModels;

namespace EnvMaid.App.Views;

public partial class DeleteCoverageDialog : Window
{
    public DeleteCoverageDialog(ConflictLocation folder, IReadOnlyList<CoverageItem> coverage)
    {
        InitializeComponent();
        FolderText.Text = folder.DisplayPath;
        CoverageList.ItemsSource = coverage;
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
