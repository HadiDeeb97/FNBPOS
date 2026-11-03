using ClothingStore.Core;
using ClothingStore.Core.Entities;
using ClothingStore.Data.Services;

namespace ClothingStore.Tests;

/// <summary>Deleting a sale for good (admins and managers).</summary>
public class SaleDeleteTests
{
    private static Task<Sale> SellAsync(TestDatabase db, Shift shift, ProductVariant variant, int? customerId = null, params PaymentInput[] payments) =>
        db.Sales.CompleteSaleAsync(new CheckoutRequest
        {
            UserId = db.Cashier.Id,
            ShiftId = shift.Id,
            CustomerId = customerId,
            Lines = [new CheckoutLine(variant.Id, 1)],
            Payments = payments.Length > 0 ? payments : [new PaymentInput(PaymentMethod.Cash, 50m)],
        });

    [Fact]
    public async Task A_deleted_sale_is_gone_its_stock_is_back_and_balances_are_reversed()
    {
        await using var db = await TestDatabase.CreateAsync();
        var tee = await db.CreateTeeAsync(stockEach: 5);
        var customer = await db.Customers.SaveAsync(new Customer { FirstName = "Ben" });
        await db.Customers.AdjustStoreCreditAsync(customer.Id, 5m, db.Manager.Id);
        var shift = await db.OpenShiftAsync();
        var sale = await SellAsync(db, shift, tee.Variants[0], customer.Id, new PaymentInput(PaymentMethod.StoreCredit, 5m), new PaymentInput(PaymentMethod.Cash, 20m));

        await Assert.ThrowsAsync<BusinessRuleException>(() => db.Sales.DeleteSaleAsync(sale.Id, db.Cashier.Id));
        await db.Sales.DeleteSaleAsync(sale.Id, db.Manager.Id);

        Assert.Null(await db.Sales.GetAsync(sale.Id));
        Assert.Equal(5, (await db.GetVariantAsync(tee.Variants[0].Id)).StockQuantity);
        var c = await db.GetCustomerAsync(customer.Id);
        Assert.Equal(5m, c.StoreCredit);
        Assert.Equal(0, c.LoyaltyPoints);
        var summary = await db.Shifts.GetSummaryAsync(shift.Id);
        Assert.Equal(100m, summary.ExpectedCash); // only the opening float
        Assert.Equal(0, summary.VoidedCount);
        Assert.Contains(await db.Inventory.GetMovementsAsync(tee.Variants[0].Id),
            m => m.Type == StockMovementType.SaleDeleted && m.Reference == sale.ReceiptNumber && m.QuantityChange == 1);
    }

    [Fact]
    public async Task A_deleted_receipt_number_is_never_given_to_another_sale()
    {
        await using var db = await TestDatabase.CreateAsync();
        var tee = await db.CreateTeeAsync();
        var shift = await db.OpenShiftAsync();
        var first = await SellAsync(db, shift, tee.Variants[0]);
        var second = await SellAsync(db, shift, tee.Variants[0]);

        await db.Sales.DeleteSaleAsync(second.Id, db.Admin.Id);
        var third = await SellAsync(db, shift, tee.Variants[0]);

        Assert.NotEqual(second.ReceiptNumber, third.ReceiptNumber);
        Assert.EndsWith("-0003", third.ReceiptNumber);
        Assert.EndsWith("-0001", first.ReceiptNumber);
    }

    [Fact]
    public async Task Deleting_a_voided_sale_does_not_put_its_stock_back_twice()
    {
        await using var db = await TestDatabase.CreateAsync();
        var tee = await db.CreateTeeAsync(stockEach: 5);
        var shift = await db.OpenShiftAsync();
        var sale = await SellAsync(db, shift, tee.Variants[0]);
        await db.Sales.VoidSaleAsync(sale.Id, db.Manager.Id, "mistake");

        await db.Sales.DeleteSaleAsync(sale.Id, db.Manager.Id);

        Assert.Null(await db.Sales.GetAsync(sale.Id));
        Assert.Equal(5, (await db.GetVariantAsync(tee.Variants[0].Id)).StockQuantity);
    }

    [Fact]
    public async Task Returned_sales_and_sales_of_a_closed_shift_cannot_be_deleted()
    {
        await using var db = await TestDatabase.CreateAsync();
        var tee = await db.CreateTeeAsync();
        var shift = await db.OpenShiftAsync();
        var returned = await SellAsync(db, shift, tee.Variants[0]);
        var kept = await SellAsync(db, shift, tee.Variants[0]);
        await db.Returns.ProcessReturnAsync(new ReturnRequest
        {
            SaleId = returned.Id, UserId = db.Cashier.Id, ShiftId = shift.Id,
            Lines = [new ReturnLineRequest(returned.Lines[0].Id, 1)],
        });

        await Assert.ThrowsAsync<BusinessRuleException>(() => db.Sales.DeleteSaleAsync(returned.Id, db.Manager.Id));

        var summary = await db.Shifts.GetSummaryAsync(shift.Id);
        await db.Shifts.CloseShiftAsync(shift.Id, summary.ExpectedCash, null, summary.ExpectedCashLbp);
        await Assert.ThrowsAsync<BusinessRuleException>(() => db.Sales.DeleteSaleAsync(kept.Id, db.Manager.Id));
        Assert.NotNull(await db.Sales.GetAsync(kept.Id));
    }

    [Theory]
    [InlineData("R20261026-0009", "R20261026-0010", "R20261026-0010")]
    [InlineData("R20261026-9999", "R20261026-10000", "R20261026-10000")]
    [InlineData(null, "R20261026-0002", "R20261026-0002")]
    [InlineData("R20261026-0004", null, "R20261026-0004")]
    public void The_later_of_two_receipt_numbers(string? a, string? b, string expected)
    {
        Assert.Equal(expected, QueryHelpers.Later(a, b));
        Assert.Equal(expected, QueryHelpers.Later(b, a));
    }
}
