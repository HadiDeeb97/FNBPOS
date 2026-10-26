namespace ClothingStore.Core.Text;

/// <summary>
/// Colour names for entering products: the common ones a new shop starts with, a swatch for the well-known names, and
/// suggestions while typing that reuse the shop's own spelling ("black" becomes the "Black" already used).
/// </summary>
public static class ColorNames
{
    /// <summary>English, Arabic, swatch. The first ten are offered to a shop that has no colours of its own yet.</summary>
    private static readonly (string En, string Ar, string Hex)[] Known =
    [
        ("Black", "أسود", "#111827"), ("White", "أبيض", "#FFFFFF"), ("Navy", "كحلي", "#1E3A8A"), ("Grey", "رمادي", "#9CA3AF"),
        ("Beige", "بيج", "#E8D9B5"), ("Red", "أحمر", "#DC2626"), ("Blue", "أزرق", "#2563EB"), ("Green", "أخضر", "#16A34A"),
        ("Brown", "بني", "#7C4A2D"), ("Pink", "زهري", "#F472B6"),
        ("Yellow", "أصفر", "#FACC15"), ("Orange", "برتقالي", "#F97316"), ("Purple", "بنفسجي", "#7C3AED"), ("Cream", "كريمي", "#FFF6DC"),
        ("Olive", "زيتي", "#6B7A2E"), ("Maroon", "خمري", "#7F1D1D"), ("Burgundy", "عنابي", "#800020"), ("Khaki", "كاكي", "#BDB76B"),
        ("Camel", "جملي", "#C19A6B"), ("Gold", "ذهبي", "#D4AF37"), ("Silver", "فضي", "#C0C0C0"), ("Sky blue", "سماوي", "#7DD3FC"),
        ("Light blue", "أزرق فاتح", "#93C5FD"), ("Dark blue", "أزرق غامق", "#1E40AF"), ("Turquoise", "تركواز", "#2DD4BF"),
        ("Mint", "نعناعي", "#A7F3D0"), ("Lilac", "ليلكي", "#C8A2C8"), ("Mustard", "خردلي", "#D4A017"), ("Coral", "مرجاني", "#FF7F50"),
        ("Rose", "وردي", "#E11D48"), ("Off white", "أوف وايت", "#F5F5F0"), ("Gray", "رصاصي", "#9CA3AF"),
    ];

    private const int Starters = 10;

    /// <summary>The colours offered to a shop with none of its own yet, in the app's language.</summary>
    public static IReadOnlyList<string> Common(bool arabic) => Known.Take(Starters).Select(k => arabic ? k.Ar : k.En).ToList();

    /// <summary>Every colour name known here, in the app's language (for suggestions after the shop's own).</summary>
    public static IReadOnlyList<string> All(bool arabic) => Known.Select(k => arabic ? k.Ar : k.En).ToList();

    /// <summary>A swatch for a well-known colour name, in English or Arabic ("#111827" for "black" or "أسود"); null otherwise.</summary>
    public static string? Swatch(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        var key = SmartSearch.Fold(name);
        foreach (var (en, ar, hex) in Known)
            if (SmartSearch.Fold(en) == key || SmartSearch.Fold(ar) == key) return hex;
        return null;
    }

    /// <summary>The colours in a typed list: "Black, White، Navy" (commas, Arabic commas, semicolons or new lines).</summary>
    public static IReadOnlyList<string> Split(string? text) =>
        (text ?? "").Split([',', '،', ';', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .DistinctBy(SmartSearch.Fold).ToList();

    /// <summary>The spelling already in use for <paramref name="typed"/> (same letters, any case or accents); else it as typed.</summary>
    public static string Canonical(string typed, IEnumerable<string> known)
    {
        var trimmed = typed.Trim();
        var key = SmartSearch.Fold(trimmed);
        return known.FirstOrDefault(k => SmartSearch.Fold(k) == key) ?? trimmed;
    }

    /// <summary>
    /// Up to <paramref name="max"/> colours for what is being typed: those starting with it first, then those containing
    /// it, in the order given (most used first). Nothing for an empty word or one that is already a whole colour.
    /// </summary>
    public static IReadOnlyList<string> Suggest(string? typed, IEnumerable<string> known, int max = 6, IEnumerable<string>? exclude = null)
    {
        var key = SmartSearch.Fold(typed ?? "");
        if (key.Length == 0) return [];
        var skip = (exclude ?? []).Select(SmartSearch.Fold).ToHashSet();
        var candidates = known.Where(k => !skip.Contains(SmartSearch.Fold(k))).DistinctBy(SmartSearch.Fold).ToList();
        if (candidates.Any(k => SmartSearch.Fold(k) == key)) return [];
        return candidates.Where(k => SmartSearch.Fold(k).StartsWith(key, StringComparison.Ordinal))
            .Concat(candidates.Where(k => !SmartSearch.Fold(k).StartsWith(key, StringComparison.Ordinal) && SmartSearch.Fold(k).Contains(key, StringComparison.Ordinal)))
            .Take(max)
            .ToList();
    }
}
