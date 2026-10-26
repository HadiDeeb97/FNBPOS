using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace ClothingStore.Desktop.Infrastructure;

/// <summary>
/// Every table column can be dragged to any width. Columns keep their minimum widths while they size themselves (so a
/// table fits its content and the window by default), but once the user drags a column's edge that column, and the one
/// on the other side of the edge, may go down to <see cref="Floor"/>.
/// </summary>
public static class ColumnSizing
{
    /// <summary>The narrowest a column can be dragged: room for a few letters and the drag handle.</summary>
    public const double Floor = 28;

    public static readonly DependencyProperty FreeResizeProperty = DependencyProperty.RegisterAttached(
        "FreeResize", typeof(bool), typeof(ColumnSizing), new PropertyMetadata(false, OnChanged));

    public static bool GetFreeResize(DependencyObject d) => (bool)d.GetValue(FreeResizeProperty);
    public static void SetFreeResize(DependencyObject d, bool value) => d.SetValue(FreeResizeProperty, value);

    /// <summary>A column the user sized: its own minimum no longer applies.</summary>
    public static void Release(DataGridColumn column)
    {
        if (column.CanUserResize && column.MinWidth > Floor) column.MinWidth = Floor;
    }

    private static void OnChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not DataGrid grid) return;
        grid.RemoveHandler(Thumb.DragStartedEvent, (DragStartedEventHandler)OnDragStarted);
        if ((bool)e.NewValue) grid.AddHandler(Thumb.DragStartedEvent, (DragStartedEventHandler)OnDragStarted);
    }

    private static void OnDragStarted(object sender, DragStartedEventArgs e)
    {
        if (sender is not DataGrid grid || e.OriginalSource is not Thumb thumb || FindHeader(thumb) is not { Column: { } column }) return;
        // The header's right handle sizes its own column, the left one the column before it; both columns meet at the edge.
        Release(column);
        var before = grid.Columns.Where(c => c.Visibility == Visibility.Visible && c.DisplayIndex < column.DisplayIndex)
            .MaxBy(c => c.DisplayIndex);
        if (before is not null) Release(before);
    }

    private static DataGridColumnHeader? FindHeader(DependencyObject start)
    {
        for (var d = start; d is not null; d = VisualTreeHelper.GetParent(d))
            if (d is DataGridColumnHeader header) return header;
        return null;
    }
}
