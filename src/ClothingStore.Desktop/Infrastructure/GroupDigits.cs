using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ClothingStore.Core.Text;

namespace ClothingStore.Desktop.Infrastructure;

/// <summary>
/// A number box that groups thousands while typing ("10000000" shows as "10,000,000"), for amounts in pounds. The caret
/// stays after the same digit; decimals typed after the point are kept as they are. Text that isn't a number is left alone.
/// The view models read the grouped text as before (they accept grouping marks).
/// </summary>
public static class GroupDigits
{
    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled", typeof(bool), typeof(GroupDigits), new PropertyMetadata(false, OnChanged));

    public static bool GetIsEnabled(DependencyObject d) => (bool)d.GetValue(IsEnabledProperty);
    public static void SetIsEnabled(DependencyObject d, bool value) => d.SetValue(IsEnabledProperty, value);

    private static readonly DependencyProperty BusyProperty = DependencyProperty.RegisterAttached(
        "Busy", typeof(bool), typeof(GroupDigits));

    /// <summary>The text as it was after the last change, to tell a deleted grouping comma from other edits.</summary>
    private static readonly DependencyProperty LastTextProperty = DependencyProperty.RegisterAttached(
        "LastText", typeof(string), typeof(GroupDigits));

    /// <summary>Backspace or Delete, when that is the key behind the change.</summary>
    private static readonly DependencyProperty DeleteKeyProperty = DependencyProperty.RegisterAttached(
        "DeleteKey", typeof(Key), typeof(GroupDigits), new PropertyMetadata(Key.None));

    private static void OnChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TextBox box) return;
        box.TextChanged -= OnTextChanged;
        box.PreviewKeyDown -= OnPreviewKeyDown;
        if (!(bool)e.NewValue) return;
        box.TextChanged += OnTextChanged;
        box.PreviewKeyDown += OnPreviewKeyDown;
    }

    private static void OnPreviewKeyDown(object sender, KeyEventArgs e) =>
        ((TextBox)sender).SetValue(DeleteKeyProperty, e.Key is Key.Back or Key.Delete ? e.Key : Key.None);

    private static void OnTextChanged(object sender, TextChangedEventArgs e)
    {
        var box = (TextBox)sender;
        if ((bool)box.GetValue(BusyProperty)) return;
        var format = CultureInfo.CurrentCulture.NumberFormat;
        var text = box.Text;
        var caret = Math.Min(box.CaretIndex, text.Length);
        // Backspace or Delete on a grouping comma removes the digit next to it (the comma alone would just come back).
        var key = (Key)box.GetValue(DeleteKeyProperty);
        box.SetValue(DeleteKeyProperty, Key.None);
        if (key != Key.None && box.GetValue(LastTextProperty) is string last && last.Length == text.Length + 1 && format.NumberGroupSeparator.Length == 1)
        {
            var removed = Enumerable.Range(0, text.Length).FirstOrDefault(i => last[i] != text[i], text.Length);
            var digit = key == Key.Back ? removed - 1 : removed; // the digit before the comma, or after it
            if (last[removed] == format.NumberGroupSeparator[0] && digit >= 0 && digit < text.Length)
            {
                text = text.Remove(digit, 1);
                caret = digit;
            }
        }
        if (DigitGroups.Format(text, format.NumberGroupSeparator, format.NumberDecimalSeparator) is not { } grouped || grouped == box.Text)
        {
            box.SetValue(LastTextProperty, box.Text);
            return;
        }

        // The caret goes after as many digits (and decimal point) as were before it.
        var keep = Significant(text[..Math.Min(caret, text.Length)], format.NumberDecimalSeparator);
        box.SetValue(BusyProperty, true);
        try
        {
            box.Text = grouped;
            box.CaretIndex = CaretAfter(grouped, keep, format.NumberDecimalSeparator);
            box.SetValue(LastTextProperty, grouped);
        }
        finally
        {
            box.SetValue(BusyProperty, false);
        }
    }

    private static int Significant(string text, string point) =>
        text.Count(c => char.IsDigit(c)) + (text.Contains(point, StringComparison.Ordinal) ? 1 : 0);

    private static int CaretAfter(string text, int significant, string point)
    {
        if (significant == 0) return 0;
        var seen = 0;
        for (var i = 0; i < text.Length; i++)
        {
            if (char.IsDigit(text[i]) || string.CompareOrdinal(text, i, point, 0, point.Length) == 0) seen++;
            if (seen == significant) return i + 1;
        }
        return text.Length;
    }
}
