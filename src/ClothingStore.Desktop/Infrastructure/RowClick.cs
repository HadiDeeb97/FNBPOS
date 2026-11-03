using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace ClothingStore.Desktop.Infrastructure;

/// <summary>Where a table was double-clicked: only a row means "open this one" (not a header, its edge or the scroll bar).</summary>
public static class RowClick
{
    public static bool IsOnRow(object sender, MouseButtonEventArgs e) =>
        sender is DataGrid grid && e.OriginalSource is DependencyObject source && ItemsControl.ContainerFromElement(grid, source) is DataGridRow;
}
