using ClothingStore.Core.Barcodes;
using ClothingStore.Desktop.Services;

namespace ClothingStore.Tests;

/// <summary>Label styles and how a label is laid out: the barcode always prints readably, whatever the look.</summary>
public class LabelStyleTests
{
    private const double LabelPrinterDpi = 203;

    public static TheoryData<string, string> EveryStyleOnEverySize()
    {
        var data = new TheoryData<string, string>();
        foreach (var style in LabelStyle.All.Where(s => s.Id != LabelStyle.Custom))
            foreach (var preset in LabelPreset.All.Where(p => p.Id != "Custom"))
                data.Add(style.Id, preset.Id);
        return data;
    }

    [Theory]
    [MemberData(nameof(EveryStyleOnEverySize))]
    public void Every_style_on_every_size_keeps_text_and_barcode_on_the_label_and_the_barcode_readable(string styleId, string presetId)
    {
        foreach (var arrangement in Enum.GetValues<LabelArrangement>())
        foreach (var type in new[] { BarcodeType.Ean13, BarcodeType.Code128 })
        {
            var o = new LabelOptions();
            LabelPreset.All.Single(p => p.Id == presetId).ApplyTo(o);
            LabelStyle.All.Single(s => s.Id == styleId).ApplyTo(o);
            o.Arrangement = arrangement;
            var symbol = BarcodeSymbol.For(Ean13.CreateInStore(151), type)!;

            var at = LabelGeometry.For(o, o.WidthPx, o.HeightPx, symbol.TotalModules, LabelPrinterDpi);
            var where = $"{styleId} on {presetId}, {arrangement}, {type}";

            Assert.NotNull(at.Barcode);
            var bars = at.Barcode!.Value;
            Inside(at.Text, o, where + " text");
            Inside(bars, o, where + " barcode");
            Assert.False(Overlap(at.Text, bars), where + ": text and barcode overlap");

            // Bars at least as readable as across the whole label, and readable (2 dots) whenever the label allows it.
            var best = LabelGeometry.Dots(o.WidthPx - at.Pad * 2, symbol.TotalModules, LabelPrinterDpi);
            var dots = LabelGeometry.Dots(at.BarcodeLength, symbol.TotalModules, LabelPrinterDpi);
            Assert.True(dots >= Math.Min(LabelGeometry.ReadableDots, best), $"{where}: {dots} dot bars, {best} possible");
            // Tall enough to scan: 9 mm with the digits, or most of a label too short for that.
            var thickness = at.Arrangement == LabelArrangement.Upright ? bars.Width : bars.Height;
            Assert.True(thickness >= Math.Min(LabelOptions.MmToPx(9) - 0.01, (o.HeightPx - at.Pad * 2) * 0.8), $"{where}: barcode only {thickness:0.#} px tall");
        }
    }

    private static void Inside(LabelBox box, LabelOptions o, string what)
    {
        Assert.True(box.X >= -0.01 && box.Y >= -0.01, what + " starts outside the label");
        Assert.True(box.Right <= o.WidthPx + 0.01 && box.Bottom <= o.HeightPx + 0.01, what + " ends outside the label");
        Assert.True(box.Width >= 0 && box.Height >= 0, what + " has no size");
    }

    private static bool Overlap(LabelBox a, LabelBox b) =>
        a.Width > 0 && a.Height > 0 && a.X < b.Right - 0.01 && b.X < a.Right - 0.01 && a.Y < b.Bottom - 0.01 && b.Y < a.Bottom - 0.01;

    [Fact]
    public void The_apps_codes_print_readably_as_ean13_on_every_label_size_in_every_style()
    {
        var symbol = BarcodeSymbol.For(Ean13.CreateInStore(151), BarcodeType.Ean13)!;
        foreach (var style in LabelStyle.All)
        foreach (var preset in LabelPreset.All.Where(p => p.Id != "Custom"))
        {
            var o = new LabelOptions();
            preset.ApplyTo(o);
            style.ApplyTo(o);
            var at = LabelGeometry.For(o, o.WidthPx, o.HeightPx, symbol.TotalModules, LabelPrinterDpi);
            Assert.True(LabelGeometry.Dots(at.BarcodeLength, symbol.TotalModules, LabelPrinterDpi) >= LabelGeometry.ReadableDots, $"{style.Id} on {preset.Id}");
        }
    }

    [Fact]
    public void Upright_is_used_on_a_tall_tag_and_falls_back_below_the_text_when_it_would_be_too_short()
    {
        var symbol = BarcodeSymbol.For(Ean13.CreateInStore(1), BarcodeType.Ean13)!;
        var tag = new LabelOptions { Sheet = false, WidthMm = 35, HeightMm = 60, Arrangement = LabelArrangement.Upright };
        Assert.Equal(LabelArrangement.Upright, LabelGeometry.For(tag, tag.WidthPx, tag.HeightPx, symbol.TotalModules, LabelPrinterDpi).Arrangement);

        var flat = new LabelOptions { Sheet = false, WidthMm = 50, HeightMm = 20, Arrangement = LabelArrangement.Upright };
        Assert.Equal(LabelArrangement.Stacked, LabelGeometry.For(flat, flat.WidthPx, flat.HeightPx, symbol.TotalModules, LabelPrinterDpi).Arrangement);
    }

    [Fact]
    public void A_label_without_a_barcode_is_all_text_and_the_hole_space_is_kept_clear()
    {
        var o = new LabelOptions { WidthMm = 35, HeightMm = 60, HoleSpace = true };
        var at = LabelGeometry.For(o, o.WidthPx, o.HeightPx, 0, LabelPrinterDpi);
        Assert.Null(at.Barcode);
        Assert.True(at.Text.Y >= LabelOptions.MmToPx(7), "text starts below the hole");
    }

    [Fact]
    public void Each_style_applies_its_look_and_a_change_makes_it_custom()
    {
        foreach (var style in LabelStyle.All.Where(s => s.Id != LabelStyle.Custom))
        {
            var o = new LabelOptions { ShowSku = true };
            style.ApplyTo(o);
            Assert.Equal(style.Id, o.Style);
            Assert.True(style.Matches(o), style.Id);
            Assert.True(o.ShowSku); // what the style doesn't decide stays as it was

            o.Align = o.Align == LabelAlign.Left ? LabelAlign.Right : LabelAlign.Left;
            Assert.False(style.Matches(o), style.Id);
        }
        var custom = new LabelOptions { Align = LabelAlign.Right };
        LabelStyle.All.Single(s => s.Id == LabelStyle.Custom).ApplyTo(custom);
        Assert.Equal((LabelStyle.Custom, LabelAlign.Right), (custom.Style, custom.Align));
    }

    [Fact]
    public void Options_saved_before_the_styles_keep_their_hidden_digits_and_border()
    {
        var old = new LabelOptions { ShowBarcodeText = false, ShowBorder = true };
        old.Upgrade();
        Assert.Equal((LabelDigits.Hidden, LabelBorder.Rounded), (old.Digits, old.Border));

        var upgraded = new LabelOptions { Version = 2, Digits = LabelDigits.Line, ShowBarcodeText = false };
        upgraded.Upgrade();
        Assert.Equal(LabelDigits.Line, upgraded.Digits); // read once only

        var fresh = new LabelOptions().Upgrade();
        Assert.Equal((LabelDigits.Shop, LabelBorder.None, "Classic"), (fresh.Digits, fresh.Border, fresh.Style));
    }

    [Theory]
    [InlineData("M", "Black", LabelVariantFormat.Slash, "M / Black")]
    [InlineData("M", "Black", LabelVariantFormat.Labelled, "Size M · Black")]
    [InlineData("", "Black", LabelVariantFormat.Labelled, "Black")]
    [InlineData("XL", null, LabelVariantFormat.Labelled, "Size XL")]
    [InlineData(null, null, LabelVariantFormat.Slash, "")]
    public void Size_and_colour_line(string? size, string? color, LabelVariantFormat format, string expected) =>
        Assert.Equal(expected, LabelText.Variant(size, color, format));

    // ---- The barcode drawn as on products -------------------------------------------------------------------------

    [Fact]
    public void Ean13_guard_bars_are_the_start_middle_and_end_pairs()
    {
        var symbol = BarcodeSymbol.For("5901234123457", BarcodeType.Ean13)!;
        var bars = symbol.Bars();
        var guards = bars.Where(b => b.Guard).ToList();

        Assert.Equal(6, guards.Count); // two thin bars at the start, in the middle and at the end
        Assert.Equal([0, 2, 46, 48, 92, 94], guards.Select(b => b.X));
        Assert.All(guards, b => Assert.Equal(1, b.Width));
        Assert.Equal(30, bars.Count); // 3 + 6×2 + 2... every bar of the code
        Assert.Equal(symbol.Widths.Where((_, i) => i % 2 == 0), bars.Select(b => b.Width)); // the same bars, placed
    }

    [Fact]
    public void Code128_has_no_guard_bars_or_shop_digits()
    {
        var symbol = BarcodeSymbol.For("AB-M-A", BarcodeType.Code128)!;
        Assert.DoesNotContain(symbol.Bars(), b => b.Guard);
        Assert.Empty(symbol.ShopDigits());
    }

    [Fact]
    public void Shop_digits_sit_like_on_products_first_digit_outside_then_six_under_each_half()
    {
        var symbol = BarcodeSymbol.For("0012345678905", BarcodeType.Ean13)!;
        var digits = symbol.ShopDigits();

        Assert.Equal("0012345678905", new string(digits.Select(d => d.Digit).ToArray()));
        Assert.InRange(digits[0].Center - 3.5, -symbol.QuietModules, -1); // in the left quiet zone, clear of the bars
        Assert.All(digits.Skip(1).Take(6), d => Assert.InRange(d.Center, 3 + 3.5, 45 - 3.5));   // under the left half
        Assert.All(digits.Skip(7), d => Assert.InRange(d.Center, 50 + 3.5, 92 - 3.5));          // under the right half
        Assert.Equal(digits.Select(d => d.Center).Order(), digits.Select(d => d.Center));       // left to right
        Assert.Equal(7, digits[2].Center - digits[1].Center);                                   // one digit every 7 modules
    }
}
