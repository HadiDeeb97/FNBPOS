using System.Globalization;
using System.Text;

namespace ClothingStore.Core.Text;

/// <summary>
/// How every search box understands what is typed:
/// <list type="bullet">
/// <item>Several words match in any order, each anywhere ("blue oxford m" finds "Oxford shirt, Blue, M").</item>
/// <item>Arabic spelling variants match each other: أ إ آ ا, ة ه, ى ي, ؤ و, ئ ي; vowel marks and the tatweel are ignored.</item>
/// <item>Arabic-Indic digits (٠١٢…) are read as 0 1 2…</item>
/// <item>Phone numbers match however they are spaced or dashed ("70 123 456" finds 70-123456).</item>
/// <item>Upper/lower case and accents don't matter (é = e).</item>
/// </list>
/// </summary>
public static class SmartSearch
{
    private const int MaxTerms = 8;

    /// <summary>The words to look for, normalized; empty when nothing searchable was typed.</summary>
    public static IReadOnlyList<string> Terms(string? text)
    {
        var cleaned = Clean(text);
        return cleaned.Split(' ', StringSplitOptions.RemoveEmptyEntries).Distinct(StringComparer.OrdinalIgnoreCase).Take(MaxTerms).ToList();
    }

    /// <summary>
    /// The typed text as one run of digits when it looks like a phone number (6+ digits and nothing but digits, spaces,
    /// dashes, dots, brackets and a leading +); otherwise null.
    /// </summary>
    public static string? PhoneDigits(string? text)
    {
        var cleaned = Clean(text);
        if (cleaned.Length == 0 || cleaned.Any(c => !(char.IsAsciiDigit(c) || c is ' ' or '-' or '.' or '(' or ')' or '+'))) return null;
        var digits = new string(cleaned.Where(char.IsAsciiDigit).ToArray());
        return digits.Length >= 6 ? digits : null;
    }

    /// <summary>
    /// A SQL Server LIKE pattern ('%term%', escape character '\') in which Arabic letter variants match each other.
    /// </summary>
    public static string LikePattern(string term)
    {
        var sb = new StringBuilder("%");
        foreach (var c in term)
        {
            sb.Append(c switch
            {
                '\\' => "\\\\",
                '%' => "\\%",
                '_' => "\\_",
                '[' => "\\[",
                ']' => "\\]",
                'ا' or 'أ' or 'إ' or 'آ' or 'ٱ' => "[اأإآٱ]",
                'ة' or 'ه' => "[ةه]",
                'ى' or 'ي' or 'ئ' => "[ىيئ]",
                'و' or 'ؤ' => "[وؤ]",
                // The database compares accents strictly, so let plain and accented Latin vowels match. Upper and
                // lower case are both listed so it also works on a database that compares case strictly.
                'a' or 'à' or 'á' or 'â' or 'ä' or 'A' or 'À' or 'Á' or 'Â' or 'Ä' => "[aàáâäAÀÁÂÄ]",
                'e' or 'è' or 'é' or 'ê' or 'ë' or 'E' or 'È' or 'É' or 'Ê' or 'Ë' => "[eèéêëEÈÉÊË]",
                'i' or 'ì' or 'í' or 'î' or 'ï' or 'I' or 'Ì' or 'Í' or 'Î' or 'Ï' => "[iìíîïIÌÍÎÏ]",
                'o' or 'ò' or 'ó' or 'ô' or 'ö' or 'O' or 'Ò' or 'Ó' or 'Ô' or 'Ö' => "[oòóôöOÒÓÔÖ]",
                'u' or 'ù' or 'ú' or 'û' or 'ü' or 'U' or 'Ù' or 'Ú' or 'Û' or 'Ü' => "[uùúûüUÙÚÛÜ]",
                'c' or 'ç' or 'C' or 'Ç' => "[cçCÇ]",
                >= 'a' and <= 'z' => $"[{c}{char.ToUpperInvariant(c)}]",
                >= 'A' and <= 'Z' => $"[{char.ToLowerInvariant(c)}{c}]",
                // Sizes, codes and names may have been typed with Arabic digits; the search term arrives as 0-9.
                >= '0' and <= '9' => $"[{c}{(char)('٠' + (c - '0'))}{(char)('۰' + (c - '0'))}]",
                _ => c.ToString(),
            });
        }
        return sb.Append('%').ToString();
    }

    /// <summary>For lists filtered in memory: true when every term appears in at least one of the fields.</summary>
    public static bool Matches(string? text, params string?[] fields)
    {
        var terms = Terms(text);
        if (terms.Count == 0) return true;
        var folded = fields.Where(f => !string.IsNullOrEmpty(f)).Select(f => Fold(f!)).ToList();
        var phone = PhoneDigits(text);
        if (phone is not null && fields.Any(f => f is not null && new string(f.Where(char.IsAsciiDigit).ToArray()).Contains(phone, StringComparison.Ordinal)))
            return true;
        return terms.All(t => { var ft = Fold(t); return folded.Any(f => f.Contains(ft, StringComparison.Ordinal)); });
    }

    /// <summary>Lower case, accents removed, Arabic variants unified, for comparing in memory.</summary>
    public static string Fold(string value)
    {
        var decomposed = Clean(value).ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
            sb.Append(c switch
            {
                'أ' or 'إ' or 'آ' or 'ٱ' => 'ا',
                'ة' => 'ه',
                'ى' or 'ئ' => 'ي',
                'ؤ' => 'و',
                _ => c,
            });
        }
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }

    /// <summary>Trims, turns Arabic-Indic digits into 0-9 and drops vowel marks and the tatweel.</summary>
    private static string Clean(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        var sb = new StringBuilder(text.Length);
        foreach (var c in text.Trim())
        {
            if (c is >= 'ً' and <= 'ْ' or 'ٰ' or 'ـ') continue; // harakat, superscript alef, tatweel
            sb.Append(c switch
            {
                >= '٠' and <= '٩' => (char)('0' + (c - '٠')), // Arabic-Indic
                >= '۰' and <= '۹' => (char)('0' + (c - '۰')), // Persian
                ' ' or '\t' or '\r' or '\n' => ' ',
                _ => c,
            });
        }
        return sb.ToString();
    }
}
