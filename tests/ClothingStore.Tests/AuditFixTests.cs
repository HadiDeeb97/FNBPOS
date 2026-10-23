using ClothingStore.Core;
using ClothingStore.Data.Services;

namespace ClothingStore.Tests;

/// <summary>Bugs found in the whole-app review. The tee sells for 22.00 (20 + 10% tax).</summary>
public class AuditFixTests
{
    [Fact]
    public async Task Stock_can_come_back_while_the_count_is_below_zero()
    {
        await using var db = await TestDatabase.CreateAsync();
        var settings = await db.Settings.GetAsync();
        settings.AllowNegativeStock = true;
        await db.Settings.SaveAsync(settings);
        var tee = await db.CreateTeeAsync(stockEach: 1);
        var variant = tee.Variants[0];
        var shift = await db.OpenShiftAsync();

        var sale = await db.Sales.CompleteSaleAsync(new CheckoutRequest
        {
            UserId = db.Cashier.Id, ShiftId = shift.Id, Lines = [new CheckoutLine(variant.Id, 3)],
            Payments = [new PaymentInput(PaymentMethod.Cash, 66m)],
        });
        Assert.Equal(-2, (await db.GetVariantAsync(variant.Id)).StockQuantity);

        // A return used to be refused because the count stayed below zero after it.
        await db.Returns.ProcessReturnAsync(new ReturnRequest
        {
            SaleId = sale.Id, UserId = db.Cashier.Id, ShiftId = shift.Id, Lines = [new ReturnLineRequest(sale.Lines[0].Id, 1)],
        });
        Assert.Equal(-1, (await db.GetVariantAsync(variant.Id)).StockQuantity);

        // Taking more out is still refused when negative stock is off.
        settings.AllowNegativeStock = false;
        await db.Settings.SaveAsync(settings);
        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            db.Inventory.AdjustStockAsync(variant.Id, -1, StockMovementType.Damaged, "torn", db.Manager.Id));
        await db.Inventory.AdjustStockAsync(variant.Id, 1, StockMovementType.Adjustment, "found one", db.Manager.Id);
        Assert.Equal(0, (await db.GetVariantAsync(variant.Id)).StockQuantity);
    }

    [Fact]
    public async Task An_order_the_delivery_company_already_paid_for_cannot_be_voided()
    {
        await using var db = await TestDatabase.CreateAsync();
        var tee = await db.CreateTeeAsync();
        var sale = await db.Sales.CompleteSaleAsync(new CheckoutRequest
        {
            UserId = db.Cashier.Id, CustomerId = await db.ShopperIdAsync(), Lines = [new CheckoutLine(tee.Variants[0].Id, 1)],
            Channel = SalesChannel.Instagram, Courier = "Toters", Payments = [new PaymentInput(PaymentMethod.Delivery, 22m)],
        });
        await db.Deliveries.SettleAsync(new SettleDeliveriesRequest { SaleIds = [sale.Id], UserId = db.Manager.Id, Method = SettlementMethod.Transfer });

        await Assert.ThrowsAsync<BusinessRuleException>(() => db.Sales.VoidSaleAsync(sale.Id, db.Manager.Id, "mistake"));
    }

    [Fact]
    public async Task Voiding_still_works_and_a_voided_sale_cannot_be_returned()
    {
        await using var db = await TestDatabase.CreateAsync();
        var tee = await db.CreateTeeAsync();
        var shift = await db.OpenShiftAsync();
        var sale = await db.Sales.CompleteSaleAsync(new CheckoutRequest
        {
            UserId = db.Cashier.Id, ShiftId = shift.Id, Lines = [new CheckoutLine(tee.Variants[0].Id, 2)],
            Payments = [new PaymentInput(PaymentMethod.Cash, 44m)],
        });
        await db.Sales.VoidSaleAsync(sale.Id, db.Manager.Id, "mistake");
        Assert.Equal(10, (await db.GetVariantAsync(tee.Variants[0].Id)).StockQuantity);
        await Assert.ThrowsAsync<BusinessRuleException>(() => db.Returns.ProcessReturnAsync(new ReturnRequest
        {
            SaleId = sale.Id, UserId = db.Cashier.Id, ShiftId = shift.Id, Lines = [new ReturnLineRequest(sale.Lines[0].Id, 1)],
        }));
    }
}
