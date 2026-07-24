using System.Collections;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace EnvMaid.App.Behaviors;

public static class DataGridDragDropReorder
{
    public static readonly DependencyProperty IsEnabledProperty =
        DependencyProperty.RegisterAttached(
            "IsEnabled", typeof(bool), typeof(DataGridDragDropReorder),
            new PropertyMetadata(false, OnIsEnabledChanged));

    public static void SetIsEnabled(DependencyObject element, bool value) => element.SetValue(IsEnabledProperty, value);
    public static bool GetIsEnabled(DependencyObject element) => (bool)element.GetValue(IsEnabledProperty);

    private static Point _dragStart;
    private static object? _draggedItem;

    private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not DataGrid grid || e.NewValue is not true) return;

        grid.AllowDrop = true;
        var pressedOnInteractiveControl = false;

        grid.PreviewMouseLeftButtonDown += (_, args) =>
        {
            _dragStart = args.GetPosition(null);
            pressedOnInteractiveControl = IsOnInteractiveControl(args.OriginalSource as DependencyObject);
        };

        grid.PreviewMouseMove += (_, args) =>
        {
            if (args.LeftButton != MouseButtonState.Pressed) return;
            if (pressedOnInteractiveControl) return;

            var current = args.GetPosition(null);
            var diff = _dragStart - current;
            if (Math.Abs(diff.X) < 5 && Math.Abs(diff.Y) < 5) return;

            var item = grid.SelectedItem;
            if (item is null) return;

            _draggedItem = item;
            DragDrop.DoDragDrop(grid, item, DragDropEffects.Move);
        };

        grid.Drop += (_, args) =>
        {
            if (_draggedItem is null) return;
            if (grid.ItemsSource is not IList items) return;

            var targetRow = FindDataGridRow(args.OriginalSource as DependencyObject);
            if (targetRow?.Item is null) return;

            var oldIndex = items.IndexOf(_draggedItem);
            var newIndex = items.IndexOf(targetRow.Item);
            if (oldIndex < 0 || newIndex < 0 || oldIndex == newIndex) return;

            var moveMethod = items.GetType().GetMethod("Move");
            moveMethod?.Invoke(items, new object[] { oldIndex, newIndex });

            _draggedItem = null;
        };
    }

    private static DataGridRow? FindDataGridRow(DependencyObject? source)
    {
        while (source is not null && source is not DataGridRow)
            source = GetParent(source);

        return source as DataGridRow;
    }

    private static bool IsOnInteractiveControl(DependencyObject? source)
    {
        // Walk all the way to the root (not stopping at DataGridRow): the tree can hop
        // between visual and logical trees, and stopping early can miss an interactive
        // ancestor, letting a drag hijack a button/checkbox click.
        while (source is not null)
        {
            if (source is ButtonBase or CheckBox or TextBox)
                return true;
            source = GetParent(source);
        }
        return false;
    }

    // OriginalSource can be a ContentElement (e.g. a Run), which is not a Visual;
    // VisualTreeHelper.GetParent throws on those, so fall back to the logical tree.
    private static DependencyObject? GetParent(DependencyObject source) =>
        source is Visual or Visual3D
            ? VisualTreeHelper.GetParent(source)
            : LogicalTreeHelper.GetParent(source);
}
