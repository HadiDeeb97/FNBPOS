using ClothingStore.Core;
using ClothingStore.Data.Services;

namespace ClothingStore.Tests;

public class SalesHistoryTests
{
    /// <summary>Three sales: one kept, one with an item returned, one voided. The summary and filters tell them apart.</summary>
    [Fact]
    public async Task History_shows_voided_and_returned_sales_and_the_net_after_them()
    {
        await using var db = await TestDatabase.CreateAsync();
        var tee = await db.CreateTeeAsync();
        var shift = await db.OpenShiftAsync();
        async Task<Core.Entities.Sale> Sell(int qty) => await db.Sales.CompleteSaleAsync(new CheckoutRequest
        {
            UserId = db.Cashier.Id, ShiftId = shift.Id, Lines = [new CheckoutLine(tee.Variants[0].Id, qty)],
            Payments = [new PaymentInput(PaymentMethod.Card, 22m * qty)],
        });

        await Sell(1);                                   // kept: 22
        var returned = await Sell(2);                    // 44, one comes back: 22 refunded
        var voided = await Sell(1);                      // 22, voided
        await db.Returns.ProcessReturnAsync(new ReturnRequest
        {
            SaleId = returned.Id, UserId = db.Cashier.Id, ShiftId = shift.Id, Lines = [new ReturnLineRequest(returned.Lines[0].Id, 1)],
        });
        await db.Sales.VoidSaleAsync(voided.Id, db.Manager.Id, "Wrong item");

        var (from, to) = (DateTime.Today, DateTime.Today.AddDays(1));
        var summary = await db.Sales.GetSummaryAsync(from, to);
        Assert.Equal((3, 88m), (summary.Count, summary.Total));
        Assert.Equal((1, 22m), (summary.VoidedCount, summary.VoidedTotal));
        Assert.Equal((1, 22m), (summary.ReturnedCount, summary.Refunded));
        Assert.Equal(44m, summary.Net);                  // 88 - 22 voided - 22 refunded

        var all = await db.Sales.SearchAsync(from, to);
        Assert.Equal(3, all.Count);
        var withReturn = all.Single(s => s.Id == returned.Id);
        // The list comes without lines (quicker) but still knows how many items each sale had; the lines load for one sale.
        Assert.Empty(withReturn.Lines);
        Assert.Equal(2, withReturn.ItemCount);
        var lines = await db.Sales.GetLinesAsync(returned.Id);
        Assert.Equal((2, 1), (Assert.Single(lines).Quantity, lines[0].ReturnedQuantity));
        Assert.Equal((22m, 22m, true), (withReturn.RefundedTotal, withReturn.NetTotal, withReturn.HasReturns));
        Assert.Equal(0m, all.Single(s => s.Id == voided.Id).NetTotal);

        Assert.Equal(returned.Id, Assert.Single(await db.Sales.SearchAsync(from, to, filter: SaleListFilter.WithReturns)).Id);
        Assert.Equal(voided.Id, Assert.Single(await db.Sales.SearchAsync(from, to, filter: SaleListFilter.Voided)).Id);
        Assert.Equal(2, (await db.Sales.SearchAsync(from, to, filter: SaleListFilter.Completed)).Count);
    }
}

public class CashDrawerTests
{
    [Fact]
    public async Task Opening_the_drawer_without_a_sale_is_counted_on_the_open_shift_only()
    {
        await using var db = await TestDatabase.CreateAsync();
        var shift = await db.OpenShiftAsync();
        Assert.True(await db.Shifts.RecordNoSaleOpeningAsync(shift.Id));
        Assert.True(await db.Shifts.RecordNoSaleOpeningAsync(shift.Id));
        Assert.Equal(2, (await db.Shifts.GetSummaryAsync(shift.Id)).NoSaleOpenings);

        await db.Shifts.CloseShiftAsync(shift.Id, 100m, null);
        Assert.False(await db.Shifts.RecordNoSaleOpeningAsync(shift.Id));
    }
}
