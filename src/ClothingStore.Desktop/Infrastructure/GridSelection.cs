using System.Collections;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace ClothingStore.Desktop.Infrastructure;

/// <summary>
/// Copies a multi-select grid's selected rows into a view-model list (WPF's SelectedItems can't be bound).
/// Usage: <c>SelectionMode="Extended" infra:GridSelection.Items="{Binding Selection}"</c>.
/// </summary>
public static class GridSelection
{
    public static readonly DependencyProperty ItemsProperty = DependencyProperty.RegisterAttached(
        "Items", typeof(IList), typeof(GridSelection), new PropertyMetadata(null, OnItemsChanged));

    public static IList? GetItems(DependencyObject d) => (IList?)d.GetValue(ItemsProperty);
    public static void SetItems(DependencyObject d, IList? value) => d.SetValue(ItemsProperty, value);

    private static void OnItemsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not MultiSelector grid) return;
        grid.SelectionChanged -= OnSelectionChanged;
        if (e.NewValue is not null) grid.SelectionChanged += OnSelectionChanged;
    }

    private static void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not MultiSelector grid || GetItems(grid) is not { } target) return;
        target.Clear();
        foreach (var item in grid.SelectedItems) target.Add(item);
    }
}
