using System.Text.RegularExpressions;
using ClothingStore.Core;
using ClothingStore.Core.Localization;

namespace ClothingStore.Tests;

/// <summary>
/// Guards the translations and the XAML, which can't be run on the build machine: every key used in code exists
/// in English and Arabic with the same placeholders, every resource a view references is defined, and no screen
/// shows hard-coded English.
/// </summary>
public partial class LocalizationTests
{
    private static readonly string Root = FindRoot();
    private static readonly string[] SourceFiles = Directory.GetFiles(Path.Combine(Root, "src"), "*.*", SearchOption.AllDirectories)
        .Where(f => (f.EndsWith(".cs") || f.EndsWith(".xaml")) && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                    && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}") && !f.Contains("Migrations"))
        .ToArray();

    private static string FindRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "ClothingStorePOS.sln"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Solution folder not found.");
    }

    [Fact]
    public void English_and_Arabic_have_the_same_keys()
    {
        var en = Loc.Table(Loc.English);
        var ar = Loc.Table(Loc.Arabic);
        Assert.NotEmpty(en);
        Assert.Empty(en.Keys.Except(ar.Keys).Order());
        Assert.Empty(ar.Keys.Except(en.Keys).Order());
        Assert.Empty(ar.Where(kv => string.IsNullOrWhiteSpace(kv.Value)).Select(kv => kv.Key));
    }

    [Fact]
    public void Translations_keep_the_same_placeholders()
    {
        var en = Loc.Table(Loc.English);
        var ar = Loc.Table(Loc.Arabic);
        var mismatched = en.Keys
            .Where(k => !Placeholders(en[k]).SetEquals(Placeholders(ar[k])))
            .Select(k => $"{k}: '{en[k]}' / '{ar[k]}'")
            .ToList();
        Assert.Empty(mismatched);
    }

    [Fact]
    public void Every_key_used_in_code_exists()
    {
        var en = Loc.Table(Loc.English);
        var missing = new SortedSet<string>();
        foreach (var file in SourceFiles)
        {
            var text = File.ReadAllText(file);
            var regex = file.EndsWith(".xaml") ? XamlKey() : CodeKey();
            foreach (Match m in regex.Matches(text))
                if (!en.ContainsKey(m.Groups["key"].Value))
                    missing.Add($"{Path.GetFileName(file)}: {m.Groups["key"].Value}");
        }
        Assert.Empty(missing);
    }

    [Fact]
    public void Line_breaks_are_real_line_breaks_not_backslash_n()
    {
        var broken = Loc.Table(Loc.English).Concat(Loc.Table(Loc.Arabic))
            .Where(kv => kv.Value.Contains("\\n"))
            .Select(kv => kv.Key)
            .ToList();
        Assert.Empty(broken);
    }

    [Fact]
    public void Every_displayed_enum_value_is_translated()
    {
        Type[] enums =
        [
            typeof(PaymentMethod), typeof(RefundMethod), typeof(RefundDestination), typeof(UserRole), typeof(Gender),
            typeof(SaleStatus), typeof(StockMovementType), typeof(PurchaseOrderStatus), typeof(CashMovementType),
            typeof(DiscountType), typeof(BackupKind), typeof(ShiftStatus), typeof(CashCurrency), typeof(ChangeCurrency), typeof(SalesChannel), typeof(SettlementMethod),
        ];
        var en = Loc.Table(Loc.English);
        var missing = enums
            .SelectMany(t => Enum.GetNames(t).Select(n => $"Enum.{t.Name}.{n}"))
            .Where(k => !en.ContainsKey(k))
            .ToList();
        Assert.Empty(missing);
    }

    [Fact]
    public void Every_resource_a_view_uses_is_defined()
    {
        var xaml = SourceFiles.Where(f => f.EndsWith(".xaml")).ToDictionary(f => f, File.ReadAllText);
        var defined = xaml.Values.SelectMany(t => DefinedKey().Matches(t).Select(m => m.Groups["key"].Value)).ToHashSet();
        var missing = xaml
            .SelectMany(f => UsedResource().Matches(f.Value).Select(m => (File: Path.GetFileName(f.Key), Key: m.Groups["key"].Value)))
            .Where(u => !defined.Contains(u.Key))
            .Select(u => $"{u.File}: {u.Key}")
            .Distinct()
            .ToList();
        Assert.Empty(missing);
    }

    [Fact]
    public void Views_contain_no_hard_coded_text()
    {
        var offenders = SourceFiles
            .Where(f => f.EndsWith(".xaml"))
            .SelectMany(f =>
            {
                var text = File.ReadAllText(f);
                return LiteralText().Matches(text).Concat(ElementText().Matches(text))
                    .Select(m => m.Groups["value"].Value)
                    .Where(v => GlyphEntity().Replace(v, "").Any(char.IsLetter) && !KeyName().IsMatch(v.Trim()))
                    .Select(v => $"{Path.GetFileName(f)}: \"{v.Trim()}\"");
            })
            .ToList();
        Assert.Empty(offenders);
    }

    private static HashSet<string> Placeholders(string text) =>
        Placeholder().Matches(text).Select(m => m.Value).ToHashSet();

    [GeneratedRegex(@"\{\d+(:[^}]*)?\}")]
    private static partial Regex Placeholder();

    [GeneratedRegex(@"Loc\.(?:T|Get|Format)\((?:[A-Za-z_.]+,\s*)?""(?<key>[A-Za-z0-9_.]+)""")]
    private static partial Regex CodeKey();

    [GeneratedRegex(@"\{l:T\s+(?:Key=)?(?<key>[A-Za-z0-9_.]+)")]
    private static partial Regex XamlKey();

    [GeneratedRegex(@"x:Key=""(?<key>[^""]+)""")]
    private static partial Regex DefinedKey();

    [GeneratedRegex(@"\{(?:StaticResource|DynamicResource)\s+(?<key>[A-Za-z][A-Za-z0-9_.]*)\}")]
    private static partial Regex UsedResource();

    [GeneratedRegex(@"\s(?:Text|Content|Tag|Header|ToolTip|Title|Watermark)=""(?<value>[^""{][^""]*)""")]
    private static partial Regex LiteralText();

    [GeneratedRegex(@">(?<value>[^<>{]*[^\s<>{][^<>]*)</(?:Run|TextBlock|Button|Label|CheckBox|RadioButton)>")]
    private static partial Regex ElementText();

    /// <summary>Keyboard key names ("F12") read the same in every language.</summary>
    [GeneratedRegex(@"^F\d{1,2}$")]
    private static partial Regex KeyName();

    [GeneratedRegex(@"&#x?[0-9A-Fa-f]+;")]
    private static partial Regex GlyphEntity();
}
