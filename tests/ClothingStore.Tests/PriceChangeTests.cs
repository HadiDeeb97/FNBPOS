using ClothingStore.Core;
using ClothingStore.Core.Entities;
using ClothingStore.Core.Pricing;
using ClothingStore.Data.Services;

namespace ClothingStore.Tests;

/// <summary>A price typed in the register instead of the shelf price.</summary>
public class PriceChangeTests
{
    // The test tee: shelf price 20, cost 8, tax 10% on top, cashiers may give up to 10% off.
    private static Task<Sale> SellAsync(TestDatabase db, Shift shift, Product tee, decimal? price, User? user = null,
        DiscountType discount = DiscountType.None, decimal discountValue = 0, int? approvedBy = null)
    {
        var line = new CheckoutLine(tee.Variants[0].Id, 1, discount, discountValue, price);
        var total = price is <= 0 ? 1m // refused anyway
            : CartCalculator.Calculate([new CartLineInput(price ?? 20m, 1, discount, discountValue)], DiscountType.None, 0, 10m, false).Total;
        return db.Sales.CompleteSaleAsync(new CheckoutRequest
        {
            UserId = (user ?? db.Cashier).Id, ShiftId = shift.Id, Lines = [line],
            Payments = [new PaymentInput(PaymentMethod.Card, total)], ApprovedByUserId = approvedBy,
        });
    }

    [Fact]
    public async Task A_higher_price_is_charged_and_the_shelf_price_is_kept_with_the_line()
    {
        await using var db = await TestDatabase.CreateAsync();
        var tee = await db.CreateTeeAsync();
        var shift = await db.OpenShiftAsync();

        var sale = await SellAsync(db, shift, tee, 25m);

        var line = Assert.Single(sale.Lines);
        Assert.Equal((25m, 20m, true), (line.UnitPrice, line.ListPrice, line.PriceChanged));
        Assert.Equal(27.50m, sale.Total);
        Assert.Equal(0m, sale.DiscountTotal); // a typed price is not a discount

        // The shelf price itself, typed or not, isn't a change.
        var same = await SellAsync(db, shift, tee, 20m);
        Assert.Null(same.Lines[0].ListPrice);
        Assert.False(same.Lines[0].PriceChanged);

        var report = await db.Reports.GetSalesReportAsync(DateTime.Today, DateTime.Today.AddDays(1));
        Assert.Equal((49.50m, 0m), (report.TotalSales, report.Discounts));
    }

    [Fact]
    public async Task A_lower_price_counts_towards_the_cashiers_discount_limit()
    {
        await using var db = await TestDatabase.CreateAsync();
        var tee = await db.CreateTeeAsync(stockEach: 20);
        var shift = await db.OpenShiftAsync();

        // 7.5% off the shelf price: within the 10% limit.
        Assert.Equal(18.50m, (await SellAsync(db, shift, tee, 18.50m)).Lines[0].UnitPrice);

        // 25% off: a manager must approve.
        var refused = await Assert.ThrowsAsync<BusinessRuleException>(() => SellAsync(db, shift, tee, 15m));
        Assert.Contains("10", refused.Message);
        Assert.Equal(15m, (await SellAsync(db, shift, tee, 15m, approvedBy: db.Manager.Id)).Lines[0].UnitPrice);
        await Assert.ThrowsAsync<BusinessRuleException>(() => SellAsync(db, shift, tee, 15m, approvedBy: db.Cashier.Id));

        // A lower price and a discount add up: 5% + 6% of the rest is more than 10% off the shelf price.
        await Assert.ThrowsAsync<BusinessRuleException>(() => SellAsync(db, shift, tee, 19m, discount: DiscountType.Percent, discountValue: 6));
        // A higher price with a discount that still leaves it near the shelf price is fine: 22 - 10% = 19.80.
        await SellAsync(db, shift, tee, 22m, discount: DiscountType.Percent, discountValue: 10);

        // Managers have no limit.
        await SellAsync(db, shift, tee, 12m, user: db.Manager);
    }

    [Fact]
    public async Task Selling_below_cost_needs_a_manager_for_a_cashier()
    {
        await using var db = await TestDatabase.CreateAsync(s => s.MaxCashierDiscountPercent = 100);
        var tee = await db.CreateTeeAsync();
        var shift = await db.OpenShiftAsync();

        // Within the discount limit (here 100%), but below the cost of 8.
        var refused = await Assert.ThrowsAsync<BusinessRuleException>(() => SellAsync(db, shift, tee, 7m));
        Assert.Contains("Basic Tee", refused.Message);
        // A discount that takes it below cost too.
        await Assert.ThrowsAsync<BusinessRuleException>(() => SellAsync(db, shift, tee, null, discount: DiscountType.Amount, discountValue: 12.5m));

        await SellAsync(db, shift, tee, 7m, approvedBy: db.Manager.Id);
        await SellAsync(db, shift, tee, 7m, user: db.Manager); // a manager is only warned at the till
        await SellAsync(db, shift, tee, 8m); // at cost is not below it
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(0.004)]
    public async Task A_price_must_be_more_than_zero(decimal price)
    {
        await using var db = await TestDatabase.CreateAsync();
        var tee = await db.CreateTeeAsync();
        var shift = await db.OpenShiftAsync();
        await Assert.ThrowsAsync<BusinessRuleException>(() => SellAsync(db, shift, tee, price, user: db.Manager));
        Assert.Equal(10, (await db.GetVariantAsync(tee.Variants[0].Id)).StockQuantity);
    }

    [Fact]
    public async Task A_held_cart_keeps_its_typed_prices_and_an_old_one_still_opens()
    {
        await using var db = await TestDatabase.CreateAsync();
        var tee = await db.CreateTeeAsync();
        var held = await db.Sales.HoldAsync("Rana", db.Cashier.Id,
            new HeldCart([new HeldCartLine(tee.Variants[0].Id, 2, DiscountType.None, 0, 24.5m), new HeldCartLine(tee.Variants[1].Id, 1, DiscountType.None, 0)],
                DiscountType.None, 0, null));

        var cart = await db.Sales.PeekHeldAsync(held.Id);
        Assert.Equal([24.5m, null], cart.Lines.Select(l => l.UnitPrice));

        // A cart held by the previous version (no prices in it).
        await using (var ctx = await db.Factory.CreateDbContextAsync())
        {
            ctx.HeldSales.Add(new HeldSale
            {
                Label = "Old", UserId = db.Cashier.Id, CreatedAt = DateTime.Now,
                Payload = $$"""{"Lines":[{"VariantId":{{tee.Variants[0].Id}},"Quantity":1,"DiscountType":0,"DiscountValue":0}],"CartDiscountType":0,"CartDiscountValue":0,"CustomerId":null}""",
            });
            await ctx.SaveChangesAsync();
        }
        var old = (await db.Sales.GetHeldAsync()).Single(h => h.Label == "Old");
        Assert.Null(Assert.Single((await db.Sales.PeekHeldAsync(old.Id)).Lines).UnitPrice);
    }

    [Fact]
    public async Task A_return_refunds_the_price_that_was_paid()
    {
        await using var db = await TestDatabase.CreateAsync();
        var tee = await db.CreateTeeAsync();
        var shift = await db.OpenShiftAsync();
        var sale = await SellAsync(db, shift, tee, 30m);

        var ret = await db.Returns.ProcessReturnAsync(new ReturnRequest
        {
            SaleId = sale.Id, UserId = db.Cashier.Id, ShiftId = shift.Id, Lines = [new ReturnLineRequest(sale.Lines[0].Id, 1)],
        });

        Assert.Equal(33m, ret.TotalRefund);
    }

    [Theory]
    [InlineData("45", 45)]
    [InlineData("45.5", 45.5)]
    [InlineData("45,50", 45.5)]
    [InlineData(" 1,250.00 ", 1250)]
    [InlineData("1,250", 1250)]
    [InlineData("٤٥٫٥٠", 45.5)]
    public void Typed_prices_are_read_the_way_cashiers_type_them(string text, decimal expected)
    {
        Assert.True(PriceRules.TryParsePrice(text, out var price));
        Assert.Equal(expected, price);
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("-5")]
    [InlineData("4.5.6")]
    public void Nonsense_is_not_a_price(string text) => Assert.False(PriceRules.TryParsePrice(text, out _));

    [Fact]
    public void Giving_away_is_measured_from_the_shelf_price()
    {
        CartTotals Totals(params (decimal Price, DiscountType Type, decimal Value)[] lines) =>
            CartCalculator.Calculate(lines.Select(l => new CartLineInput(l.Price, 1, l.Type, l.Value)).ToList(), DiscountType.None, 0, 10m, false);

        // Shelf 20 sold at 15: 25% off. Shelf 20 sold at 25: nothing off, and it doesn't hide the other line's 25%.
        Assert.Equal(25m, PriceRules.ReductionPercent([(20m, 1)], Totals((15m, DiscountType.None, 0))));
        Assert.Equal(0m, PriceRules.ReductionPercent([(20m, 1)], Totals((25m, DiscountType.None, 0))));
        Assert.Equal(25m, PriceRules.ReductionPercent([(20m, 1), (20m, 1)], Totals((15m, DiscountType.None, 0), (25m, DiscountType.None, 0))));
        // Unchanged prices: the same as the discount alone.
        Assert.Equal(10m, PriceRules.ReductionPercent([(20m, 1)], Totals((20m, DiscountType.Percent, 10))));
    }
}
