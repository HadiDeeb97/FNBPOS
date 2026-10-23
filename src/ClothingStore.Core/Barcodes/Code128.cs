namespace ClothingStore.Core.Barcodes;

/// <summary>
/// Code 128 (subset B) encoder. Produces alternating bar/space module widths that any
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

    /// <summary>Symbol values (start, data, checksum, stop) for the given text.</summary>
    public static IReadOnlyList<int> EncodeValues(string text)
    {
        ArgumentException.ThrowIfNullOrEmpty(text);
        var values = new List<int>(text.Length + 3) { StartB };
        var checksum = StartB;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c < 32 || c > 127)
                throw new ArgumentException($"Character '{c}' cannot be encoded in Code 128-B.", nameof(text));
            var value = c - 32;
            values.Add(value);
            checksum += value * (i + 1);
        }
        values.Add(checksum % 103);
        values.Add(Stop);
        return values;
    }

    /// <summary>
    /// Module widths, alternating bar, space, bar, ... starting with a bar.
    /// Does not include the quiet zone (renderers should leave 10 modules each side).
    /// </summary>
    public static IReadOnlyList<int> EncodeWidths(string text) =>
        EncodeValues(text).SelectMany(v => Patterns[v].Select(ch => ch - '0')).ToList();
}
