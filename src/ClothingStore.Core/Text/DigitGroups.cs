using System.Text;

namespace ClothingStore.Core.Text;

/// <summary>Thousands grouping for a number being typed (pounds): "10000000" → "10,000,000".</summary>
public static class DigitGroups
{
    /// <summary>"1234567.5" → "1,234,567.5"; null when the text isn't a plain positive number.</summary>
    public static string? Format(string text, string group, string point)
    {
        var t = new string(text.Select(c => c is >= '٠' and <= '٩' ? (char)('0' + (c - '٠')) : c).ToArray()).Replace(group, "").Replace(" ", "");
        if (t.Length == 0) return null;
        var at = t.IndexOf(point, StringComparison.Ordinal);
        var whole = at < 0 ? t : t[..at];
        var fraction = at < 0 ? null : t[(at + point.Length)..];
        if (!whole.All(char.IsAsciiDigit) || (fraction is not null && !fraction.All(char.IsAsciiDigit))) return null;
        if (whole.Length > 1) whole = whole.TrimStart('0') is { Length: > 0 } w ? w : "0"; // "000" → "0", "0500" → "500"

        var sb = new StringBuilder();
        for (var i = 0; i < whole.Length; i++)
        {
            if (i > 0 && (whole.Length - i) % 3 == 0) sb.Append(group);
            sb.Append(whole[i]);
        }
        if (fraction is not null) sb.Append(point).Append(fraction);
        return sb.ToString();
    }

}
