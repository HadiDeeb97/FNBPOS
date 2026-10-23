namespace ClothingStore.Core.Pricing;

public sealed record CartLineInput(
    decimal UnitPrice,
    int Quantity,
    DiscountType DiscountType = DiscountType.None,
    decimal DiscountValue = 0m);

public sealed record CartLineResult(
    decimal Gross,
    decimal LineDiscount,
    decimal CartDiscountShare,
    decimal Tax,
    decimal Total)
{
    public decimal Discount => LineDiscount + CartDiscountShare;

    /// <summary>Amount after discounts, before tax is added/extracted.</summary>
    public decimal Taxable => Gross - Discount;
}

public sealed record CartTotals(
    IReadOnlyList<CartLineResult> Lines,
    decimal Subtotal,
    decimal LineDiscounts,
    decimal CartDiscount,
    decimal TaxTotal,
    decimal Total)
{
    public decimal DiscountTotal => LineDiscounts + CartDiscount;
    public decimal TotalExcludingTax => Total - TaxTotal;

    public static readonly CartTotals Empty = new([], 0, 0, 0, 0, 0);
}

/// <summary>
/// Pure pricing engine used by the register and re-run by the sales service at checkout,
/// so the totals the cashier sees are exactly what is persisted.
/// </summary>
public static class CartCalculator
{
    public static decimal ResolveDiscount(decimal baseAmount, DiscountType type, decimal value)
    {
        if (baseAmount <= 0) return 0m;
        return type switch
        {
            DiscountType.Percent => Money.Round(baseAmount * Math.Clamp(value, 0m, 100m) / 100m),
            DiscountType.Amount => Math.Min(Money.Round(Math.Max(value, 0m)), baseAmount),
            _ => 0m,
        };
    }

    /// <summary>Effective discount percentage, used to enforce cashier discount limits.</summary>
    public static decimal DiscountPercent(decimal gross, decimal discount) =>
        gross <= 0 ? 0m : Math.Round(discount / gross * 100m, 2);

    public static CartTotals Calculate(
        IReadOnlyList<CartLineInput> lines,
        DiscountType cartDiscountType,
        decimal cartDiscountValue,
        decimal taxRatePercent,
        bool pricesIncludeTax)
    {
        if (lines.Count == 0) return CartTotals.Empty;

        var gross = new decimal[lines.Count];
        var lineDiscount = new decimal[lines.Count];
        var afterLine = new decimal[lines.Count];

        for (var i = 0; i < lines.Count; i++)
        {
            var l = lines[i];
            if (l.Quantity <= 0) throw new ArgumentException("Quantity must be positive.", nameof(lines));
            if (l.UnitPrice < 0) throw new ArgumentException("Unit price cannot be negative.", nameof(lines));

            gross[i] = Money.Round(l.UnitPrice * l.Quantity);
            lineDiscount[i] = ResolveDiscount(gross[i], l.DiscountType, l.DiscountValue);
            afterLine[i] = gross[i] - lineDiscount[i];
        }

        var afterLineTotal = afterLine.Sum();
        var cartDiscount = ResolveDiscount(afterLineTotal, cartDiscountType, cartDiscountValue);
        var shares = Allocate(cartDiscount, afterLine);

        var rate = Math.Max(taxRatePercent, 0m) / 100m;
        var results = new CartLineResult[lines.Count];
        for (var i = 0; i < lines.Count; i++)
        {
            var taxable = afterLine[i] - shares[i];
            decimal tax, total;
            if (pricesIncludeTax)
            {
                tax = Money.Round(taxable - taxable / (1m + rate));
                total = taxable;
            }
            else
            {
                tax = Money.Round(taxable * rate);
                total = taxable + tax;
            }
            results[i] = new CartLineResult(gross[i], lineDiscount[i], shares[i], tax, total);
        }

        return new CartTotals(
            results,
            Subtotal: gross.Sum(),
            LineDiscounts: lineDiscount.Sum(),
            CartDiscount: cartDiscount,
            TaxTotal: results.Sum(r => r.Tax),
            Total: results.Sum(r => r.Total));
    }

    /// <summary>
    /// Splits <paramref name="amount"/> across lines proportionally to <paramref name="weights"/>,
    /// to the cent, never giving a line more than its weight and always summing exactly.
    /// </summary>
    public static decimal[] Allocate(decimal amount, IReadOnlyList<decimal> weights)
    {
        var shares = new decimal[weights.Count];
        var totalWeight = weights.Sum();
        if (amount <= 0 || totalWeight <= 0) return shares;

        for (var i = 0; i < weights.Count; i++)
            shares[i] = Math.Min(weights[i], Money.Round(amount * weights[i] / totalWeight));

        var diff = amount - shares.Sum();
        var cent = diff > 0 ? 0.01m : -0.01m;
        // Distribute rounding remainder one cent at a time, starting with the largest lines.
        var order = Enumerable.Range(0, weights.Count).OrderByDescending(i => weights[i]).ToArray();
        var guard = 0;
        while (diff != 0 && guard++ < 100_000)
        {
            var moved = false;
            foreach (var i in order)
            {
                if (diff == 0) break;
                var next = shares[i] + cent;
                if (next < 0 || next > weights[i]) continue;
                shares[i] = next;
                diff -= cent;
                moved = true;
            }
            if (!moved) break;
        }
        return shares;
    }
}
