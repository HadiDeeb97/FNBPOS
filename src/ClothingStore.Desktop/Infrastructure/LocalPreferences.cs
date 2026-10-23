using System.IO;
using System.Text.Json;

namespace ClothingStore.Desktop.Infrastructure;

/// <summary>
/// Per-PC preferences (language on the sign-in screen, sidebar, panel sizes and column widths, printers), kept in
/// %LocalAppData%\ClothingStorePOS\preferences.json. Losing the file only resets the layout.
/// </summary>
public sealed class LocalPreferences
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ClothingStorePOS", "preferences.json");

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static LocalPreferences Current { get; } = Load();

    public string? Language { get; set; }
    public bool SidebarCollapsed { get; set; }
    public Dictionary<string, string> Layout { get; set; } = [];

    /// <summary>Label size and content last used on this PC (each PC has its own label printer).</summary>
    public Services.LabelOptions? Labels { get; set; }

    /// <summary>Printer receipts go to without asking (null = show the Windows print dialog).</summary>
    public string? ReceiptPrinter { get; set; }

    /// <summary>Printer price labels go to without asking (null = show the Windows print dialog).</summary>
    public string? LabelPrinter { get; set; }

    /// <summary>A cash drawer is plugged into this PC's receipt printer (opened by a signal sent to the printer).</summary>
    public bool CashDrawer { get; set; }

    /// <summary>Which signal opens it: almost every printer takes the standard one.</summary>
    public CashDrawerSignal DrawerSignal { get; set; } = CashDrawerSignal.Standard;

    public bool DrawerOnCashSale { get; set; } = true;
    public bool DrawerOnCashRefund { get; set; } = true;
    public bool DrawerOnCashMovement { get; set; } = true;
    public bool DrawerOnShift { get; set; } = true;

    /// <summary>A second screen facing the customer shows the cart, totals and change (Settings → Customer display).</summary>
    public bool CustomerDisplay { get; set; }

    /// <summary>Windows name of that screen (e.g. \\.\DISPLAY2); null or unplugged = the first screen that isn't the main one.</summary>
    public string? CustomerDisplayScreen { get; set; }

    /// <summary>New product: "Save &amp; add new" keeps the details for the next product (only the name is cleared).</summary>
    public bool KeepProductDetails { get; set; }

    /// <summary>Copies of a receipt printed by default on this PC.</summary>
    public int ReceiptCopies { get; set; } = 1;

    /// <summary>Delivery fee of the last online order taken here, offered for the next one.</summary>
    public decimal? LastDeliveryFee { get; set; }

    /// <summary>Delivery company / driver of the last online order taken here, offered for the next one.</summary>
    public string? LastCourier { get; set; }

    /// <summary>The installed program put its shortcut on the desktop already (it isn't made again once deleted).</summary>
    public bool DesktopShortcutMade { get; set; }

    /// <summary>Text size (zoom) of the POS on this PC: 1 = 100%. Snapped to ZoomSteps when read.</summary>
    public double UiScale { get; set; } = 1.0;

    /// <summary>This PC runs the POS full screen (no title bar or taskbar); it starts that way next time too.</summary>
    public bool FullScreen { get; set; }

    /// <summary>Day the license expiry warning was last shown here (shown once a day).</summary>
    public DateTime? LicenseWarnedOn { get; set; }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, Json));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Preferences are a convenience; never fail the app over them.
        }
    }

    private static LocalPreferences Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<LocalPreferences>(File.ReadAllText(FilePath), Json) ?? new LocalPreferences();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
        }
        return new LocalPreferences();
    }
}
