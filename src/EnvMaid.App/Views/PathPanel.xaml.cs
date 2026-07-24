using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using EnvMaid.App.Models;

namespace EnvMaid.App.Views;

public partial class PathPanel : UserControl
{
    public PathPanel()
    {
        InitializeComponent();
    }

    // The DataGrid consumes the first click on an unselected cell for selection, so a
    // ToggleButton in a cell often needs two clicks. Handle the press directly and mark
    // it handled so the toggle is reliable on the first click.
    private void ConflictToggle_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is ToggleButton { DataContext: PathEntry entry })
        {
            entry.IsExpanded = !entry.IsExpanded;
            e.Handled = true;
        }
    }
}
