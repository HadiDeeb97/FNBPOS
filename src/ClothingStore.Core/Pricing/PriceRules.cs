namespace ClothingStore.Core.Pricing;

/// <summary>
/// How much a cart gives away from the shelf prices, and which lines sell below cost: worked out the same way at the
/// till (to ask for a manager) and when the sale is saved (to refuse it without one).
/// </summary>
public static class PriceRules
{
    /// <summary>
    /// The biggest reduction from the shelf prices, in percent: the worst line or the whole cart, whichever is larger.
    /// Typed prices, line discounts and the cart discount all count; a price above the shelf price gives nothing away.
    /// </summary>
    public static decimal ReductionPercent(IReadOnlyList<(decimal ShelfPrice, int Quantity)> lines, CartTotals totals)
    {
        decimal worst = 0, shelfTotal = 0, soldTotal = 0;
        for (var i = 0; i < lines.Count; i++)
        {
            var shelf = Money.Round(lines[i].ShelfPrice * lines[i].Quantity);
            var sold = totals.Lines[i].Taxable;
            worst = Math.Max(worst, CartCalculator.DiscountPercent(shelf, Math.Max(0m, shelf - sold)));
            shelfTotal += shelf;
            soldTotal += sold;
        }
        return Math.Max(worst, CartCalculator.DiscountPercent(shelfTotal, Math.Max(0m, shelfTotal - soldTotal)));
    }

    /// <summary>A price typed at the till: "45", "45.5", "45,50", "1,250.00" and Arabic digits. Never negative.</summary>
    public static bool TryParsePrice(string? text, out decimal price)
    {
        var t = new string((text ?? "").Select(c => c switch
        {
            >= '٠' and <= '٩' => (char)('0' + (c - '٠')),
            >= '۰' and <= '۹' => (char)('0' + (c - '۰')),
            '٫' => '.',
            '٬' => ',',
            _ => c,
        }).Where(c => !char.IsWhiteSpace(c)).ToArray());
        // One comma, no point and at most two digits after it is a decimal comma ("45,50"); otherwise commas group thousands.
        if (t.Count(c => c == ',') == 1 && !t.Contains('.') && t.Length - t.IndexOf(',') <= 3) t = t.Replace(',', '.');
        const System.Globalization.NumberStyles style = System.Globalization.NumberStyles.AllowDecimalPoint | System.Globalization.NumberStyles.AllowThousands;
        return decimal.TryParse(t, style, System.Globalization.CultureInfo.InvariantCulture, out price);
    }

    /// <summary>True when the line, after its discounts and without tax, brings in less than it cost.</summary>
    public static bool IsBelowCost(CartLineResult line, int quantity, decimal unitCost) =>
        line.Total - line.Tax < Money.Round(unitCost * quantity);
}
