namespace ClothingStore.Core.Barcodes;

/// <summary>The kind of barcode printed on labels (Settings → Receipts and labels).</summary>
public enum BarcodeType
{
    /// <summary>EAN-13 for 13-digit codes (what the app generates); anything else still prints as Code 128.</summary>
    Ean13 = 0,

    /// <summary>Code 128 for every code.</summary>
    Code128 = 1,
}

/// <summary>
/// A barcode ready to draw: its bar and space widths in modules (alternating, starting with a bar) and the empty
/// margin it needs on each side. <see cref="ModuleDots"/> picks a bar width that is a whole number of printer dots, so
/// every bar prints exactly as wide as it should; stretched to fit, bars come out uneven and scanners struggle.
/// </summary>
public sealed record BarcodeSymbol(string Code, IReadOnlyList<int> Widths, int QuietModules, BarcodeType Type)
{
    /// <summary>Width of the whole code in modules, margins included.</summary>
    public int TotalModules => Widths.Sum() + QuietModules * 2;

    /// <summary>The code to print for <paramref name="code"/>; null when it can't be drawn (empty, or characters no barcode holds).</summary>
    public static BarcodeSymbol? For(string? code, BarcodeType type)
    {
        if (string.IsNullOrWhiteSpace(code)) return null;
        code = code.Trim();
        if (type == BarcodeType.Ean13 && Ean13.IsValid(code)) return new(code, Ean13.EncodeWidths(code), 11, BarcodeType.Ean13);
        try
        {
            return new(code, Code128.EncodeWidths(code), 10, BarcodeType.Code128);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    /// <summary>
    /// Printer dots per module so the whole code, margins included, fits <paramref name="widthInches"/>: a whole number,
    /// at least 1. Above <paramref name="maxDots"/> the bars would only get needlessly wide.
    /// </summary>
    public int ModuleDots(double widthInches, double dpi, int maxDots = 6) =>
        Math.Clamp((int)Math.Floor(widthInches * dpi / TotalModules + 1e-9), 1, maxDots);

    /// <summary>
    /// The bars, each at its place in modules from the first one. EAN-13's guard bars (the two thin bars at the start, in
    /// the middle and at the end) are marked: as on products, they are drawn longer, down between the digit groups.
    /// </summary>
    public IReadOnlyList<Bar> Bars()
    {
        var bars = new List<Bar>();
        var x = 0;
        for (var i = 0; i < Widths.Count; i++)
        {
            if (i % 2 == 0) bars.Add(new Bar(x, Widths[i], Type == BarcodeType.Ean13 && IsEanGuard(x)));
            x += Widths[i];
        }
        return bars;
    }

    private static bool IsEanGuard(int module) => module is < 3 or (>= 45 and < 50) or >= 92;

    /// <summary>
    /// EAN-13's digits as printed on products, "0 123456 789012": the first left of the bars (in the quiet zone), then
    /// six under each half, each centred in its 7-module digit. Centres are in modules from the first bar. Empty for
    /// Code 128, whose text is simply centred under the bars.
    /// </summary>
    public IReadOnlyList<(char Digit, double Center)> ShopDigits()
    {
        if (Type != BarcodeType.Ean13) return [];
        var digits = new List<(char, double)> { (Code[0], -6) };
        for (var i = 0; i < 6; i++) digits.Add((Code[1 + i], 3 + i * 7 + 3.5));
        for (var i = 0; i < 6; i++) digits.Add((Code[7 + i], 50 + i * 7 + 3.5));
        return digits;
    }

    /// <summary>Narrowest bar in millimetres at <paramref name="dots"/> dots per module (EAN-13 scans best from about 0.26 mm).</summary>
    public static double ModuleMm(int dots, double dpi) => dots * 25.4 / dpi;
}

/// <summary>One bar: where it starts and how wide it is, in modules; <see cref="Guard"/> bars are drawn longer.</summary>
public readonly record struct Bar(int X, int Width, bool Guard);
