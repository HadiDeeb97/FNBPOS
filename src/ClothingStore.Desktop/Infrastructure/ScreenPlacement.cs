using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace ClothingStore.Desktop.Infrastructure;

/// <summary>Puts a window full screen on a given monitor without ever activating it (the register keeps the keyboard).</summary>
public static class ScreenPlacement
{
    public static void FullScreenOn(Window window, ScreenInfo screen)
    {
        window.WindowStyle = WindowStyle.None;
        window.ResizeMode = ResizeMode.NoResize;
        window.ShowInTaskbar = false;
        window.ShowActivated = false;
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            // Clicking it never takes focus from the register, and it stays out of Alt+Tab.
            SetWindowLong(hwnd, GwlExStyle, GetWindowLong(hwnd, GwlExStyle) | WsExNoActivate | WsExToolWindow);
            Cover(window, screen);
        };
        // Again once shown: moving onto a monitor with other scaling makes WPF resize the window for it. Not by
        // maximising: that always activates the window, and the register would lose the keyboard (and a scan).
        window.Loaded += (_, _) => Cover(window, screen);
        window.DpiChanged += (_, _) => window.Dispatcher.BeginInvoke(() => Cover(window, screen));
    }

    /// <summary>Exactly the monitor's area (physical pixels: the app is per-monitor DPI aware), never activating.</summary>
    public static void Cover(Window window, ScreenInfo screen)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero) return;
        var b = screen.Bounds;
        SetWindowPos(hwnd, IntPtr.Zero, b.X, b.Y, b.Width, b.Height, SwpNoZOrder | SwpNoActivate);
    }

    private const int GwlExStyle = -20;
    private const int WsExNoActivate = 0x08000000;
    private const int WsExToolWindow = 0x00000080;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern int GetWindowLong(IntPtr hwnd, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")]
    private static extern int SetWindowLong(IntPtr hwnd, int index, int value);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int width, int height, uint flags);
}
