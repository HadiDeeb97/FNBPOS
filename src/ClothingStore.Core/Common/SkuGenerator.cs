using System.Text;

namespace ClothingStore.Core;

/// <summary>Builds readable SKUs such as <c>OXF-SHIRT-M-BLUE</c> from style, size and colour.</summary>
public static class SkuGenerator
{
    public static string Build(string styleCode, string? size, string? color)
    {
        var parts = new[] { Normalize(styleCode), Normalize(size), ColorCode(color) }
            .Where(p => p.Length > 0);
        var sku = string.Join("-", parts);
        return sku.Length == 0 ? "ITEM" : sku;
    }

    /// <summary>Derives a style code from a product name: "Slim Fit Oxford Shirt" -> "SFOS".</summary>
    public static string StyleCodeFromName(string name)
    {
        var words = name.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(Normalize)
            .Where(w => w.Length > 0)
            .ToArray();
        return words.Length switch
        {
            0 => "ITEM",
            1 => words[0].Length <= 6 ? words[0] : words[0][..6],
            _ => string.Concat(words.Take(5).Select(w => w[0])),
        };
    }

    /// <summary>Appends -2, -3, ... until the SKU is not in <paramref name="taken"/>.</summary>
    public static string MakeUnique(string sku, ISet<string> taken)
    {
        var candidate = sku;
        for (var n = 2; taken.Contains(candidate); n++) candidate = $"{sku}-{n}";
        return candidate;
    }

    private static string ColorCode(string? color)
    {
        var c = Normalize(color);
        return c.Length <= 4 ? c : c[..3];
    }

    internal static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "";
        var sb = new StringBuilder(value.Length);
        foreach (var ch in value.ToUpperInvariant())
            if (char.IsAsciiLetterOrDigit(ch)) sb.Append(ch);
        return sb.ToString();
    }
}
