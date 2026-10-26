using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using ClothingStore.Core.Text;

namespace ClothingStore.Desktop.Infrastructure;

/// <summary>
/// Suggestions under a text box while typing (colours in the product editor): Down/Up to choose, Tab or Enter to take
/// one, Esc to close. With <see cref="IsListProperty"/> the box holds a list ("Black, White, Na…"): the word being typed
/// is completed and ", " added for the next one.
/// </summary>
public static class TypeAhead
{
    public static readonly DependencyProperty ItemsProperty = DependencyProperty.RegisterAttached(
        "Items", typeof(IEnumerable<string>), typeof(TypeAhead), new PropertyMetadata(null, OnItemsChanged));

    public static readonly DependencyProperty IsListProperty = DependencyProperty.RegisterAttached(
        "IsList", typeof(bool), typeof(TypeAhead), new PropertyMetadata(false));

    private static readonly DependencyProperty StateProperty = DependencyProperty.RegisterAttached(
        "State", typeof(State), typeof(TypeAhead));

    public static IEnumerable<string>? GetItems(DependencyObject d) => (IEnumerable<string>?)d.GetValue(ItemsProperty);
    public static void SetItems(DependencyObject d, IEnumerable<string>? value) => d.SetValue(ItemsProperty, value);
    public static bool GetIsList(DependencyObject d) => (bool)d.GetValue(IsListProperty);
    public static void SetIsList(DependencyObject d, bool value) => d.SetValue(IsListProperty, value);

    /// <summary>True while <paramref name="element"/> shows suggestions (Esc closes them, not the window).</summary>
    public static bool IsOpen(DependencyObject? element) => element?.GetValue(StateProperty) is State { Popup.IsOpen: true };

    private static readonly char[] Separators = [',', '،', ';'];

    private sealed class State
    {
        public required Popup Popup { get; init; }
        public required ListBox List { get; init; }
        public bool Applying { get; set; }
    }

    private static void OnItemsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TextBox box || box.GetValue(StateProperty) is State) return;
        var list = new ListBox { MaxHeight = 220, Focusable = false, BorderThickness = new Thickness(0), Background = Brushes.Transparent };
        var popup = new Popup
        {
            PlacementTarget = box, Placement = PlacementMode.Bottom, StaysOpen = false, AllowsTransparency = true,
            Child = new Border
            {
                Child = list, Padding = new Thickness(4), CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1),
                Background = Resource("SurfaceBrush", Brushes.White), BorderBrush = Resource("BorderBrush", Brushes.LightGray),
            },
        };
        var state = new State { Popup = popup, List = list };
        box.SetValue(StateProperty, state);

        box.TextChanged += (_, _) => Refresh(box, state);
        box.PreviewKeyDown += (_, args) => OnKey(box, state, args);
        box.LostKeyboardFocus += (_, _) => popup.IsOpen = false;
        list.PreviewMouseLeftButtonUp += (_, args) =>
        {
            if ((args.OriginalSource as DependencyObject)?.FindAncestor<ListBoxItem>() is { Content: string chosen })
                Accept(box, state, chosen);
        };
    }

    private static Brush Resource(string key, Brush fallback) => Application.Current?.TryFindResource(key) as Brush ?? fallback;

    private static T? FindAncestor<T>(this DependencyObject start) where T : DependencyObject
    {
        for (var d = start; d is not null; d = VisualTreeHelper.GetParent(d))
            if (d is T found) return found;
        return null;
    }

    /// <summary>The word under the caret: from after the last separator before it to the next one after it.</summary>
    private static (int Start, int End) Word(TextBox box)
    {
        var text = box.Text;
        var caret = Math.Min(box.CaretIndex, text.Length);
        if (!GetIsList(box)) return (0, text.Length);
        var start = text.LastIndexOfAny(Separators, Math.Max(0, caret - 1)) + 1;
        if (caret == 0) start = 0;
        var end = text.IndexOfAny(Separators, caret);
        return (start, end < 0 ? text.Length : end);
    }

    private static void Refresh(TextBox box, State state)
    {
        // Only while someone types here: not when a colour button or the program changes the text.
        if (state.Applying || !box.IsKeyboardFocusWithin || GetItems(box) is not { } items)
        {
            state.Popup.IsOpen = false;
            return;
        }
        var (start, _) = Word(box);
        var typed = box.Text[start..Math.Min(box.CaretIndex, box.Text.Length)].Trim();
        var others = GetIsList(box) ? ColorNames.Split(box.Text).Where(c => !string.Equals(c, typed, StringComparison.OrdinalIgnoreCase)) : null;
        var suggestions = ColorNames.Suggest(typed, items, 6, others);
        if (suggestions.Count == 0)
        {
            state.Popup.IsOpen = false;
            return;
        }
        state.List.ItemsSource = suggestions;
        state.List.SelectedIndex = 0;
        state.Popup.MinWidth = Math.Max(160, box.ActualWidth);
        state.Popup.IsOpen = true;
    }

    private static void OnKey(TextBox box, State state, KeyEventArgs e)
    {
        if (!state.Popup.IsOpen) return;
        var count = state.List.Items.Count;
        switch (e.Key)
        {
            case Key.Down:
                state.List.SelectedIndex = (state.List.SelectedIndex + 1) % count;
                e.Handled = true;
                break;
            case Key.Up:
                state.List.SelectedIndex = (state.List.SelectedIndex - 1 + count) % count;
                e.Handled = true;
                break;
            case Key.Tab or Key.Enter when state.List.SelectedItem is string chosen:
                Accept(box, state, chosen);
                e.Handled = true; // not the next box, not the window's Save
                break;
            case Key.Escape:
                state.Popup.IsOpen = false;
                e.Handled = true; // not the window
                break;
        }
    }

    private static void Accept(TextBox box, State state, string chosen)
    {
        state.Applying = true;
        try
        {
            if (GetIsList(box))
            {
                var (start, end) = Word(box);
                var before = box.Text[..start];
                var after = box.Text[end..];
                var word = (start > 0 ? " " : "") + chosen;
                // The next colour can be typed straight away.
                var next = after.Length == 0 ? ", " : "";
                box.Text = before + word + next + after;
                box.CaretIndex = before.Length + word.Length + next.Length;
            }
            else
            {
                box.Text = chosen;
                box.CaretIndex = chosen.Length;
            }
        }
        finally
        {
            state.Applying = false;
            state.Popup.IsOpen = false;
        }
    }
}
