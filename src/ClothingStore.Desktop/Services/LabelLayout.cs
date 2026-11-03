using ClothingStore.Core.Localization;

namespace ClothingStore.Desktop.Services;

/// <summary>What to print on a price label and how labels sit on the paper. Saved per PC (each has its own printer).</summary>
public sealed class LabelOptions
{
    /// <summary>true = sheets of labels (A4/Letter); false = a label printer, one label per page.</summary>
    public bool Sheet { get; set; } = true;

    public string Preset { get; set; } = "L7160";
    public double WidthMm { get; set; } = 63.5;
    public double HeightMm { get; set; } = 38.1;

    // Sheet layout: where the first label starts and the space between labels.
    public double MarginTopMm { get; set; } = 15.1;
    public double MarginLeftMm { get; set; } = 7.2;
    public double GapXMm { get; set; } = 2.5;
    public double GapYMm { get; set; }

    public bool ShowStoreName { get; set; }
    public bool ShowName { get; set; } = true;
    public bool ShowVariant { get; set; } = true;
    public bool ShowSku { get; set; }
    public bool ShowPrice { get; set; } = true;
    public bool ShowLbpPrice { get; set; }
    public bool ShowBarcode { get; set; } = true;

    // ---- Look (see LabelStyle) ----

    /// <summary>The ready-made style these options came from, or "Custom" once changed.</summary>
    public string Style { get; set; } = "Classic";
    public LabelArrangement Arrangement { get; set; } = LabelArrangement.Stacked;
    public LabelAlign Align { get; set; } = LabelAlign.Center;
    public LabelBarcodeSize BarcodeSize { get; set; } = LabelBarcodeSize.Medium;
    public LabelDigits Digits { get; set; } = LabelDigits.Shop;
    public LabelPriceStyle PriceStyle { get; set; } = LabelPriceStyle.Normal;
    public LabelVariantFormat VariantFormat { get; set; } = LabelVariantFormat.Slash;

    /// <summary>A narrower typeface, so more of a long name fits on a small label.</summary>
    public bool NarrowFont { get; set; }

    /// <summary>Room at the top for the hole of a hanging tag.</summary>
    public bool HoleSpace { get; set; }

    /// <summary>Outline around each label (handy to check alignment on plain paper).</summary>
    public LabelBorder Border { get; set; } = LabelBorder.None;

    // ---- Saved by versions before the label styles (read once, then replaced by Digits and Border) ----

    /// <summary>Which version of these options was saved; older saves are brought up to date by <see cref="Upgrade"/>.</summary>
    public int Version { get; set; }
    public bool ShowBarcodeText { get; set; } = true;
    public bool ShowBorder { get; set; }

    private const int CurrentVersion = 2;

    /// <summary>Options saved before the label styles: "barcode digits off" and "border on" carry over.</summary>
    public LabelOptions Upgrade()
    {
        if (Version >= CurrentVersion) return this;
        if (!ShowBarcodeText) Digits = LabelDigits.Hidden;
        if (ShowBorder) Border = LabelBorder.Rounded;
        ShowBarcodeText = true;
        ShowBorder = false;
        Version = CurrentVersion;
        return this;
    }

    /// <summary>Multiplier for all text (0.7 = small, 1 = normal, 1.3 = large).</summary>
    public double TextScale { get; set; } = 1.0;

    public LabelOptions Clone() => (LabelOptions)MemberwiseClone();

    public static double MmToPx(double mm) => mm * 96.0 / 25.4;

    public double WidthPx => MmToPx(WidthMm);
    public double HeightPx => MmToPx(HeightMm);

    /// <summary>Labels that fit on a page of the given size (in px), as columns x rows.</summary>
    public (int Columns, int Rows) Grid(double pageWidthPx, double pageHeightPx)
    {
        if (!Sheet) return (1, 1);
        var columns = (int)Math.Floor((pageWidthPx - MmToPx(MarginLeftMm) + MmToPx(GapXMm)) / (WidthPx + MmToPx(GapXMm)) + 1e-6);
        var rows = (int)Math.Floor((pageHeightPx - MmToPx(MarginTopMm) + MmToPx(GapYMm)) / (HeightPx + MmToPx(GapYMm)) + 1e-6);
        return (Math.Max(1, columns), Math.Max(1, rows));
    }

    /// <summary>A4 in px, used to tell the user how many labels fit before the printer is chosen.</summary>
    public static readonly (double Width, double Height) A4 = (MmToPx(210), MmToPx(297));
}

/// <summary>A ready-made label size. <see cref="NameKey"/> is a string key; the size is shown next to it.</summary>
public sealed record LabelPreset(
    string Id, string NameKey, bool Sheet, double WidthMm, double HeightMm,
    double MarginTopMm = 0, double MarginLeftMm = 0, double GapXMm = 0, double GapYMm = 0)
{
    public static readonly IReadOnlyList<LabelPreset> All =
    [
        new("L7160", "Labels.Preset.Sheet", true, 63.5, 38.1, 15.1, 7.2, 2.5, 0),   // 21 per A4
        new("L7159", "Labels.Preset.Sheet", true, 63.5, 33.9, 12.9, 7.2, 2.5, 0),   // 24 per A4
        new("L7651", "Labels.Preset.Sheet", true, 38.1, 21.2, 10.7, 4.7, 2.5, 0),   // 65 per A4
        new("A4-70x37", "Labels.Preset.Sheet", true, 70, 37, 0, 0, 0, 0),          // 24 per A4, edge to edge
        new("Roll-50x30", "Labels.Preset.Roll", false, 50, 30),
        new("Roll-40x25", "Labels.Preset.Roll", false, 40, 25),
        new("Roll-58x40", "Labels.Preset.Roll", false, 58, 40),
        new("Tag-35x60", "Labels.Preset.Tag", false, 35, 60),
        new("Custom", "Labels.Preset.Custom", true, 0, 0),
    ];

    public void ApplyTo(LabelOptions o)
    {
        o.Preset = Id;
        if (Id == "Custom") return; // keep whatever was there
        o.Sheet = Sheet;
        o.WidthMm = WidthMm;
        o.HeightMm = HeightMm;
        o.MarginTopMm = MarginTopMm;
        o.MarginLeftMm = MarginLeftMm;
        o.GapXMm = GapXMm;
        o.GapYMm = GapYMm;
    }
}

/// <summary>Where the barcode sits: under the text, beside it, or beside it turned upright (narrow tags).</summary>
public enum LabelArrangement { Stacked, Beside, Upright }

public enum LabelAlign { Left, Center, Right }

public enum LabelBarcodeSize { Small, Medium, Large }

/// <summary>The digits of the barcode: as on products (EAN-13 "0 123456 789012"), one line under the bars, or none.</summary>
public enum LabelDigits { Shop, Line, Hidden }

/// <summary>The price as normal text, large and bold, or white on a black box.</summary>
public enum LabelPriceStyle { Normal, Large, Inverted }

/// <summary>"M / Black" or "Size M · Black".</summary>
public enum LabelVariantFormat { Slash, Labelled }

public enum LabelBorder { None, Line, Rounded }

/// <summary>A ready-made look for price labels. Choosing one sets the look options; they can still be changed after.</summary>
public sealed record LabelStyle(
    string Id, LabelArrangement Arrangement, LabelAlign Align, LabelBarcodeSize BarcodeSize, LabelPriceStyle PriceStyle,
    bool StoreName, bool Name, bool Variant, bool HoleSpace)
{
    public const string Custom = "Custom";

    public static readonly IReadOnlyList<LabelStyle> All =
    [
        new("Classic", LabelArrangement.Stacked, LabelAlign.Center, LabelBarcodeSize.Medium, LabelPriceStyle.Normal, false, true, true, false),
        new("BigPrice", LabelArrangement.Stacked, LabelAlign.Center, LabelBarcodeSize.Small, LabelPriceStyle.Large, false, true, true, false),
        new("BarcodeFirst", LabelArrangement.Stacked, LabelAlign.Center, LabelBarcodeSize.Large, LabelPriceStyle.Normal, false, true, false, false),
        new("SideBySide", LabelArrangement.Beside, LabelAlign.Left, LabelBarcodeSize.Medium, LabelPriceStyle.Normal, false, true, true, false),
        new("HangingTag", LabelArrangement.Stacked, LabelAlign.Center, LabelBarcodeSize.Medium, LabelPriceStyle.Large, true, true, true, true),
        new("Minimal", LabelArrangement.Stacked, LabelAlign.Center, LabelBarcodeSize.Medium, LabelPriceStyle.Large, false, false, false, false),
        new(Custom, LabelArrangement.Stacked, LabelAlign.Center, LabelBarcodeSize.Medium, LabelPriceStyle.Normal, false, true, true, false),
    ];

    public string NameKey => "Labels.Style." + Id;

    public void ApplyTo(LabelOptions o)
    {
        o.Style = Id;
        if (Id == Custom) return; // keep whatever was there
        o.Arrangement = Arrangement;
        o.Align = Align;
        o.BarcodeSize = BarcodeSize;
        o.PriceStyle = PriceStyle;
        o.ShowStoreName = StoreName;
        o.ShowName = Name;
        o.ShowVariant = Variant;
        o.ShowPrice = true;
        o.ShowBarcode = true;
        o.HoleSpace = HoleSpace;
    }

    /// <summary>The options still look exactly as this style sets them.</summary>
    public bool Matches(LabelOptions o) =>
        Id != Custom && o.Arrangement == Arrangement && o.Align == Align && o.BarcodeSize == BarcodeSize && o.PriceStyle == PriceStyle &&
        o.ShowStoreName == StoreName && o.ShowName == Name && o.ShowVariant == Variant && o.ShowPrice && o.ShowBarcode && o.HoleSpace == HoleSpace;
}

/// <summary>Text on a label.</summary>
public static class LabelText
{
    /// <summary>"M / Black", or "Size M · Black"; whichever of size and colour the item has.</summary>
    public static string Variant(string? size, string? color, LabelVariantFormat format)
    {
        size = string.IsNullOrWhiteSpace(size) ? null : size.Trim();
        color = string.IsNullOrWhiteSpace(color) ? null : color.Trim();
        return format == LabelVariantFormat.Labelled
            ? string.Join(" · ", new[] { size is null ? null : Loc.T("Labels.SizeWord", size), color }.Where(p => p is not null))
            : string.Join(" / ", new[] { size, color }.Where(p => p is not null));
    }
}

/// <summary>A rectangle on a label, in px (1/96 inch).</summary>
public readonly record struct LabelBox(double X, double Y, double Width, double Height)
{
    public double Right => X + Width;
    public double Bottom => Y + Height;
}

/// <summary>
/// Where the text and the barcode go on a label. The barcode always gets room to print readably: in a style that would
/// leave it too short (bars under 2 printer dots), it moves under the text across the whole label, and the text gives way.
/// </summary>
public sealed record LabelGeometry(LabelBox Text, LabelBox? Barcode, LabelArrangement Arrangement, double Pad)
{
    /// <summary>Bars this many printer dots wide or more scan easily (0.25 mm at 203 dpi).</summary>
    public const int ReadableDots = 2;

    /// <summary>The barcode's length along its bars (its width, or its height when upright).</summary>
    public double BarcodeLength => Barcode is not { } b ? 0 : Arrangement == LabelArrangement.Upright ? b.Height : b.Width;

    /// <param name="barcodeModules">The barcode's width in modules with its margins; 0 for a label without one.</param>
    public static LabelGeometry For(LabelOptions o, double width, double height, int barcodeModules, double dpi)
    {
        var pad = Math.Clamp(height * 0.05, 3, 10);
        var hole = o.HoleSpace ? Math.Min(LabelOptions.MmToPx(7), height * 0.25) : 0;
        var content = new LabelBox(pad, pad + hole, Math.Max(0, width - pad * 2), Math.Max(0, height - pad * 2 - hole));
        if (barcodeModules <= 0) return new(content, null, o.Arrangement, pad);

        var stacked = Stacked(o, content, pad);
        var stackedDots = Dots(content.Width, barcodeModules, dpi);
        var gap = 3.0;

        if (o.Arrangement == LabelArrangement.Beside)
        {
            // A column wide enough for readable bars if the label allows it, the text beside it.
            var column = Math.Max(content.Width * o.BarcodeSize switch { LabelBarcodeSize.Small => 0.48, LabelBarcodeSize.Large => 0.62, _ => 0.55 },
                Math.Ceiling(barcodeModules * ReadableDots * 96.0 / dpi));
            if (column <= content.Width - LabelOptions.MmToPx(8) && Dots(column, barcodeModules, dpi) >= Math.Min(ReadableDots, stackedDots))
            {
                var tall = Math.Min(content.Height, Math.Max(LabelOptions.MmToPx(9),
                    content.Height * o.BarcodeSize switch { LabelBarcodeSize.Small => 0.6, LabelBarcodeSize.Large => 0.95, _ => 0.8 }));
                return new(new LabelBox(content.X, content.Y, content.Width - column - gap, content.Height),
                    new LabelBox(content.Right - column, content.Y + (content.Height - tall) / 2, column, tall), LabelArrangement.Beside, pad);
            }
        }
        else if (o.Arrangement == LabelArrangement.Upright)
        {
            // Turned upright the bars run along the label's height; worth it only when that gives readable bars.
            var thickness = Math.Max(LabelOptions.MmToPx(9),
                content.Width * o.BarcodeSize switch { LabelBarcodeSize.Small => 0.25, LabelBarcodeSize.Large => 0.4, _ => 0.32 });
            if (thickness <= content.Width - LabelOptions.MmToPx(8) && Dots(content.Height, barcodeModules, dpi) >= Math.Min(ReadableDots, stackedDots))
                return new(new LabelBox(content.X, content.Y, content.Width - thickness - gap, content.Height),
                    new LabelBox(content.Right - thickness, content.Y, thickness, content.Height), LabelArrangement.Upright, pad);
        }
        return stacked;
    }

    /// <summary>Text above, the barcode across the whole label below it.</summary>
    private static LabelGeometry Stacked(LabelOptions o, LabelBox content, double pad)
    {
        var share = o.BarcodeSize switch { LabelBarcodeSize.Small => 0.32, LabelBarcodeSize.Large => 0.6, _ => 0.45 };
        // At least 9 mm (bars of 5 mm or more, plus the digits), whatever the style: the text gives way first.
        var tall = Math.Min(content.Height, Math.Max(content.Height * share, Math.Min(LabelOptions.MmToPx(9), content.Height * 0.85)));
        var text = new LabelBox(content.X, content.Y, content.Width, Math.Max(0, content.Height - tall - 2));
        return new(text, new LabelBox(content.X, content.Bottom - tall, content.Width, tall), LabelArrangement.Stacked, pad);
    }

    /// <summary>Whole printer dots per module for a barcode of <paramref name="modules"/> across <paramref name="lengthPx"/>.</summary>
    public static int Dots(double lengthPx, int modules, double dpi) =>
        modules <= 0 ? 0 : (int)Math.Floor(lengthPx / 96.0 * dpi / modules + 1e-9);
}

