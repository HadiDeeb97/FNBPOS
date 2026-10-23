using System.Runtime.InteropServices;
using System.Windows;

namespace ClothingStore.Desktop.Infrastructure;

/// <summary>A monitor connected to this PC. <see cref="Bounds"/> is in physical pixels.</summary>
public sealed record ScreenInfo(string DeviceName, int Number, Int32Rect Bounds, bool IsPrimary)
{
    public string Display => ClothingStore.Core.Localization.Loc.T("Display.ScreenName", Number, Bounds.Width, Bounds.Height);
}

/// <summary>Lists the monitors (WPF has no API for it).</summary>
public static class Screens
{
    public static List<ScreenInfo> All()
    {
        var found = new List<(string Name, Int32Rect Bounds, bool Primary)>();
        try
        {
            EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (monitor, _, _, _) =>
            {
                var info = new MonitorInfoEx { Size = Marshal.SizeOf<MonitorInfoEx>() };
                if (GetMonitorInfo(monitor, ref info))
                {
                    var r = info.Monitor;
                    found.Add((info.DeviceName, new Int32Rect(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top), (info.Flags & 1) != 0));
                }
                return true;
            }, IntPtr.Zero);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return [];
        }
        // Numbered the way Windows does (DISPLAY1, DISPLAY2...), so "Screen 2" matches Display settings.
        return found
            .OrderBy(s => DeviceNumber(s.Name))
            .Select((s, i) => new ScreenInfo(s.Name, DeviceNumber(s.Name) is var n and > 0 and < int.MaxValue ? n : i + 1, s.Bounds, s.Primary))
            .ToList();
    }

    /// <summary>The screen for the customer display: the one chosen if it's plugged in, else the first that isn't the main one.</summary>
    public static ScreenInfo? ForCustomerDisplay(string? chosen)
    {
        var secondary = All().Where(s => !s.IsPrimary).ToList();
        return secondary.FirstOrDefault(s => string.Equals(s.DeviceName, chosen, StringComparison.OrdinalIgnoreCase))
               ?? secondary.FirstOrDefault();
    }

    private static int DeviceNumber(string name)
    {
        var digits = new string(name.Reverse().TakeWhile(char.IsDigit).Reverse().ToArray());
        return int.TryParse(digits, out var n) ? n : int.MaxValue;
    }

    private delegate bool MonitorEnumProc(IntPtr monitor, IntPtr hdc, IntPtr rect, IntPtr data);

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MonitorInfoEx
    {
        public int Size;
        public Rect Monitor;
        public Rect Work;
        public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
    }

    [DllImport("user32.dll")]
    private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorEnumProc callback, IntPtr data);

    [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW", CharSet = CharSet.Unicode)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfoEx info);
}
