using System.Windows;
using System.Windows.Controls;
using EnvMaid.App.ViewModels;

namespace EnvMaid.App.Views;

public partial class ConflictsView : UserControl
{
    public ConflictsView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
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
