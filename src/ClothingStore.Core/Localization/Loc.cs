using System.Globalization;
using System.Reflection;
using System.Text.Json;

namespace ClothingStore.Core.Localization;

/// <summary>
/// Translated text for the app, service messages and receipts. Strings live in
/// <c>Localization/Strings/{area}.{language}.json</c> (embedded). A missing translation falls back to English,
/// and a missing key shows the key itself so it is easy to spot.
/// </summary>
public static class Loc
{
    public const string English = "en";
    public const string Arabic = "ar";

    public static IReadOnlyList<string> Languages { get; } = [English, Arabic];

    private static readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> Tables = Load();

    /// <summary>The language of the user interface (one per app instance).</summary>
    public static string Language { get; private set; } = English;

    public static bool IsRightToLeft => Language == Arabic;

    public static event EventHandler? LanguageChanged;

    public static void SetLanguage(string? language)
    {
        var normalized = Normalize(language);
        if (normalized == Language) return;
        Language = normalized;
        LanguageChanged?.Invoke(null, EventArgs.Empty);
    }

    /// <summary>"ar", "ar-LB"... become "ar"; anything unknown becomes English.</summary>
    public static string Normalize(string? language) =>
        language is not null && language.StartsWith(Arabic, StringComparison.OrdinalIgnoreCase) ? Arabic : English;

    /// <summary>Native name of a language, for pickers.</summary>
    public static string DisplayName(string language) => Normalize(language) == Arabic ? "العربية" : "English";

    public static string T(string key) => Get(Language, key);

    public static string T(string key, params object?[] args) => Format(Language, key, args);

    public static string Get(string language, string key)
    {
        if (Tables.TryGetValue(Normalize(language), out var table) && table.TryGetValue(key, out var text)) return text;
        return Tables[English].TryGetValue(key, out var english) ? english : key;
    }

    public static string Format(string language, string key, params object?[] args) =>
        string.Format(CultureInfo.CurrentCulture, Get(language, key), args);

    /// <summary>Display text for an enum value (key <c>Enum.{Type}.{Value}</c>).</summary>
    public static string Enum<TEnum>(TEnum value) where TEnum : struct, System.Enum => EnumText(value);

    public static string EnumText(System.Enum value) =>
        Get(Language, $"Enum.{value.GetType().Name}.{value}") is var text && !text.StartsWith("Enum.", StringComparison.Ordinal)
            ? text
            : value.ToString();

    /// <summary>All strings of one language (used by tests).</summary>
    public static IReadOnlyDictionary<string, string> Table(string language) => Tables[Normalize(language)];

    private static IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> Load()
    {
        var assembly = typeof(Loc).Assembly;
        var tables = Languages.ToDictionary(l => l, _ => new Dictionary<string, string>(StringComparer.Ordinal));
        foreach (var name in assembly.GetManifestResourceNames().Where(n => n.EndsWith(".json", StringComparison.Ordinal)))
        {
            var language = Languages.FirstOrDefault(l => name.EndsWith($".{l}.json", StringComparison.Ordinal));
            if (language is null) continue;
            foreach (var (key, value) in Read(assembly, name))
                if (!tables[language].TryAdd(key, value))
                    throw new InvalidOperationException($"Duplicate translation key '{key}' in {name}.");
        }
        return tables.ToDictionary(t => t.Key, t => (IReadOnlyDictionary<string, string>)t.Value);
    }

    private static Dictionary<string, string> Read(Assembly assembly, string resource)
    {
        using var stream = assembly.GetManifestResourceStream(resource)!;
        return JsonSerializer.Deserialize<Dictionary<string, string>>(stream, new JsonSerializerOptions
        {
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        }) ?? [];
    }
}
