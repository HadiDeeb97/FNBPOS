namespace ClothingStore.Core.Barcodes;

/// <summary>
/// Code 128 encoder (subsets B and C). Produces alternating bar/space module widths that any
/// renderer (WPF, PDF, thermal printer) can draw. Used for price tags / shelf labels.
/// </summary>
public static class Code128
{
    public const int StartB = 104;
    public const int Stop = 106;

    internal static readonly string[] Patterns =
    [
        "212222", "222122", "222221", "121223", "121322", "131222", "122213", "122312", "132212", "221213",
        "221312", "231212", "112232", "122132", "122231", "113222", "123122", "123221", "223211", "221132",
        "221231", "213212", "223112", "312131", "311222", "321122", "321221", "312212", "322112", "322211",
        "212123", "212321", "232121", "111323", "131123", "131321", "112313", "132113", "132311", "211313",
        "231113", "231311", "112133", "112331", "132131", "113123", "113321", "133121", "313121", "211331",
        "231131", "213113", "213311", "213131", "311123", "311321", "331121", "312113", "312311", "332111",
        "314111", "221411", "431111", "111224", "111422", "121124", "121421", "141122", "141221", "112214",
        "112412", "122114", "122411", "142112", "142211", "241211", "221114", "413111", "241112", "134111",
        "111242", "121142", "121241", "114212", "124112", "124211", "411212", "421112", "421211", "212141",
        "214121", "412121", "111143", "111341", "131141", "114113", "114311", "411113", "411311", "113141",
        "114131", "311141", "411131", "211412", "211214", "211232", "2331112",
    ];

    public const int StartC = 105;
    private const int SwitchToB = 100;
    private const int SwitchToC = 99;

    /// <summary>
    /// Symbol values (start, data, checksum, stop) for the given text. Runs of digits are packed two per symbol
    /// (subset C), so a 13-digit barcode is about a third narrower than in subset B and its bars can be printed wider,
    /// which is what makes it easy to scan; everything else is subset B.
    /// </summary>
    public static IReadOnlyList<int> EncodeValues(string text)
    {
        ArgumentException.ThrowIfNullOrEmpty(text);
        foreach (var c in text)
            if (c < 32 || c > 127)
                throw new ArgumentException($"Character '{c}' cannot be encoded in Code 128.", nameof(text));

        var values = new List<int>(text.Length + 4);
        var i = 0;
        var inC = DigitRun(text, 0) >= 4 || (DigitRun(text, 0) == text.Length && text.Length is 2);
        values.Add(inC ? StartC : StartB);
        while (i < text.Length)
        {
            var run = DigitRun(text, i);
            if (inC)
            {
                if (run >= 2)
                {
                    values.Add((text[i] - '0') * 10 + (text[i + 1] - '0'));
                    i += 2;
                    continue;
                }
                values.Add(SwitchToB);
                inC = false;
                continue;
            }
            // Worth switching to C for a run of 4+ digits (6+ in the middle of text), taking an even number of them.
            if (run >= 4 && (i + run == text.Length || run >= 6))
            {
                if (run % 2 == 1)
                {
                    values.Add(text[i] - 32); // the odd digit goes in subset B first
                    i++;
                }
                values.Add(SwitchToC);
                inC = true;
                continue;
            }
            values.Add(text[i] - 32);
            i++;
        }

        var checksum = values[0];
        for (var k = 1; k < values.Count; k++) checksum += values[k] * k;
        values.Add(checksum % 103);
        values.Add(Stop);
        return values;
    }

    private static int DigitRun(string text, int from)
    {
        var n = 0;
        while (from + n < text.Length && char.IsAsciiDigit(text[from + n])) n++;
        return n;
    }

    /// <summary>
    /// Module widths, alternating bar, space, bar, ... starting with a bar.
    /// Does not include the quiet zone (renderers should leave 10 modules each side).
    /// </summary>
    public static IReadOnlyList<int> EncodeWidths(string text) =>
        EncodeValues(text).SelectMany(v => Patterns[v].Select(ch => ch - '0')).ToList();
}
