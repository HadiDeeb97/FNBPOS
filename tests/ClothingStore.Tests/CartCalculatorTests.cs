using ClothingStore.Core;
using ClothingStore.Core.Pricing;

namespace ClothingStore.Tests;

public class CartCalculatorTests
{
    [Fact]
    public void Tax_exclusive_adds_tax_on_top()
    {
        var totals = CartCalculator.Calculate([new CartLineInput(10m, 3)], DiscountType.None, 0, 10m, pricesIncludeTax: false);

        Assert.Equal(30m, totals.Subtotal);
        Assert.Equal(3m, totals.TaxTotal);
        Assert.Equal(33m, totals.Total);
    }

    [Fact]
    public void Tax_inclusive_extracts_tax_from_price()
    {
        var totals = CartCalculator.Calculate([new CartLineInput(115m, 1)], DiscountType.None, 0, 15m, pricesIncludeTax: true);

        Assert.Equal(115m, totals.Total);
        Assert.Equal(15m, totals.TaxTotal);
        Assert.Equal(100m, totals.TotalExcludingTax);
    }

    [Fact]
    public void Line_and_cart_discounts_stack()
    {
        var totals = CartCalculator.Calculate(
            [
                new CartLineInput(50m, 2, DiscountType.Percent, 10m), // 100 -> 90
                new CartLineInput(10m, 1, DiscountType.Amount, 2m),    // 10 -> 8
            ],
            DiscountType.Amount, 9.80m, 0m, pricesIncludeTax: true);

        Assert.Equal(110m, totals.Subtotal);
        Assert.Equal(12m, totals.LineDiscounts);
        Assert.Equal(9.80m, totals.CartDiscount);
        Assert.Equal(88.20m, totals.Total);
        // Cart discount allocated proportionally: 90/98 and 8/98 of 9.80.
        Assert.Equal(9.00m, totals.Lines[0].CartDiscountShare);
        Assert.Equal(0.80m, totals.Lines[1].CartDiscountShare);
    }

    [Fact]
    public void Discount_never_exceeds_line_value()
    {
        var totals = CartCalculator.Calculate([new CartLineInput(5m, 1, DiscountType.Amount, 50m)], DiscountType.Percent, 150m, 10m, false);

        Assert.Equal(5m, totals.LineDiscounts);
        Assert.Equal(0m, totals.CartDiscount);
        Assert.Equal(0m, totals.Total);
    }

    [Theory]
    [InlineData(10.00, 3)]
    [InlineData(0.01, 7)]
    [InlineData(33.33, 5)]
    [InlineData(100, 1)]
    public void Allocation_sums_exactly_and_respects_weights(decimal amount, int lines)
    {
        var weights = Enumerable.Range(1, lines).Select(i => i * 3.33m).ToArray();
        var shares = CartCalculator.Allocate(amount, weights);

        Assert.Equal(Math.Min(amount, weights.Sum()), shares.Sum());
        Assert.All(shares.Zip(weights), p => Assert.InRange(p.First, 0m, p.Second));
    }

    [Fact]
    public void Line_totals_add_up_to_cart_total_with_awkward_numbers()
    {
        var lines = new[] { new CartLineInput(19.99m, 3), new CartLineInput(7.77m, 2), new CartLineInput(0.99m, 11) };
        var totals = CartCalculator.Calculate(lines, DiscountType.Percent, 12.5m, 8.25m, false);

        Assert.Equal(totals.Total, totals.Lines.Sum(l => l.Total));
        Assert.Equal(totals.CartDiscount, totals.Lines.Sum(l => l.CartDiscountShare));
        Assert.Equal(totals.Subtotal - totals.DiscountTotal + totals.TaxTotal, totals.Total);
    }

    [Fact]
    public void Empty_cart_is_zero()
    {
        var totals = CartCalculator.Calculate([], DiscountType.Percent, 10m, 10m, false);
        Assert.Equal(0m, totals.Total);
    }
}
