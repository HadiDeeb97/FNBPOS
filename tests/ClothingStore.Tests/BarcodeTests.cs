using ClothingStore.Core.Barcodes;

namespace ClothingStore.Tests;

/// <summary>
/// Barcodes on price labels. Each code is encoded, then read back from its bars by a decoder written here from the
/// standards (as a scanner would), so a wrong bar anywhere fails the test.
/// </summary>
public class BarcodeTests
{
    // ---- EAN-13 ------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("4006381333931")]
    [InlineData("5901234123457")]
    [InlineData("9780201379624")]
    [InlineData("0012345678905")]
    [InlineData("2000000001517")] // one the app made for a shop
    public void Ean13_bars_read_back_as_the_same_code(string code)
    {
        var widths = Ean13.EncodeWidths(code);

        Assert.Equal(95, widths.Sum());
        Assert.Equal(59, widths.Count); // 3 + 6×4 + 5 + 6×4 + 3 bars and spaces
        Assert.All(widths, w => Assert.InRange(w, 1, 4));
        Assert.Equal(code, DecodeEan13(widths));
    }

    [Fact]
    public void Every_first_digit_and_every_in_store_code_reads_back()
    {
        for (var first = 0; first <= 9; first++)
        {
            var payload = first + "12345678901";
            var code = payload + Ean13.ComputeCheckDigit(payload);
            Assert.Equal(code, DecodeEan13(Ean13.EncodeWidths(code)));
        }
        foreach (var sequence in new long[] { 0, 1, 151, 99_999, 1_234_567_890, 9_999_999_999 })
        {
            var code = Ean13.CreateInStore(sequence);
            Assert.StartsWith(Ean13.InStorePrefix, code);
            Assert.Equal(code, DecodeEan13(Ean13.EncodeWidths(code)));
        }
    }

    [Theory]
    [InlineData("4006381333932")] // wrong check digit
    [InlineData("400638133393")]  // 12 digits
    [InlineData("40063813339X1")]
    [InlineData("")]
    public void Only_a_valid_ean13_is_drawn_as_one(string code)
    {
        Assert.False(Ean13.IsValid(code));
        Assert.Throws<ArgumentException>(() => Ean13.EncodeWidths(code));
    }

    // ---- Code 128 ----------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("2000000001517")]   // 13 digits: subset C, then the last digit in B
    [InlineData("20000000015")]
    [InlineData("12345678")]        // all digits, even
    [InlineData("1234")]
    [InlineData("12")]
    [InlineData("7")]
    [InlineData("AB-M-A")]          // an app SKU
    [InlineData("TEE-BLK-123456")]  // letters, then a run of digits
    [InlineData("A1234567B")]       // a long run in the middle
    [InlineData("12345ABC")]        // an odd run first
    [InlineData("x 9~")]
    public void Code128_bars_read_back_as_the_same_text(string text)
    {
        var widths = Code128.EncodeWidths(text);
        Assert.All(widths, w => Assert.InRange(w, 1, 4));
        Assert.Equal(text, DecodeCode128(widths));
    }

    [Fact]
    public void Random_texts_read_back()
    {
        var rng = new Random(128);
        const string chars = "0123456789-ABCDEFabcdef /.";
        for (var n = 0; n < 2000; n++)
        {
            var text = new string(Enumerable.Range(0, rng.Next(1, 25)).Select(_ => chars[rng.Next(chars.Length)]).ToArray());
            if (rng.Next(3) == 0) text = string.Concat(Enumerable.Range(0, rng.Next(1, 20)).Select(_ => (char)('0' + rng.Next(10))));
            Assert.Equal(text, DecodeCode128(Code128.EncodeWidths(text)));
        }
    }

    [Fact]
    public void Digits_are_packed_two_per_symbol_so_the_code_is_short()
    {
        // 13 digits: start, 6 pairs, a switch, the last digit, checksum, stop = 10 symbols of 11 + a stop of 13.
        Assert.Equal(123, Code128.EncodeWidths("2000000001517").Sum());
        Assert.Equal(Code128.StartC, Code128.EncodeValues("2000000001517")[0]);
        // Plain text stays in subset B exactly as before.
        Assert.Equal(Code128.StartB, Code128.EncodeValues("PJJ123C")[0]);
    }

    [Fact]
    public void Characters_no_barcode_holds_are_refused() =>
        Assert.Throws<ArgumentException>(() => Code128.EncodeWidths("أسود"));

    // ---- Which barcode a label gets, and how wide its bars print -----------------------------------------------

    [Fact]
    public void Ean13_setting_prints_ean13_codes_as_ean13_and_anything_else_as_code128()
    {
        var made = Ean13.CreateInStore(151);
        Assert.Equal((BarcodeType.Ean13, 11), Pick(made, BarcodeType.Ean13));
        Assert.Equal((BarcodeType.Code128, 10), Pick("AB-M-A", BarcodeType.Ean13));             // a SKU
        Assert.Equal((BarcodeType.Code128, 10), Pick("4006381333932", BarcodeType.Ean13));      // 13 digits, wrong check digit
        Assert.Equal((BarcodeType.Code128, 10), Pick(made, BarcodeType.Code128));               // the setting says Code 128
        Assert.Null(BarcodeSymbol.For("", BarcodeType.Ean13));
        Assert.Null(BarcodeSymbol.For("أسود", BarcodeType.Code128));
        Assert.Equal(BarcodeType.Ean13, BarcodeSymbol.For(" " + made + " ", BarcodeType.Ean13)!.Type); // spaces around a typed code
    }

    [Theory]
    [InlineData(40, 203)]   // small roll labels
    [InlineData(50, 203)]
    [InlineData(58, 203)]
    [InlineData(35, 300)]
    [InlineData(63.5, 600)] // A4 sheet labels on an office printer
    public void Bars_are_whole_printer_dots_and_the_code_fits_with_its_margins(double labelMm, int dpi)
    {
        foreach (var type in new[] { BarcodeType.Ean13, BarcodeType.Code128 })
        {
            var symbol = BarcodeSymbol.For(Ean13.CreateInStore(151), type)!;
            var widthInches = (labelMm - 3) / 25.4; // a little padding either side
            var dots = symbol.ModuleDots(widthInches, dpi);

            Assert.InRange(dots, 1, 6);
            Assert.True(dots * symbol.TotalModules <= widthInches * dpi, $"{type} on {labelMm} mm doesn't fit");
            // As EAN-13 the app's codes print with bars at least 0.25 mm wide on every common label size, easy for any
            // scanner. Code 128 is longer: as wide only from 40 mm labels up (on the 35 mm tag its bars are about 0.17 mm).
            var minimum = type == BarcodeType.Ean13 || labelMm >= 40 ? 0.25 : 0.16;
            Assert.True(BarcodeSymbol.ModuleMm(dots, dpi) >= minimum, $"{type} on {labelMm} mm at {dpi} dpi: {BarcodeSymbol.ModuleMm(dots, dpi):0.###} mm");
        }
    }

    [Fact]
    public void A_tiny_label_still_gets_one_dot_bars()
    {
        var symbol = BarcodeSymbol.For(Ean13.CreateInStore(1), BarcodeType.Ean13)!;
        Assert.Equal(1, symbol.ModuleDots(0.2, 203));
        Assert.Equal(6, symbol.ModuleDots(20, 600)); // a huge label doesn't get needlessly fat bars
    }

    private static (BarcodeType, int) Pick(string code, BarcodeType type) =>
        BarcodeSymbol.For(code, type) is { } s ? (s.Type, s.QuietModules) : default;

    // ---- Decoders, from the standards ----------------------------------------------------------------------------

    private static readonly string[] L = ["0001101", "0011001", "0010011", "0111101", "0100011", "0110001", "0101111", "0111011", "0110111", "0001011"];
    private static readonly string[] R = ["1110010", "1100110", "1101100", "1000010", "1011100", "1001110", "1010000", "1000100", "1001000", "1110100"];
    private static readonly string[] FirstDigitParity = ["LLLLLL", "LLGLGG", "LLGGLG", "LLGGGL", "LGLLGG", "LGGLLG", "LGGGLL", "LGLGLG", "LGLGGL", "LGGLGL"];

    /// <summary>Reads an EAN-13 from its bar widths: guards, six left digits (odd or even parity), six right digits, check digit.</summary>
    private static string DecodeEan13(IReadOnlyList<int> widths)
    {
        var bits = string.Concat(widths.Select((w, i) => new string(i % 2 == 0 ? '1' : '0', w)));
        Assert.Equal(95, bits.Length);
        Assert.Equal("101", bits[..3]);
        Assert.Equal("01010", bits.Substring(45, 5));
        Assert.Equal("101", bits[92..]);

        var digits = new List<char>();
        var parity = "";
        for (var i = 0; i < 6; i++)
        {
            var chunk = bits.Substring(3 + i * 7, 7);
            var odd = Array.IndexOf(L, chunk);
            // Even parity (G) is the right-hand pattern read backwards.
            var even = Array.IndexOf(R, new string(chunk.Reverse().ToArray()));
            Assert.True(odd >= 0 || even >= 0, $"left digit {i + 1} unreadable: {chunk}");
            digits.Add((char)('0' + (odd >= 0 ? odd : even)));
            parity += odd >= 0 ? 'L' : 'G';
        }
        for (var i = 0; i < 6; i++)
        {
            var chunk = bits.Substring(50 + i * 7, 7);
            var digit = Array.IndexOf(R, chunk);
            Assert.True(digit >= 0, $"right digit {i + 1} unreadable: {chunk}");
            digits.Add((char)('0' + digit));
        }
        var first = Array.IndexOf(FirstDigitParity, parity);
        Assert.True(first >= 0, "parity pattern " + parity);
        var code = (char)('0' + first) + new string(digits.ToArray());
        Assert.True(Ean13.IsValid(code), "check digit");
        return code;
    }

    /// <summary>Reads Code 128 from its bar widths: start symbol, subsets B and C with switches, checksum, stop.</summary>
    private static string DecodeCode128(IReadOnlyList<int> widths)
    {
        var symbols = new List<int>();
        var i = 0;
        while (i < widths.Count)
        {
            var size = widths.Count - i == 7 ? 7 : 6; // the stop symbol has 7 elements
            var pattern = string.Concat(widths.Skip(i).Take(size));
            var value = Array.IndexOf(Code128.Patterns, pattern);
            Assert.True(value >= 0, $"unknown symbol {pattern}");
            Assert.Equal(11, widths.Skip(i).Take(6).Sum());
            symbols.Add(value);
            i += size;
        }
        Assert.Equal(Code128.Stop, symbols[^1]);
        var checksum = symbols[0];
        for (var k = 1; k < symbols.Count - 2; k++) checksum += symbols[k] * k;
        Assert.Equal(checksum % 103, symbols[^2]);

        var set = symbols[0] switch { 104 => 'B', 105 => 'C', _ => throw new Xunit.Sdk.XunitException("start " + symbols[0]) };
        var text = new System.Text.StringBuilder();
        foreach (var v in symbols.Skip(1).Take(symbols.Count - 3))
        {
            if (set == 'C' && v < 100) text.Append(v.ToString("00"));
            else if (set == 'B' && v < 96) text.Append((char)(v + 32));
            else if (v == 99) set = 'C';
            else if (v == 100) set = 'B';
            else throw new Xunit.Sdk.XunitException($"unexpected symbol {v} in subset {set}");
        }
        return text.ToString();
    }
}

/// <summary>Scanning a label at the till or in stock screens.</summary>
public class BarcodeScanTests
{
    [Fact]
    public async Task A_scanned_code_finds_its_item_and_a_upc_is_found_in_either_form()
    {
        await using var db = await TestDatabase.CreateAsync();
        var category = await db.Categories.SaveAsync(new ClothingStore.Core.Entities.Category { Name = "Tops" });
        var product = await db.Products.SaveAsync(new ClothingStore.Core.Entities.Product
        {
            Name = "Scan Tee", CategoryId = category.Id, Price = 10,
            Variants =
            [
                new ClothingStore.Core.Entities.ProductVariant { Size = "S" },                                // the app makes its EAN-13
                new ClothingStore.Core.Entities.ProductVariant { Size = "M", Barcode = "012345678905" },      // a supplier's UPC
                new ClothingStore.Core.Entities.ProductVariant { Size = "L", Barcode = "4006381333931" },     // a supplier's EAN
            ],
        }, db.Admin.Id);
        var small = product.Variants.Single(v => v.Size == "S");
        Assert.True(Ean13.IsValid(small.Barcode));

        Assert.Equal(small.Id, (await db.Products.FindByCodeAsync(small.Barcode!))!.Id);
        Assert.Equal(small.Id, (await db.Products.FindByCodeAsync(" " + small.Barcode + "\r\n"))!.Id); // scanner's Enter
        Assert.Equal(small.Id, (await db.Products.FindByCodeAsync(small.Sku))!.Id);
        Assert.Equal("M", (await db.Products.FindByCodeAsync("012345678905"))!.Size);
        Assert.Equal("M", (await db.Products.FindByCodeAsync("0012345678905"))!.Size); // the UPC sent as EAN-13
        Assert.Equal("L", (await db.Products.FindByCodeAsync("4006381333931"))!.Size);
        Assert.Null(await db.Products.FindByCodeAsync("4006381333932"));
        Assert.Null(await db.Products.FindByCodeAsync(""));
    }

    [Fact]
    public async Task New_items_get_unique_valid_ean13_codes_that_print_as_ean13()
    {
        await using var db = await TestDatabase.CreateAsync();
        var category = await db.Categories.SaveAsync(new ClothingStore.Core.Entities.Category { Name = "Tops" });
        var codes = new List<string>();
        for (var p = 0; p < 3; p++)
        {
            var product = await db.Products.SaveAsync(new ClothingStore.Core.Entities.Product
            {
                Name = "Batch " + p, CategoryId = category.Id, Price = 10,
                Variants = [.. new[] { "S", "M", "L", "XL" }.Select(s => new ClothingStore.Core.Entities.ProductVariant { Size = s })],
            }, db.Admin.Id);
            codes.AddRange(product.Variants.Select(v => v.Barcode!));
        }

        Assert.Equal(12, codes.Distinct().Count());
        Assert.All(codes, c =>
        {
            Assert.True(Ean13.IsValid(c), c);
            Assert.StartsWith(Ean13.InStorePrefix, c);
            Assert.Equal(BarcodeType.Ean13, BarcodeSymbol.For(c, BarcodeType.Ean13)!.Type);
        });
    }
}
