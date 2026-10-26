using ClothingStore.Core;
using ClothingStore.Core.Entities;
using ClothingStore.Data.Services;
using Microsoft.EntityFrameworkCore;

namespace ClothingStore.Tests;

/// <summary>Two tills saving at the same moment: the one that loses tries again by itself instead of showing an error.</summary>
public class TillConflictTests
{
    [Fact]
    public async Task Tills_selling_at_the_same_moment_all_go_through_with_their_own_receipt_numbers()
    {
        await using var db = await TestDatabase.CreateAsync();
        var tee = await db.CreateTeeAsync(stockEach: 100);
        var shift = await db.OpenShiftAsync();

        // Six tills sell the same tee at once: same receipt number and the same stock row for all of them.
        var sales = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => db.Sales.CompleteSaleAsync(new CheckoutRequest
        {
            UserId = db.Cashier.Id, ShiftId = shift.Id,
            Lines = [new CheckoutLine(tee.Variants[0].Id, 1)], Payments = [new PaymentInput(PaymentMethod.Card, 22m)],
        })));

        Assert.Equal(6, sales.Select(s => s.ReceiptNumber).Distinct().Count());
        Assert.Equal(94, (await db.GetVariantAsync(tee.Variants[0].Id)).StockQuantity);
    }

    [Fact]
    public async Task Two_returns_on_the_same_receipt_at_the_same_moment_both_go_through()
    {
        await using var db = await TestDatabase.CreateAsync();
        var tee = await db.CreateTeeAsync();
        var shift = await db.OpenShiftAsync();
        var sale = await db.Sales.CompleteSaleAsync(new CheckoutRequest
        {
            UserId = db.Cashier.Id, ShiftId = shift.Id,
            Lines = [new CheckoutLine(tee.Variants[0].Id, 1), new CheckoutLine(tee.Variants[1].Id, 1)],
            Payments = [new PaymentInput(PaymentMethod.Card, 44m)],
        });

        var returns = await Task.WhenAll(sale.Lines.Select(l => db.Returns.ProcessReturnAsync(new ReturnRequest
        {
            SaleId = sale.Id, UserId = db.Cashier.Id, ShiftId = shift.Id, Lines = [new ReturnLineRequest(l.Id, 1)],
        })));

        Assert.NotEqual(returns[0].ReturnNumber, returns[1].ReturnNumber);
        Assert.Equal(sale.Total, returns.Sum(r => r.TotalRefund));
    }

    [Fact]
    public async Task Receipt_numbers_carry_on_past_9999_in_a_day()
    {
        await using var db = await TestDatabase.CreateAsync();
        var tee = await db.CreateTeeAsync();
        var shift = await db.OpenShiftAsync();
        Task<Sale> Sell() => db.Sales.CompleteSaleAsync(new CheckoutRequest
        {
            UserId = db.Cashier.Id, ShiftId = shift.Id,
            Lines = [new CheckoutLine(tee.Variants[0].Id, 1)], Payments = [new PaymentInput(PaymentMethod.Card, 22m)],
        });
        var first = await Sell();
        await using (var ctx = await db.Factory.CreateDbContextAsync())
            await ctx.Sales.Where(s => s.Id == first.Id).ExecuteUpdateAsync(u => u.SetProperty(s => s.ReceiptNumber, first.ReceiptNumber[..^4] + "9999"));

        var next = await Sell();
        var after = await Sell();

        Assert.EndsWith("-10000", next.ReceiptNumber);
        Assert.EndsWith("-10001", after.ReceiptNumber);
    }
}
