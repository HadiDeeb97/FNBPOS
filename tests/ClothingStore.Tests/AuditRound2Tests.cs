using ClothingStore.Core;
using ClothingStore.Core.Entities;
using ClothingStore.Data.Services;
using Microsoft.EntityFrameworkCore;

namespace ClothingStore.Tests;

/// <summary>Bugs found in the second whole-app review, kept fixed.</summary>
public class AuditRound2Tests
{
    [Fact]
    public async Task Receiving_more_than_outstanding_needs_an_explicit_ok()
    {
        await using var db = await TestDatabase.CreateAsync();
        var tee = await db.CreateTeeAsync(stockEach: 0);
        var supplier = await db.Suppliers.SaveAsync(new Supplier { Name = "Acme " + Guid.NewGuid().ToString("N")[..6] });
        var po = await db.PurchaseOrders.SaveAsync(new PurchaseOrder
        {
            SupplierId = supplier.Id,
            Lines = [new PurchaseOrderLine { ProductVariantId = tee.Variants[0].Id, QuantityOrdered = 10, UnitCost = 8m }],
        }, db.Manager.Id);
        var line = po.Lines.Single();

        // A typo (100 for 10), or a second PC receiving the same delivery: refused.
        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            db.PurchaseOrders.ReceiveAsync(po.Id, new Dictionary<int, int> { [line.Id] = 100 }, db.Manager.Id));
        Assert.Equal(0, (await db.GetVariantAsync(tee.Variants[0].Id)).StockQuantity);

        // The supplier really sent 12: confirmed on screen, so it is booked.
        po = await db.PurchaseOrders.ReceiveAsync(po.Id, new Dictionary<int, int> { [line.Id] = 12 }, db.Manager.Id, allowOverReceipt: true);
        Assert.Equal(PurchaseOrderStatus.Received, po.Status);
        Assert.Equal(12, (await db.GetVariantAsync(tee.Variants[0].Id)).StockQuantity);
    }

    [Fact]
    public async Task A_retired_size_keeps_its_codes_and_adding_it_again_gets_new_ones()
    {
        await using var db = await TestDatabase.CreateAsync();
        var tee = await db.CreateTeeAsync(stockEach: 0);
        var small = tee.Variants.Single(v => v.Size == "S");
        await db.Inventory.AdjustStockAsync(small.Id, 2, StockMovementType.Adjustment, "Found", db.Manager.Id);
        tee = (await db.Products.GetAsync(tee.Id))!;

        // Remove S (it has stock history, so it is retired) and add S / Black again with blank codes.
        tee.Variants.Remove(tee.Variants.Single(v => v.Id == small.Id));
        tee.Variants.Add(new ProductVariant { Size = "S", Color = "Black" });
        var saved = await db.Products.SaveAsync(tee, db.Admin.Id);

        var both = saved.Variants.Where(v => v.Size == "S").ToList();
        Assert.Equal(2, both.Count);
        Assert.Single(both, v => !v.IsActive);
        Assert.Equal(2, both.Select(v => v.Sku).Distinct().Count());
        Assert.Equal(2, both.Select(v => v.Barcode).Distinct().Count());
    }

    [Fact]
    public async Task Several_points_payments_are_rounded_once()
    {
        await using var db = await TestDatabase.CreateAsync(); // a point is worth 0.10
        var tee = await db.CreateTeeAsync();
        var customer = await db.Customers.SaveAsync(new Customer { FirstName = "Lina", LastName = "Saad", Phone = "03" + Random.Shared.Next(100000, 999999) });
        await using (var ctx = await db.Factory.CreateDbContextAsync())
            await ctx.Customers.Where(c => c.Id == customer.Id).ExecuteUpdateAsync(c => c.SetProperty(x => x.LoyaltyPoints, 1));

        // 0.05 + 0.05 is one point's worth; rounding each up would need two.
        var sale = await db.Sales.CompleteSaleAsync(new CheckoutRequest
        {
            UserId = db.Cashier.Id, CustomerId = customer.Id, Lines = [new CheckoutLine(tee.Variants[0].Id, 1)],
            Payments =
            [
                new PaymentInput(PaymentMethod.LoyaltyPoints, 0.05m), new PaymentInput(PaymentMethod.LoyaltyPoints, 0.05m),
                new PaymentInput(PaymentMethod.Card, 21.90m),
            ],
        });
        Assert.Equal(1, sale.LoyaltyPointsRedeemed);
    }

    [Fact]
    public async Task Only_managers_settle_delivery_money_by_transfer_and_adjust_store_credit()
    {
        await using var db = await TestDatabase.CreateAsync();
        var tee = await db.CreateTeeAsync();
        var customer = await db.Customers.SaveAsync(new Customer { FirstName = "Omar", LastName = "Khoury", Phone = "70" + Random.Shared.Next(100000, 999999) });
        var online = await db.Sales.CompleteSaleAsync(new CheckoutRequest
        {
            UserId = db.Cashier.Id, Channel = SalesChannel.WhatsApp, Courier = "Toters", CustomerId = customer.Id,
            Lines = [new CheckoutLine(tee.Variants[0].Id, 1)], Payments = [new PaymentInput(PaymentMethod.Delivery, 22m)],
        });

        await Assert.ThrowsAsync<BusinessRuleException>(() => db.Deliveries.SettleAsync(new SettleDeliveriesRequest
            { SaleIds = [online.Id], UserId = db.Cashier.Id, Method = SettlementMethod.Transfer }));
        await Assert.ThrowsAsync<BusinessRuleException>(() => db.Customers.AdjustStoreCreditAsync(customer.Id, 50m, db.Cashier.Id));

        await db.Deliveries.SettleAsync(new SettleDeliveriesRequest { SaleIds = [online.Id], UserId = db.Manager.Id, Method = SettlementMethod.Transfer });
        Assert.Equal(50m, (await db.Customers.AdjustStoreCreditAsync(customer.Id, 50m, db.Manager.Id)).StoreCredit);
    }

    [Fact]
    public async Task An_item_sold_in_a_past_period_and_again_since_is_not_a_slow_mover_for_that_period()
    {
        await using var db = await TestDatabase.CreateAsync();
        var tee = await db.CreateTeeAsync(stockEach: 5);
        var old = await db.Sales.CompleteSaleAsync(new CheckoutRequest
        {
            UserId = db.Cashier.Id, Lines = [new CheckoutLine(tee.Variants[0].Id, 1)], Payments = [new PaymentInput(PaymentMethod.Card, 22m)],
        });
        await using (var ctx = await db.Factory.CreateDbContextAsync())
            await ctx.Sales.Where(s => s.Id == old.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.CreatedAt, DateTime.Today.AddDays(-10)));
        await db.Sales.CompleteSaleAsync(new CheckoutRequest
        {
            UserId = db.Cashier.Id, Lines = [new CheckoutLine(tee.Variants[0].Id, 1)], Payments = [new PaymentInput(PaymentMethod.Card, 22m)],
        });

        var alerts = await db.Reports.GetStockAlertsAsync(DateTime.Today.AddDays(-15), DateTime.Today.AddDays(-5));
        var skus = alerts.SlowMovers.Select(r => r.Sku).ToList();
        Assert.DoesNotContain(tee.Variants[0].Sku, skus); // sold on day -10
        Assert.Contains(tee.Variants[1].Sku, skus);       // never sold
    }

    [Fact]
    public async Task Receipts_keep_the_tax_settings_of_the_day_of_the_sale()
    {
        await using var db = await TestDatabase.CreateAsync(); // 10%, added on top
        var tee = await db.CreateTeeAsync();
        var sale = await db.Sales.CompleteSaleAsync(new CheckoutRequest
        {
            UserId = db.Cashier.Id, Lines = [new CheckoutLine(tee.Variants[0].Id, 1)], Payments = [new PaymentInput(PaymentMethod.Card, 22m)],
        });
        Assert.Equal((10m, false), (sale.TaxRate, sale.PricesIncludeTax));
    }
}
