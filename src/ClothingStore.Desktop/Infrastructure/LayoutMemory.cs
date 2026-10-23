using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace ClothingStore.Desktop.Infrastructure;

/// <summary>
/// Remembers panel sizes and table columns on this PC. Set <c>LayoutMemory.Key</c> on a <see cref="Grid"/> that
/// contains <see cref="GridSplitter"/>s to keep its column/row sizes, or on a <see cref="DataGrid"/> to keep column
/// widths and order. Sizes are saved when a splitter is released or the table is closed.
/// </summary>
public static class LayoutMemory
{
    public static readonly DependencyProperty KeyProperty = DependencyProperty.RegisterAttached(
        "Key", typeof(string), typeof(LayoutMemory), new PropertyMetadata(null, OnKeyChanged));

    public static string? GetKey(DependencyObject element) => (string?)element.GetValue(KeyProperty);
    public static void SetKey(DependencyObject element, string? value) => element.SetValue(KeyProperty, value);

    /// <summary>Forgets every saved size, so screens go back to their default layout.</summary>
    public static void ResetAll()
    {
        LocalPreferences.Current.Layout.Clear();
        LocalPreferences.Current.Save();
    }

    private static void OnKeyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        switch (d)
        {
            case DataGrid grid:
                grid.Loaded -= OnDataGridLoaded;
                grid.Unloaded -= OnDataGridUnloaded;
                grid.Loaded += OnDataGridLoaded;
                grid.Unloaded += OnDataGridUnloaded;
                break;
            case Grid grid:
                grid.Loaded -= OnGridLoaded;
                grid.Loaded += OnGridLoaded;
                grid.RemoveHandler(Thumb.DragCompletedEvent, (DragCompletedEventHandler)OnSplitterReleased);
                grid.AddHandler(Thumb.DragCompletedEvent, (DragCompletedEventHandler)OnSplitterReleased);
                break;
        }
    }

    // ---- Panels (Grid + GridSplitter) ------------------------------------------------------------

    private static void OnGridLoaded(object sender, RoutedEventArgs e)
    {
        var grid = (Grid)sender;
        if (!TryGet(grid, "panels", out var saved)) return;
        var parts = saved.Split('|');
        if (parts.Length != 2) return;
        Restore(grid.ColumnDefinitions.Select(c => (DefinitionBase)c).ToList(), parts[0]);
        Restore(grid.RowDefinitions.Select(r => (DefinitionBase)r).ToList(), parts[1]);
    }

    private static void OnSplitterReleased(object sender, DragCompletedEventArgs e)
    {
        if (e.OriginalSource is not GridSplitter splitter || splitter.Parent != sender) return;
        var grid = (Grid)sender;
        var columns = string.Join(";", grid.ColumnDefinitions.Select(c => Encode(c.Width, c.ActualWidth)));
        var rows = string.Join(";", grid.RowDefinitions.Select(r => Encode(r.Height, r.ActualHeight)));
        Set(grid, "panels", columns + "|" + rows);
    }

    /// <summary>Star sizes are saved as their current pixel share, so proportions survive a window resize.</summary>
    private static string Encode(GridLength length, double actual) => length.GridUnitType switch
    {
        GridUnitType.Auto => "auto",
        GridUnitType.Star => actual.ToString("0.#", CultureInfo.InvariantCulture) + "*",
        _ => actual.ToString("0.#", CultureInfo.InvariantCulture),
    };

    private static void Restore(IList<DefinitionBase> definitions, string saved)
    {
        var sizes = saved.Length == 0 ? [] : saved.Split(';');
        if (sizes.Length != definitions.Count) return; // the screen's layout changed since it was saved
        for (var i = 0; i < sizes.Length; i++)
        {
            if (sizes[i] == "auto") continue;
            var star = sizes[i].EndsWith('*');
            if (!double.TryParse(sizes[i].TrimEnd('*'), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) || value <= 0)
                continue;
            var length = new GridLength(value, star ? GridUnitType.Star : GridUnitType.Pixel);
            switch (definitions[i])
            {
                case ColumnDefinition c when c.Width.GridUnitType != GridUnitType.Auto: c.Width = length; break;
                case RowDefinition r when r.Height.GridUnitType != GridUnitType.Auto: r.Height = length; break;
            }
        }
    }

    // ---- Tables (DataGrid) ------------------------------------------------------------------------

    private static void OnDataGridLoaded(object sender, RoutedEventArgs e)
    {
        var grid = (DataGrid)sender;
        // "cols" (not the older "columns"): widths saved before columns sized themselves to their content are ignored.
        if (!TryGet(grid, "cols", out var saved)) return;
        var columns = saved.Split(';');
        if (columns.Length != grid.Columns.Count) return;
        var order = new List<(DataGridColumn Column, int Index)>();
        for (var i = 0; i < columns.Length; i++)
        {
            var parts = columns[i].Split(':');
            if (parts.Length != 2) continue;
            // "-" = the user never resized it: keep its own sizing (fit to content, or share of the free space).
            // A fixed column (a bin or tick-box column) keeps its own width, whatever an older version saved.
            if (grid.Columns[i].CanUserResize
                && double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var width) && width > 10)
                grid.Columns[i].Width = new DataGridLength(Math.Max(width, grid.Columns[i].MinWidth));
            if (int.TryParse(parts[1], out var index) && index >= 0 && index < grid.Columns.Count)
                order.Add((grid.Columns[i], index));
        }
        // Apply positions from left to right so moving one column doesn't undo another.
        foreach (var (column, index) in order.OrderBy(o => o.Index)) column.DisplayIndex = index;
    }

    private static void OnDataGridUnloaded(object sender, RoutedEventArgs e)
    {
        var grid = (DataGrid)sender;
        if (grid.Columns.Count == 0 || grid.Columns.All(c => c.ActualWidth <= 0)) return;
        // Only widths the user dragged are pixel widths; columns still auto- or star-sized are saved as "-" so they keep
        // fitting their content (and the window) next time.
        Set(grid, "cols", string.Join(";", grid.Columns.Select(c =>
            $"{(c.Width.IsAbsolute ? c.ActualWidth.ToString("0.#", CultureInfo.InvariantCulture) : "-")}:{c.DisplayIndex}")));
    }

    // ---- Storage ----------------------------------------------------------------------------------

    private static bool TryGet(DependencyObject element, string kind, out string value)
    {
        value = "";
        return GetKey(element) is { Length: > 0 } key
               && LocalPreferences.Current.Layout.TryGetValue($"{kind}:{key}", out value!);
    }

    private static void Set(DependencyObject element, string kind, string value)
    {
        if (GetKey(element) is not { Length: > 0 } key) return;
        LocalPreferences.Current.Layout[$"{kind}:{key}"] = value;
        LocalPreferences.Current.Save();
    }
}
