using UniPos.Core.Entities;
using UniPos.Core.Localization;

namespace UniPos.Core.Menu;

/// <summary>
/// Checks the options picked for an item against its modifier groups, and prices them. Used by the register while
/// the cashier picks, and again at checkout, so a sale is never saved with options the item doesn't offer.
/// </summary>
public static class ModifierRules
{
    /// <summary>
    /// Why <paramref name="picked"/> can't be ordered with <paramref name="product"/>, or null when it can. The product
    /// must come with its groups and their options.
    /// </summary>
    public static string? Problem(Product product, IReadOnlyCollection<int> picked)
    {
        if (picked.Count != picked.Distinct().Count()) return Loc.T("Err.OptionTwice");

        var groups = ActiveGroups(product).ToList();
        var offered = groups.SelectMany(g => g.Options).ToDictionary(o => o.Id);
        foreach (var id in picked)
        {
            if (!offered.TryGetValue(id, out var option) || !option.IsActive) return Loc.T("Err.OptionNotOffered", product.Name);
            if (option.SoldOut) return Loc.T("Err.OptionSoldOut", option.Name);
        }

        foreach (var group in groups)
        {
            var count = group.Options.Count(o => picked.Contains(o.Id));
            if (count < group.MinChoices)
                return group.MinChoices == 1 ? Loc.T("Err.ChooseOne", group.Name) : Loc.T("Err.ChooseAtLeast", group.MinChoices, group.Name);
            if (group.MaxChoices > 0 && count > group.MaxChoices)
                return group.MaxChoices == 1 ? Loc.T("Err.ChooseOnlyOne", group.Name) : Loc.T("Err.ChooseAtMost", group.MaxChoices, group.Name);
        }
        return null;
    }

    /// <summary>The item's groups in order, without groups switched off.</summary>
    public static IEnumerable<ModifierGroup> ActiveGroups(Product product) =>
        product.ModifierGroups.OrderBy(g => g.SortOrder).Select(g => g.ModifierGroup).OfType<ModifierGroup>().Where(g => g.IsActive);

    /// <summary>The picked options in the order of the item's groups, then of each group.</summary>
    public static List<Modifier> Ordered(Product product, IReadOnlyCollection<int> picked) =>
        ActiveGroups(product)
            .SelectMany(g => g.Options.Where(o => picked.Contains(o.Id)).OrderBy(o => o.SortOrder).ThenBy(o => o.Id))
            .ToList();

    /// <summary>"Large · Nutella, Banana · No sugar": the options as printed under the item.</summary>
    public static string Describe(IEnumerable<Modifier> options) =>
        string.Join(" · ", options.GroupBy(o => o.ModifierGroupId).Select(g => string.Join(", ", g.Select(o => o.Name))));

    /// <summary>The item's price with these options.</summary>
    public static decimal Price(Product product, IEnumerable<Modifier> options) => Money.Round(product.Price + options.Sum(o => o.Price));

    public static decimal Cost(Product product, IEnumerable<Modifier> options) => Money.Round(product.Cost + options.Sum(o => o.Cost));
}
