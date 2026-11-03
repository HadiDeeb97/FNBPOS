using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace ClothingStore.Desktop.Infrastructure;

/// <summary>
/// One copy of the POS per Windows sign-in. Opening it again (the desktop shortcut, the taskbar, the Start menu) brings
/// the copy already running to the front, restored if it was minimised, and the new copy closes straight away. Two
/// copies on one till would each keep their own cart, drawer and customer screen.
/// </summary>
public static class SingleInstance
{
    private const string Name = @"Local\ClothingStorePOS";

    // Kept for the app's lifetime: Windows releases them when the app ends, however it ends.
    private static Mutex? _running;
    private static EventWaitHandle? _showRequest;

    /// <summary>True for the first copy; false when another is already running (it has been asked to come to the front).</summary>
    public static bool IsFirst()
    {
        _running = new Mutex(initiallyOwned: true, Name + ".Running", out var first);
        if (first)
        {
            _showRequest = new EventWaitHandle(false, EventResetMode.AutoReset, Name + ".Show");
            return true;
        }

        // This copy was just opened by the user, so it may hand the front of the screen over to the running one.
        AllowSetForegroundWindow(AsfwAny);
        try
        {
            using var show = EventWaitHandle.OpenExisting(Name + ".Show");
            show.Set();
        }
        catch (WaitHandleCannotBeOpenedException)
        {
            // The other copy is still starting: it comes up by itself.
        }
        return false;
    }

    /// <summary>The first copy: whenever the app is opened again, <paramref name="bringToFront"/> runs on the screen's thread.</summary>
    public static void WhenOpenedAgain(Application app, Action bringToFront)
    {
        if (_showRequest is not { } request) return;
        var thread = new Thread(() =>
        {
            while (request.WaitOne()) app.Dispatcher.BeginInvoke(bringToFront);
        }) { IsBackground = true, Name = "Opened again" };
        thread.Start();
    }

    /// <summary>Shows <paramref name="window"/> in front, restoring it from the taskbar (maximised again if it was).</summary>
    public static void BringToFront(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle != IntPtr.Zero && IsIconic(handle)) ShowWindow(handle, SwRestore);
        if (!window.IsVisible) window.Show();
        window.Activate();
        // Windows sometimes only flashes the taskbar button; a moment on top brings it forward for sure.
        window.Topmost = true;
        window.Topmost = false;
        window.Focus();
    }

    private const int AsfwAny = -1;
    private const int SwRestore = 9;

    [DllImport("user32.dll")]
    private static extern bool AllowSetForegroundWindow(int processId);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr window);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr window, int command);
}
