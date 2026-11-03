using System.Collections.Concurrent;
using System.Globalization;
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

    // ---- Every number box in the app ---------------------------------------------------------------------------

    /// <summary>
    /// Every box that holds a number (a price, a cost, a quantity, an amount...) behaves this way, in every window: its
    /// value is selected when it is clicked or tabbed into, so typing 20 over a 0 gives 20, not 020. A box counts as a
    /// number box when it is bound to a number or shows one; text boxes (names, notes) keep the normal caret.
    /// </summary>
    public static void EnableForNumberBoxes()
    {
        EventManager.RegisterClassHandler(typeof(TextBox), UIElement.GotKeyboardFocusEvent, new KeyboardFocusChangedEventHandler(OnAnyGotFocus));
        EventManager.RegisterClassHandler(typeof(TextBox), UIElement.PreviewMouseLeftButtonDownEvent, new MouseButtonEventHandler(OnAnyMouseDown));
    }

    private static void OnAnyGotFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is TextBox box && !GetIsEnabled(box) && IsNumberBox(box)) box.SelectAll();
    }

    private static void OnAnyMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not TextBox { IsKeyboardFocusWithin: false } box || GetIsEnabled(box) || !IsNumberBox(box)) return;
        box.Focus();
        e.Handled = true; // the first click only focuses (and selects); a second click places the caret
    }

    private static readonly ConcurrentDictionary<(Type, string), bool> NumberProperties = new();

    private static bool IsNumberBox(TextBox box)
    {
        if (box.IsReadOnly || box.AcceptsReturn || !box.IsEnabled) return false;
        if (box.Text.Length > 0 && LooksLikeNumber(box.Text)) return true;
        // An empty box bound to a number (a price not typed yet) counts too.
        return box.GetBindingExpression(TextBox.TextProperty) is { ResolvedSource: { } source, ResolvedSourcePropertyName: { } name }
               && NumberProperties.GetOrAdd((source.GetType(), name), static key =>
               {
                   var type = key.Item1.GetProperty(key.Item2)?.PropertyType;
                   type = type is null ? null : Nullable.GetUnderlyingType(type) ?? type;
                   return type == typeof(decimal) || type == typeof(double) || type == typeof(float)
                          || type == typeof(int) || type == typeof(long) || type == typeof(short);
               });
    }

    private static bool LooksLikeNumber(string text) =>
        decimal.TryParse(text, NumberStyles.Number, CultureInfo.CurrentCulture, out _)
        || decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out _);

    private static void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not TextBox { IsKeyboardFocusWithin: false } box) return;
        box.Focus();
        e.Handled = true; // the caret stays out of it, so the selection made on focus remains
    }
}
