using System.Runtime.InteropServices;

namespace ClothingStore.Desktop.Infrastructure;

/// <summary>The "open drawer" signal a receipt printer passes to the cash drawer plugged into it.</summary>
public enum CashDrawerSignal
{
    /// <summary>ESC p 0: drawer port pin 2. Epson, Xprinter, Rongta and most others.</summary>
    Standard,

    /// <summary>ESC p 1: pin 5, for a drawer wired to the second connector.</summary>
    SecondDrawer,

    /// <summary>BEL: Star printers in Star mode.</summary>
    Star,
}

/// <summary>Why the drawer is being opened: each can be switched off per PC in Settings.</summary>
public enum DrawerReason
{
    CashSale,
    CashRefund,
    CashMovement,
    Shift,
    Manual,
}

/// <summary>
/// Opens a cash drawer that is plugged into the receipt printer, by sending the printer the drawer signal directly
/// (the Windows driver passes it on untouched). Uses this PC's default receipt printer (Settings → Printers).
/// </summary>
public static class CashDrawer
{
    /// <summary>Opens it when this PC has a drawer and the reason is switched on; failures are silent.</summary>
    public static void OpenFor(DrawerReason reason)
    {
        var prefs = LocalPreferences.Current;
        if (!prefs.CashDrawer) return;
        var wanted = reason switch
        {
            DrawerReason.CashSale => prefs.DrawerOnCashSale,
            DrawerReason.CashRefund => prefs.DrawerOnCashRefund,
            DrawerReason.CashMovement => prefs.DrawerOnCashMovement,
            DrawerReason.Shift => prefs.DrawerOnShift,
            _ => true,
        };
        if (wanted) Open(out _);
    }

    /// <summary>Sends the signal now; false (with the reason) when it couldn't be sent.</summary>
    public static bool Open(out string? error)
    {
        var printer = LocalPreferences.Current.ReceiptPrinter;
        if (string.IsNullOrWhiteSpace(printer))
        {
            error = ClothingStore.Core.Localization.Loc.T("Drawer.NeedsPrinter");
            return false;
        }
        byte[] signal = LocalPreferences.Current.DrawerSignal switch
        {
            CashDrawerSignal.SecondDrawer => [0x1B, 0x70, 0x01, 0x19, 0xFA],
            CashDrawerSignal.Star => [0x07],
            _ => [0x1B, 0x70, 0x00, 0x19, 0xFA],
        };
        return SendRaw(printer, signal, out error);
    }

    private static bool SendRaw(string printer, byte[] data, out string? error)
    {
        error = null;
        if (!OpenPrinter(printer, out var handle, IntPtr.Zero))
        {
            error = new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error()).Message;
            return false;
        }
        try
        {
            var doc = new DocInfo { Name = "Cash drawer", DataType = "RAW" };
            if (StartDocPrinter(handle, 1, ref doc) == 0) { error = LastError(); return false; }
            try
            {
                if (!StartPagePrinter(handle)) { error = LastError(); return false; }
                var buffer = Marshal.AllocHGlobal(data.Length);
                try
                {
                    Marshal.Copy(data, 0, buffer, data.Length);
                    if (!WritePrinter(handle, buffer, data.Length, out _)) { error = LastError(); return false; }
                }
                finally
                {
                    Marshal.FreeHGlobal(buffer);
                    EndPagePrinter(handle);
                }
            }
            finally
            {
                EndDocPrinter(handle);
            }
            return true;
        }
        finally
        {
            ClosePrinter(handle);
        }
    }

    private static string LastError() => new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error()).Message;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DocInfo
    {
        [MarshalAs(UnmanagedType.LPWStr)] public string Name;
        [MarshalAs(UnmanagedType.LPWStr)] public string? OutputFile;
        [MarshalAs(UnmanagedType.LPWStr)] public string DataType;
    }

    [DllImport("winspool.drv", EntryPoint = "OpenPrinterW", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool OpenPrinter(string name, out IntPtr handle, IntPtr defaults);

    [DllImport("winspool.drv", SetLastError = true)]
    private static extern bool ClosePrinter(IntPtr handle);

    [DllImport("winspool.drv", EntryPoint = "StartDocPrinterW", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern int StartDocPrinter(IntPtr handle, int level, ref DocInfo doc);

    [DllImport("winspool.drv", SetLastError = true)]
    private static extern bool EndDocPrinter(IntPtr handle);

    [DllImport("winspool.drv", SetLastError = true)]
    private static extern bool StartPagePrinter(IntPtr handle);

    [DllImport("winspool.drv", SetLastError = true)]
    private static extern bool EndPagePrinter(IntPtr handle);

    [DllImport("winspool.drv", SetLastError = true)]
    private static extern bool WritePrinter(IntPtr handle, IntPtr bytes, int count, out int written);
}
