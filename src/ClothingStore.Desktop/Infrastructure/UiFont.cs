using System.Windows;
using System.Windows.Markup;
using System.Windows.Media;

namespace ClothingStore.Desktop.Infrastructure;

/// <summary>A font in the Settings list: <see cref="Name"/> is null for the app's own font.</summary>
public sealed record FontChoice(string? Name, string Display, FontFamily Family);

/// <summary>
/// The font of this PC's screens, chosen in Settings → Screen and kept in <see cref="LocalPreferences"/>. Every window
/// takes it from the "UiFont" resource, so changing it changes every open screen at once. Arabic text uses Windows'
/// own fallback when a font has no Arabic letters, so no choice can leave text unreadable.
/// </summary>
public static class UiFont
{
    /// <summary>The app's own font (as in Theme.xaml).</summary>
    public const string Default = "Segoe UI Variable Text, Segoe UI";

    /// <summary>Clear fonts that come with Windows, shown first.</summary>
    private static readonly string[] Recommended = ["Segoe UI", "Tahoma", "Calibri", "Arial", "Verdana", "Bahnschrift", "Sakkal Majalla", "Arabic Typesetting"];

    /// <summary>The font in use: the saved choice when it is still installed, otherwise the app's own.</summary>
    public static string Current => IsInstalled(LocalPreferences.Current.UiFont) ? LocalPreferences.Current.UiFont! : Default;

    /// <summary>Puts this PC's font in place (at start-up, before the first window).</summary>
    public static void Apply()
    {
        if (Application.Current is { } app) app.Resources["UiFont"] = new FontFamily(Current);
    }

    /// <summary>Saves and applies a font; null goes back to the app's own.</summary>
    public static void Set(string? name)
    {
        LocalPreferences.Current.UiFont = string.IsNullOrWhiteSpace(name) || name == Default ? null : name;
        LocalPreferences.Current.Save();
        Apply();
    }

    /// <summary>The app's own font, the recommended ones that are installed, then every other installed font A–Z.</summary>
    public static List<FontChoice> Choices(string defaultLabel)
    {
        var installed = InstalledNames();
        var list = new List<FontChoice> { new(null, defaultLabel, new FontFamily(Default)) };
        var recommended = Recommended.Where(installed.Contains).ToList();
        list.AddRange(recommended.Select(n => new FontChoice(n, n, new FontFamily(n))));
        list.AddRange(installed.Where(n => !recommended.Contains(n)).Order(StringComparer.CurrentCultureIgnoreCase)
            .Select(n => new FontChoice(n, n, new FontFamily(n))));
        return list;
    }

    private static HashSet<string> InstalledNames()
    {
        var english = XmlLanguage.GetLanguage("en-us");
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var family in Fonts.SystemFontFamilies)
        {
            var name = family.FamilyNames.TryGetValue(english, out var n) ? n : family.Source;
            // Symbol fonts (Wingdings, the icon fonts) would turn the screens into pictures.
            if (string.IsNullOrWhiteSpace(name) || name.Contains("Wingdings") || name.Contains("Webdings") || name.Contains("Symbol")
                || name.Contains("MDL2") || name.Contains("Fluent Icons") || name.Contains("Emoji") || name.Contains("Marlett")) continue;
            names.Add(name);
        }
        return names;
    }

    private static bool IsInstalled(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        try
        {
            return InstalledNames().Contains(name);
        }
        catch (Exception)
        {
            return false;
        }
    }
}
