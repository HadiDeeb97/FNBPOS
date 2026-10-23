using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace ClothingStore.Desktop.Infrastructure;

/// <summary>
/// The same behaviour for every search box (set by the SearchBox/FilterBox styles): a × button clears it, Esc clears
/// it, and Ctrl+F anywhere on a page jumps to the page's search box (<see cref="FocusFirst"/>).
/// </summary>
public static class SearchField
{
    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled", typeof(bool), typeof(SearchField), new PropertyMetadata(false, OnIsEnabledChanged));

    public static bool GetIsEnabled(DependencyObject d) => (bool)d.GetValue(IsEnabledProperty);
    public static void SetIsEnabled(DependencyObject d, bool value) => d.SetValue(IsEnabledProperty, value);

    private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TextBox box || e.NewValue is not true) return;
        box.Loaded += (_, _) =>
        {
            if (box.Template?.FindName("PART_Clear", box) is ButtonBase clear && clear.Tag is not "wired")
            {
                clear.Tag = "wired";
                clear.Click += (_, _) => Clear(box);
            }
        };
        // Bubbling and only when nobody else used Esc (the register uses it first to close its results).
        box.KeyDown += (_, args) =>
        {
            if (args.Key == Key.Escape && !args.Handled && box.Text.Length > 0)
            {
                Clear(box);
                args.Handled = true;
            }
        };
    }

    private static void Clear(TextBox box)
    {
        box.Clear();
        box.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
        box.Focus();
    }

    /// <summary>Focuses the first visible search box inside <paramref name="root"/>; false when there is none.</summary>
    public static bool FocusFirst(DependencyObject root)
    {
        if (Find(root) is not { } box) return false;
        box.Focus();
        box.SelectAll();
        return true;
    }

    private static TextBox? Find(DependencyObject parent)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is UIElement { IsVisible: false }) continue;
            if (child is TextBox box && GetIsEnabled(box) && box.IsEnabled) return box;
            if (Find(child) is { } found) return found;
        }
        return null;
    }
}
