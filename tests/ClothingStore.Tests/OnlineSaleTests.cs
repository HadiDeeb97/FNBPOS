using ClothingStore.Core;
using ClothingStore.Data.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace ClothingStore.Tests;

/// <summary>Online orders are made on the register as a normal sale with a channel. The tee sells for 22.00 (20 + 10% tax).</summary>
public class OnlineSaleTests
{
    private static CheckoutRequest Request(TestDatabase db, int variantId, SalesChannel channel, decimal fee, PaymentMethod method, decimal paid) => new()
    {
        UserId = db.Cashier.Id,
        CustomerId = channel == SalesChannel.InStore ? null : db.ShopperIdAsync().GetAwaiter().GetResult(),
        Lines = [new CheckoutLine(variantId, 1)],
        Payments = [new PaymentInput(method, paid)],
        Channel = channel,
        DeliveryFee = fee,
        Notes = channel == SalesChannel.InStore ? null : "Hamra, Beirut · @rana",
    };

    [Fact]
    public async Task An_online_order_is_a_sale_with_its_channel_and_delivery_fee_and_can_be_filtered()
    {
        await using var db = await TestDatabase.CreateAsync();
        var tee = await db.CreateTeeAsync();
        var shift = await db.OpenShiftAsync();

        var online = await db.Sales.CompleteSaleAsync(Request(db, tee.Variants[0].Id, SalesChannel.Instagram, 3m, PaymentMethod.Cash, 30m) with { ShiftId = shift.Id });
        var store = await db.Sales.CompleteSaleAsync(Request(db, tee.Variants[1].Id, SalesChannel.InStore, 0m, PaymentMethod.Card, 22m));

        Assert.Equal((SalesChannel.Instagram, 3m, 25m, 5m), (online.Channel, online.DeliveryFee, online.Total, online.ChangeGiven));
        Assert.Equal("Hamra, Beirut · @rana", online.Notes);
        Assert.Equal((SalesChannel.InStore, 22m), (store.Channel, store.Total));

        var today = DateTime.Today;
        Assert.Equal(2, (await db.Sales.SearchAsync(today, today.AddDays(1))).Count);
        Assert.Equal(online.Id, Assert.Single(await db.Sales.SearchAsync(today, today.AddDays(1), online: true)).Id);
        Assert.Equal(store.Id, Assert.Single(await db.Sales.SearchAsync(today, today.AddDays(1), online: false)).Id);

        var report = await db.Reports.GetSalesReportAsync(today, today.AddDays(1));
        Assert.Equal(3m, report.DeliveryFees);
        Assert.Equal(2, report.ByChannel.Count);
    }

    [Fact]
    public async Task Delivery_fees_only_go_on_online_orders_and_are_never_negative()
    {
        await using var db = await TestDatabase.CreateAsync();
        var tee = await db.CreateTeeAsync();
        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            db.Sales.CompleteSaleAsync(Request(db, tee.Variants[0].Id, SalesChannel.InStore, 2m, PaymentMethod.Card, 24m)));
        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            db.Sales.CompleteSaleAsync(Request(db, tee.Variants[0].Id, SalesChannel.WhatsApp, -1m, PaymentMethod.Card, 21m)));
    }

    [Fact]
    public async Task An_online_order_needs_a_customer()
    {
        await using var db = await TestDatabase.CreateAsync();
        var tee = await db.CreateTeeAsync();
        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            db.Sales.CompleteSaleAsync(Request(db, tee.Variants[0].Id, SalesChannel.WhatsApp, 0m, PaymentMethod.Card, 22m) with { CustomerId = null }));
        Assert.Contains("customer", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_held_online_order_comes_back_as_an_online_order()
    {
        await using var db = await TestDatabase.CreateAsync();
        var tee = await db.CreateTeeAsync();
        var held = await db.Sales.HoldAsync("Rana", db.Cashier.Id,
            new HeldCart([new HeldCartLine(tee.Variants[0].Id, 2, DiscountType.None, 0)], DiscountType.None, 0, null, SalesChannel.WhatsApp, 4m, "Achrafieh"));

        var cart = await db.Sales.ResumeAsync(held.Id);
        Assert.Equal((SalesChannel.WhatsApp, 4m, "Achrafieh"), (cart.Channel, cart.DeliveryFee, cart.Notes));
    }

    [Fact]
    public async Task Upgrading_puts_back_stock_held_by_unfinished_orders_from_the_old_screen()
    {
        await using var db = await TestDatabase.CreateAsync();
        var tee = await db.CreateTeeAsync(stockEach: 10);
        var variant = tee.Variants[0];

        await using var ctx = await db.Factory.CreateDbContextAsync();
        var migrator = ctx.GetService<IMigrator>();
        await migrator.MigrateAsync("AddAppState"); // back to the version that had the online-orders screen

        // A confirmed order holding 3 tees, and a completed one (its stock already became a sale).
        await ctx.Database.ExecuteSqlRawAsync($"""
            UPDATE ProductVariants SET StockQuantity = StockQuantity - 3 WHERE Id = {variant.Id};
            INSERT OnlineOrders (OrderNumber, CreatedAt, CreatedByUserId, Channel, Status, CustomerName, DiscountType, DiscountValue,
                                 Subtotal, DiscountTotal, TaxTotal, ItemsTotal, DeliveryFee, Total, StockHeld)
            VALUES (N'OL20261006-0001', SYSDATETIME(), {db.Cashier.Id}, 1, 1, N'Rana', 0, 0, 60, 0, 6, 66, 0, 66, 1),
                   (N'OL20261006-0002', SYSDATETIME(), {db.Cashier.Id}, 2, 3, N'Maya', 0, 0, 20, 0, 2, 22, 0, 22, 0);
            INSERT OnlineOrderLines (OnlineOrderId, ProductVariantId, ProductName, VariantDescription, Sku, UnitPrice, UnitCost, Quantity,
                                     DiscountAmount, TaxAmount, LineTotal)
            SELECT Id, {variant.Id}, N'Basic Tee', N'S / Black', N'X', 20, 8, CASE WHEN StockHeld = 1 THEN 3 ELSE 1 END, 0, 0, 0 FROM OnlineOrders;
            """);

        await migrator.MigrateAsync(); // this version

        Assert.Equal(10, (await db.GetVariantAsync(variant.Id)).StockQuantity);
        var movement = await ctx.StockMovements.AsNoTracking()
            .Where(m => m.ProductVariantId == variant.Id && m.Type == StockMovementType.OnlineOrderCancelled).SingleAsync();
        Assert.Equal((3, 10, "OL20261006-0001"), (movement.QuantityChange, movement.QuantityAfter, movement.Reference));
    }
}
