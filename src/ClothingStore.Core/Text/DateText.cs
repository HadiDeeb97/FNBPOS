using System.Globalization;
using System.Text.RegularExpressions;

namespace ClothingStore.Core.Text;

/// <summary>
/// Dates and times written the way this PC writes them (Windows' region settings: "26/10/2026 14:05", "10/26/2026 2:05 PM"...),
/// so the app reads like the clock on the taskbar. Files and the database keep their own fixed formats.
/// </summary>
public static partial class DateText
{
    /// <summary>The PC's short date: "26/10/2026".</summary>
    public static string Date(DateTime value) => value.ToString("d", CultureInfo.CurrentCulture);

    /// <summary>The PC's short time: "14:05" or "2:05 PM".</summary>
    public static string Time(DateTime value) => value.ToString("t", CultureInfo.CurrentCulture);

    /// <summary>Short date and time: "26/10/2026 14:05".</summary>
    public static string DateTime(DateTime value) => value.ToString("g", CultureInfo.CurrentCulture);

    /// <summary>The PC's long date, for a heading: "Monday, 26 October 2026".</summary>
    public static string LongDate(DateTime value) => value.ToString("D", CultureInfo.CurrentCulture);

    /// <summary>The short date without the year, in the PC's order: "26/10" or "10/26".</summary>
    public static string DayMonth(DateTime value) => value.ToString(DayMonthPattern(CultureInfo.CurrentCulture.DateTimeFormat.ShortDatePattern), CultureInfo.CurrentCulture);

    /// <summary>The day's short name before the date or time: "Mon 26/10/2026".</summary>
    public static string WithDay(DateTime value, string text) => value.ToString("ddd", CultureInfo.CurrentCulture) + " " + text;

    /// <summary>"dd/MM/yyyy" → "dd/MM", "yyyy-MM-dd" → "MM-dd", "M/d/yyyy" → "M/d".</summary>
    public static string DayMonthPattern(string shortDatePattern)
    {
        var pattern = YearPart().Replace(shortDatePattern, "").Trim();
        return pattern.Length == 0 ? "M" : pattern;
    }

    [GeneratedRegex(@"^y+[^dMy]*|[^dMy]*y+")]
    private static partial Regex YearPart();
}
