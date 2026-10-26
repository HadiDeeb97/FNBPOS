using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace ClothingStore.Desktop.Infrastructure;

/// <summary>
/// A box whose whole text is selected when it is clicked or tabbed into, so typing replaces it (a price, a quantity).
/// A click normally puts the caret where it lands and drops the selection; the first click here only focuses.
/// </summary>
public static class SelectAllOnFocus
{
    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled", typeof(bool), typeof(SelectAllOnFocus), new PropertyMetadata(false, OnChanged));

    public static bool GetIsEnabled(DependencyObject d) => (bool)d.GetValue(IsEnabledProperty);
    public static void SetIsEnabled(DependencyObject d, bool value) => d.SetValue(IsEnabledProperty, value);

    private static void OnChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TextBox box) return;
        if ((bool)e.NewValue)
        {
            box.GotKeyboardFocus += OnGotFocus;
            box.PreviewMouseLeftButtonDown += OnMouseDown;
        }
        else
        {
            box.GotKeyboardFocus -= OnGotFocus;
            box.PreviewMouseLeftButtonDown -= OnMouseDown;
        }
    }

    private static void OnGotFocus(object sender, KeyboardFocusChangedEventArgs e) => ((TextBox)sender).SelectAll();

    private static void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not TextBox { IsKeyboardFocusWithin: false } box) return;
        box.Focus();
        e.Handled = true; // the caret stays out of it, so the selection made on focus remains
    }
}
