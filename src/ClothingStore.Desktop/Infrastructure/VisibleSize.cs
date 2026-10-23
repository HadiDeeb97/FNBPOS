using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace ClothingStore.Desktop.Infrastructure;

/// <summary>
/// How much of an element is really on screen. When a panel's minimum sizes don't fit, WPF still lays it out at that
/// minimum and cuts off the far side, so its ActualWidth never drops below it and code reading ActualWidth (or waiting
/// for SizeChanged) never notices the window got narrower. The cut shows up higher up: some ancestor is wider than the
/// space its own parent gave it. That excess is what is hidden.
/// </summary>
public static class VisibleSize
{
    public static double Width(FrameworkElement element)
    {
        double hidden = 0;
        for (DependencyObject? d = element; d is not null and not Window and not Popup; d = VisualTreeHelper.GetParent(d))
        {
            if (d is not FrameworkElement fe) continue;
            // A zoomed element (UiScale) is laid out in its own scale: its size and its slot can't be compared, and it
            // is the top of the page anyway (it fills the window).
            if (fe != element && fe.LayoutTransform is { Value.IsIdentity: false }) break;
            var slot = LayoutInformation.GetLayoutSlot(fe).Width;
            var used = fe.ActualWidth + fe.Margin.Left + fe.Margin.Right;
            if (slot > 0 && used - slot > hidden) hidden = used - slot;
        }
        return Math.Max(0, element.ActualWidth - hidden);
    }
}
