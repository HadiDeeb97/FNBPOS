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
    public bool ShowBarcodeText { get; set; } = true;

    /// <summary>Thin outline around each label (handy to check alignment on plain paper).</summary>
    public bool ShowBorder { get; set; }

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
