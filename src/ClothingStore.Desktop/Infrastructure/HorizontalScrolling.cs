using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace ClothingStore.Desktop.Infrastructure;

/// <summary>
/// Sideways scrolling for tables and panels. WPF ignores the horizontal wheel that a laptop touchpad sends for a
/// two-finger swipe left/right (and a tilting mouse wheel), so wide tables couldn't be scrolled sideways with it. Shift +
/// the mouse wheel scrolls sideways too, as in most Windows programs. Applies to every window of the app.
/// </summary>
public static class HorizontalScrolling
{
    private const int WmMouseWheel = 0x020A;
    private const int WmMouseHWheel = 0x020E;
    private const int MkShift = 0x0004;

    /// <summary>Pixels moved per wheel notch (120 units), the same distance WPF scrolls vertically.</summary>
    private const double PixelsPerNotch = 48;

    private static bool _enabled;

    public static void Enable()
    {
        if (_enabled) return;
        _enabled = true;
        ComponentDispatcher.ThreadPreprocessMessage += OnThreadMessage;
    }

    private static void OnThreadMessage(ref MSG msg, ref bool handled)
    {
        if (handled) return;
        var wParam = msg.wParam.ToInt64();
        var sideways = msg.message == WmMouseHWheel;
        var shiftWheel = msg.message == WmMouseWheel && (wParam & MkShift) != 0;
        if (!sideways && !shiftWheel) return;

        if (FindScrollable(Mouse.DirectlyOver as DependencyObject) is not { } viewer) return;
        var delta = (short)((wParam >> 16) & 0xFFFF);
        // A swipe or tilt to the right is positive and moves the view right; Shift + wheel down (negative) does the same.
        var amount = (sideways ? delta : -delta) * PixelsPerNotch / 120.0;
        if (viewer.FlowDirection == FlowDirection.RightToLeft) amount = -amount; // the view is mirrored in Arabic
        viewer.ScrollToHorizontalOffset(viewer.HorizontalOffset + amount);
        handled = true;
    }

    /// <summary>The nearest scroll area around the pointer that can actually move sideways.</summary>
    private static ScrollViewer? FindScrollable(DependencyObject? element)
    {
        for (var d = element; d is not null; d = Parent(d))
        {
            if (d is ScrollViewer { ScrollableWidth: > 0 } viewer) return viewer;
        }
        return null;
    }

    private static DependencyObject? Parent(DependencyObject d) =>
        (d is Visual or Visual3D ? VisualTreeHelper.GetParent(d) : null) ?? LogicalTreeHelper.GetParent(d);
}
