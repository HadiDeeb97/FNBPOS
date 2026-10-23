using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using ClothingStore.Core.Text;

namespace ClothingStore.Desktop.Infrastructure;

/// <summary>
/// A drop-down you can type into to search, but that only accepts an item from its list. Typing filters the list with
/// the same rules as every search box (<see cref="SmartSearch"/>); Enter or a click picks an item; leaving the box with
/// text that isn't an item puts back the chosen item. Bind <c>SelectedItem</c> (not Text).
/// <code>&lt;ComboBox infra:SmartCombo.IsEnabled="True" ItemsSource="{Binding Items}" SelectedItem="{Binding Selected}" /&gt;</code>
/// </summary>
public static class SmartCombo
{
    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled", typeof(bool), typeof(SmartCombo), new PropertyMetadata(false, OnIsEnabledChanged));

    public static bool GetIsEnabled(DependencyObject d) => (bool)d.GetValue(IsEnabledProperty);
    public static void SetIsEnabled(DependencyObject d, bool value) => d.SetValue(IsEnabledProperty, value);

    /// <summary>True while the text is being changed by the behaviour itself (not typed).</summary>
    private static readonly DependencyProperty UpdatingProperty =
        DependencyProperty.RegisterAttached("Updating", typeof(bool), typeof(SmartCombo));

    /// <summary>The last item actually picked, put back if the user types something and leaves without picking.</summary>
    private static readonly DependencyProperty CommittedProperty =
        DependencyProperty.RegisterAttached("Committed", typeof(object), typeof(SmartCombo));

    private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ComboBox combo || e.NewValue is not true) return;
        combo.IsEditable = true;
        combo.IsTextSearchEnabled = false;
        combo.StaysOpenOnEdit = true;
        combo.Loaded += (_, _) => Attach(combo);
    }

    private static void Attach(ComboBox combo)
    {
        if (combo.Template.FindName("PART_EditableTextBox", combo) is not TextBox box || box.Tag is "smart") return;
        box.Tag = "smart";

        box.TextChanged += (_, _) =>
        {
            if ((bool)combo.GetValue(UpdatingProperty) || !box.IsKeyboardFocusWithin) return;
            var text = box.Text;
            if (combo.SelectedItem is { } selected && DisplayText(combo, selected) == text) return;
            var caret = box.CaretIndex;
            SetFilter(combo, text);
            if (!combo.IsDropDownOpen && combo.HasItems) combo.IsDropDownOpen = true;
            // Opening and filtering can select the text or move the caret; keep typing where the user was.
            box.SelectionLength = 0;
            box.CaretIndex = Math.Min(caret, box.Text.Length);
        };

        combo.SelectionChanged += (_, _) =>
        {
            if ((bool)combo.GetValue(UpdatingProperty)) return;
            if (combo.SelectedItem is { } item)
            {
                combo.SetValue(CommittedProperty, item);
                if (!combo.IsDropDownOpen || !box.IsKeyboardFocusWithin) Restore(combo, box);
            }
            else if (!box.IsKeyboardFocusWithin)
            {
                combo.SetValue(CommittedProperty, null); // cleared on purpose (e.g. a clear button)
            }
        };
        combo.DropDownClosed += (_, _) => Restore(combo, box);

        combo.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter && combo.IsDropDownOpen)
            {
                // Enter picks the highlighted item, or the only one left after filtering.
                var view = CollectionViewSource.GetDefaultView(combo.ItemsSource);
                var items = view?.Cast<object>().Take(2).ToList() ?? [];
                if (combo.SelectedItem is null && items.Count == 1) combo.SelectedItem = items[0];
                combo.IsDropDownOpen = false;
                Restore(combo, box);
                e.Handled = true;
            }
            else if (e.Key == Key.Escape && combo.IsDropDownOpen)
            {
                combo.IsDropDownOpen = false;
                Restore(combo, box);
                e.Handled = true;
            }
        };

        combo.LostKeyboardFocus += (_, _) =>
        {
            if (combo.IsKeyboardFocusWithin || combo.IsDropDownOpen) return;
            Restore(combo, box);
        };
    }

    /// <summary>Shows the chosen item's text again and the whole list.</summary>
    private static void Restore(ComboBox combo, TextBox box)
    {
        combo.SetValue(UpdatingProperty, true);
        try
        {
            SetFilter(combo, null);
            if (combo.SelectedItem is null && combo.GetValue(CommittedProperty) is { } committed && combo.Items.Contains(committed))
                combo.SelectedItem = committed; // typed without picking: keep what was chosen before
            box.Text = combo.SelectedItem is { } item ? DisplayText(combo, item) : "";
        }
        finally
        {
            combo.SetValue(UpdatingProperty, false);
        }
    }

    private static void SetFilter(ComboBox combo, string? text)
    {
        if (combo.ItemsSource is null || CollectionViewSource.GetDefaultView(combo.ItemsSource) is not { } view) return;
        view.Filter = string.IsNullOrWhiteSpace(text) ? null : item => item is not null && SmartSearch.Matches(text, DisplayText(combo, item));
    }

    private static string DisplayText(ComboBox combo, object item)
    {
        if (!string.IsNullOrEmpty(combo.DisplayMemberPath)
            && TypeDescriptor.GetProperties(item)[combo.DisplayMemberPath]?.GetValue(item) is { } value)
            return value.ToString() ?? "";
        return item.ToString() ?? "";
    }
}
