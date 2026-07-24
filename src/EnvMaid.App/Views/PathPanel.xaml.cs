using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using EnvMaid.App.Models;

namespace EnvMaid.App.Views;

public partial class PathPanel : UserControl
{
    /// <summary>Raised when a row with a shadow conflict is double-clicked, so the
    /// host can switch to the Conflicts tab.</summary>
    public event EventHandler? ConflictActivated;

    public PathPanel()
    {
        InitializeComponent();
    }

    private void ToolsButton_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        if (ToolsButton.ContextMenu is null)
            return;

        ToolsButton.ContextMenu.PlacementTarget = ToolsButton;
        ToolsButton.ContextMenu.Placement = PlacementMode.Bottom;
        ToolsButton.ContextMenu.IsOpen = true;
    }

    private void Row_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is DataGridRow { Item: PathEntry entry } && entry.HasShadowConflicts)
            ConflictActivated?.Invoke(this, EventArgs.Empty);
    }
}
