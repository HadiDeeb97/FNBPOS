using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using ClothingStore.Core.Localization;

namespace ClothingStore.Desktop.Infrastructure;

/// <summary>
/// Table columns that fit the screen until the user sizes them, and then go exactly where they are dragged.
/// <list type="bullet">
/// <item>With no sizes of the user's (first use, "Reset panel sizes", or "Fit columns to the window" on a header's
/// right-click menu), columns that come out wider than the table share its width instead, in proportion to their
/// natural widths, down to their own minimum; a table that still doesn't fit scrolls left and right.</item>
/// <item>Pressing a column's edge fixes every column at its current width before the drag starts, so widening one never
/// takes width from the others (the table grows and scrolls instead), and only the dragged column may go below its
/// own minimum, down to <see cref="Floor"/>.</item>
/// </list>
/// </summary>
public static class ColumnSizing
{
    /// <summary>The narrowest a column can be dragged: room for a few letters and the drag handle.</summary>
    public const double Floor = 28;

    public static readonly DependencyProperty FreeResizeProperty = DependencyProperty.RegisterAttached(
        "FreeResize", typeof(bool), typeof(ColumnSizing), new PropertyMetadata(false, OnChanged));

    public static bool GetFreeResize(DependencyObject d) => (bool)d.GetValue(FreeResizeProperty);
    public static void SetFreeResize(DependencyObject d, bool value) => d.SetValue(FreeResizeProperty, value);

    /// <summary>The columns are the user's own sizes (dragged now, or remembered): they are no longer fitted to the screen.</summary>
    private static readonly DependencyProperty UserSizedProperty = DependencyProperty.RegisterAttached(
        "UserSized", typeof(bool), typeof(ColumnSizing));

    public static void MarkUserSized(DataGrid grid) => grid.SetValue(UserSizedProperty, true);

    /// <summary>A column's width and minimum as the screen defines them, kept to go back to on "Fit columns to the window".</summary>
    private static readonly DependencyProperty OriginalProperty = DependencyProperty.RegisterAttached(
        "Original", typeof(object), typeof(ColumnSizing));

    /// <summary>Keeps every column's own width and minimum (once, before anything resizes them).</summary>
    public static void RememberOriginal(DataGrid grid)
    {
        foreach (var column in grid.Columns)
            if (column.ReadLocalValue(OriginalProperty) == DependencyProperty.UnsetValue)
                column.SetValue(OriginalProperty, (column.Width, column.MinWidth));
    }

    /// <summary>A remembered width narrower than the column's own minimum (the user dragged it so): the minimum gives way.</summary>
    public static void AllowWidth(DataGridColumn column, double width)
    {
        if (column.CanUserResize && column.MinWidth > width) column.MinWidth = Math.Max(width, Floor);
    }

    private static void OnChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not DataGrid grid) return;
        grid.RemoveHandler(UIElement.PreviewMouseLeftButtonDownEvent, (MouseButtonEventHandler)OnPreviewMouseDown);
        grid.RemoveHandler(Thumb.DragStartedEvent, (DragStartedEventHandler)OnDragStarted);
        grid.RemoveHandler(UIElement.PreviewMouseRightButtonUpEvent, (MouseButtonEventHandler)OnHeaderRightClick);
        grid.Loaded -= OnLoaded;
        grid.Unloaded -= OnUnloaded;
        if (!(bool)e.NewValue) return;
        // Pressing a column's edge: the widths are fixed before the header starts its drag (it marks the press and the
        // drag as handled, so these listen to handled events too).
        grid.AddHandler(UIElement.PreviewMouseLeftButtonDownEvent, (MouseButtonEventHandler)OnPreviewMouseDown, handledEventsToo: true);
        grid.AddHandler(Thumb.DragStartedEvent, (DragStartedEventHandler)OnDragStarted, handledEventsToo: true);
        grid.AddHandler(UIElement.PreviewMouseRightButtonUpEvent, (MouseButtonEventHandler)OnHeaderRightClick, handledEventsToo: true);
        grid.Loaded += OnLoaded;
        grid.Unloaded += OnUnloaded;
    }

    // ---- Fitting the screen ------------------------------------------------------------------------

    private static readonly DependencyProperty WatcherProperty = DependencyProperty.RegisterAttached(
        "Watcher", typeof(ScrollChangedEventHandler), typeof(ColumnSizing));

    private static void OnLoaded(object sender, RoutedEventArgs e)
    {
        var grid = (DataGrid)sender;
        RememberOriginal(grid);
        Watch(grid);
    }

    /// <summary>
    /// Checks whenever the table's content or visible width changes (columns fitting their content grow with the rows),
    /// until it has been fitted. Only the table's own scroll area counts, not boxes inside its cells; nothing runs on the
    /// window's other layout passes.
    /// </summary>
    private static void Watch(DataGrid grid)
    {
        StopWatching(grid);
        ScrollChangedEventHandler watcher = (_, e) =>
        {
            if (e.OriginalSource is ScrollViewer viewer && viewer.TemplatedParent == grid && (e.ExtentWidthChange != 0 || e.ViewportWidthChange != 0))
                FitIfWider(grid, viewer);
        };
        grid.SetValue(WatcherProperty, watcher);
        grid.AddHandler(ScrollViewer.ScrollChangedEvent, watcher);
        if (FindChild<ScrollViewer>(grid) is { } current) FitIfWider(grid, current); // already laid out (a page visited again)
    }

    private static void OnUnloaded(object sender, RoutedEventArgs e) => StopWatching((DataGrid)sender);

    private static void StopWatching(DataGrid grid)
    {
        if (grid.GetValue(WatcherProperty) is ScrollChangedEventHandler watcher) grid.RemoveHandler(ScrollViewer.ScrollChangedEvent, watcher);
        grid.ClearValue(WatcherProperty);
    }

    private static void FitIfWider(DataGrid grid, ScrollViewer scroller)
    {
        if ((bool)grid.GetValue(UserSizedProperty))
        {
            StopWatching(grid);
            return;
        }
        if (scroller.ViewportWidth <= 0 || scroller.ExtentWidth <= scroller.ViewportWidth + 1) return;

        // Wider than the table: the columns share its width, each in proportion to its natural width, down to its own
        // minimum (still readable). Below that the table scrolls left and right.
        var sizable = grid.Columns.Where(c => c.Visibility == Visibility.Visible && c.CanUserResize && c.ActualWidth > 0).ToList();
        if (sizable.Count == 0) return;
        foreach (var column in sizable)
            column.Width = new DataGridLength(column.ActualWidth, DataGridLengthUnitType.Star);
        StopWatching(grid); // shares of the width follow the window from now on, however the rows grow
    }

    /// <summary>
    /// Back to the screen's own column widths, fitted to the table again: for a table whose columns were dragged
    /// out of shape. Its remembered widths are forgotten.
    /// </summary>
    public static void FitToWindow(DataGrid grid)
    {
        grid.ClearValue(UserSizedProperty);
        LayoutMemory.Forget(grid);
        foreach (var column in grid.Columns)
        {
            if (column.GetValue(OriginalProperty) is not ValueTuple<DataGridLength, double> original) continue;
            column.MinWidth = original.Item2;
            column.Width = original.Item1;
        }
        Watch(grid);
    }

    private static void OnHeaderRightClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not DataGrid grid || e.OriginalSource is not DependencyObject source || FindAncestor<DataGridColumnHeader>(source) is not { } header) return;
        var fit = new MenuItem { Header = Loc.T("Common.FitColumns") };
        fit.Click += (_, _) => FitToWindow(grid);
        var menu = new ContextMenu { PlacementTarget = header, Placement = PlacementMode.MousePoint };
        menu.Items.Add(fit);
        menu.IsOpen = true;
        e.Handled = true;
    }

    // ---- Dragging --------------------------------------------------------------------------------

    private static void OnPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is DataGrid grid && e.OriginalSource is DependencyObject source && FindAncestor<Thumb>(source) is { } thumb)
            Freeze(grid, thumb);
    }

    // In case a drag starts without a press seen above (a touch screen): fixing widths twice changes nothing.
    private static void OnDragStarted(object sender, DragStartedEventArgs e)
    {
        if (sender is DataGrid grid && e.OriginalSource is Thumb thumb) Freeze(grid, thumb);
    }

    /// <summary>
    /// Every column keeps the width it has now (as pixels), so the dragged one takes no width from the others; only the
    /// column being resized may then go below its own minimum.
    /// </summary>
    private static void Freeze(DataGrid grid, Thumb thumb)
    {
        if (FindAncestor<DataGridColumnHeader>(thumb) is not { Column: { } column }) return;
        MarkUserSized(grid);
        StopWatching(grid);

        var visible = grid.Columns.Where(c => c.Visibility == Visibility.Visible).OrderBy(c => c.DisplayIndex).ToList();
        // The left edge of a header resizes the column before it.
        var resizing = thumb.Name == "PART_LeftHeaderGripper" ? visible.LastOrDefault(c => c.DisplayIndex < column.DisplayIndex) : column;

        // Widths first, all at once: changing one column would otherwise make the table share out the others again.
        var widths = visible.Where(c => c.CanUserResize && c.ActualWidth > 0 && !c.Width.IsAbsolute).Select(c => (Column: c, Width: c.ActualWidth)).ToList();
        foreach (var (c, width) in widths)
            c.Width = new DataGridLength(width, DataGridLengthUnitType.Pixel, width, width);
        if (resizing is { CanUserResize: true } && resizing.MinWidth > Floor) resizing.MinWidth = Floor;
    }

    private static T? FindAncestor<T>(DependencyObject start) where T : DependencyObject
    {
        for (var d = start; d is not null; d = d is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d))
            if (d is T found) return found;
        return null;
    }

    private static T? FindChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T found) return found;
            if (FindChild<T>(child) is { } deeper) return deeper;
        }
        return null;
    }
}
