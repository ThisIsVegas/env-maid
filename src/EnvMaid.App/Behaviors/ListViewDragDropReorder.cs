using System.Collections;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace EnvMaid.App.Behaviors;

public static class ListViewDragDropReorder
{
    public static readonly DependencyProperty IsEnabledProperty =
        DependencyProperty.RegisterAttached(
            "IsEnabled", typeof(bool), typeof(ListViewDragDropReorder),
            new PropertyMetadata(false, OnIsEnabledChanged));

    public static void SetIsEnabled(DependencyObject element, bool value) => element.SetValue(IsEnabledProperty, value);
    public static bool GetIsEnabled(DependencyObject element) => (bool)element.GetValue(IsEnabledProperty);

    private static Point _dragStart;
    private static object? _draggedItem;

    private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ListView listView || e.NewValue is not true) return;

        listView.AllowDrop = true;
        listView.PreviewMouseLeftButtonDown += (_, args) => _dragStart = args.GetPosition(null);

        listView.PreviewMouseMove += (_, args) =>
        {
            if (args.LeftButton != MouseButtonState.Pressed) return;

            var current = args.GetPosition(null);
            var diff = _dragStart - current;
            if (Math.Abs(diff.X) < 5 && Math.Abs(diff.Y) < 5) return;

            var item = listView.SelectedItem;
            if (item is null) return;

            _draggedItem = item;
            DragDrop.DoDragDrop(listView, item, DragDropEffects.Move);
        };

        listView.Drop += (_, args) =>
        {
            if (_draggedItem is null) return;
            if (listView.ItemsSource is not IList items) return;

            var targetItem = FindListViewItem(args.OriginalSource as DependencyObject);
            if (targetItem?.DataContext is null) return;

            var oldIndex = items.IndexOf(_draggedItem);
            var newIndex = items.IndexOf(targetItem.DataContext);
            if (oldIndex < 0 || newIndex < 0 || oldIndex == newIndex) return;

            if (items is System.Collections.ObjectModel.ObservableCollection<object> generic)
            {
                generic.Move(oldIndex, newIndex);
            }
            else
            {
                var moveMethod = items.GetType().GetMethod("Move");
                moveMethod?.Invoke(items, new object[] { oldIndex, newIndex });
            }

            _draggedItem = null;
        };
    }

    private static ListViewItem? FindListViewItem(DependencyObject? source)
    {
        while (source is not null && source is not ListViewItem)
            source = VisualTreeHelper.GetParent(source);

        return source as ListViewItem;
    }
}
