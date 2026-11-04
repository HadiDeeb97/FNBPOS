using UniPos.Core;
using UniPos.Core.Entities;
using UniPos.Data.Services;

namespace UniPos.Tests;

public class InventoryAndPurchasingTests
{
    [Fact]
    public async Task A_new_counted_item_gets_the_next_code_and_its_opening_stock()
    {
        await using var db = await TestDatabase.CreateAsync();
        var tee = await db.CreateTeeAsync(stockEach: 4);

        Assert.Equal(["101", "102", "103"], tee.Items.Select(i => i.Sku));
        Assert.All(tee.Items, i => Assert.Equal(4, i.StockQuantity));
        var movements = await db.Inventory.GetMovementsAsync(tee.Items[0].Id);
        Assert.Equal(StockMovementType.InitialStock, Assert.Single(movements).Type);

        // A code typed for another item ("200") moves the next free one on.
        var category = tee.Items[0].CategoryId;
        await db.Products.SaveAsync(new Product { Name = "Espresso", CategoryId = category, Price = 2, Sku = "200" }, db.Admin.Id);
        Assert.Equal("201", (await db.Products.SaveAsync(new Product { Name = "Tea", CategoryId = category, Price = 1.5m }, db.Admin.Id)).Sku);
    }

    [Fact]
    public async Task An_item_made_to_order_has_no_stock_to_run_out_of()
    {
        await using var db = await TestDatabase.CreateAsync();
        var category = await db.Categories.SaveAsync(new Category { Name = "Crepes" });
        var crepe = await db.Products.SaveAsync(new Product { Name = "Nutella crepe", CategoryId = category.Id, Price = 4.5m, StockQuantity = 7 }, db.Admin.Id);
        Assert.Equal(0, crepe.StockQuantity); // opening stock is only for counted items

        await db.Sales.CompleteSaleAsync(new CheckoutRequest
        {
            UserId = db.Cashier.Id, Lines = [new CheckoutLine(crepe.Id, 3)], Payments = [new PaymentInput(PaymentMethod.Card, 14.85m)],
        });
        Assert.Empty(await db.Inventory.GetMovementsAsync(crepe.Id));
        Assert.DoesNotContain(await db.Inventory.GetStockAsync(), i => i.Id == crepe.Id);
        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            db.Inventory.AdjustStockAsync(crepe.Id, 5, StockMovementType.Adjustment, null, db.Manager.Id));
    }

    [Fact]
    public async Task Duplicate_code_and_barcode_are_rejected()
    {
        await using var db = await TestDatabase.CreateAsync();
        var tee = await db.CreateTeeAsync();
        var first = tee.Items[0];
        await db.Products.SaveAsync(new Product { Name = "Water", CategoryId = first.CategoryId, Price = 1, Barcode = "5281006400016" }, db.Admin.Id);

        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            db.Products.SaveAsync(new Product { Name = "Other", CategoryId = first.CategoryId, Price = 5, Sku = first.Sku }, db.Admin.Id));
        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            db.Products.SaveAsync(new Product { Name = "Other", CategoryId = first.CategoryId, Price = 5, Barcode = "5281006400016" }, db.Admin.Id));
    }

    [Fact]
    public async Task Editing_an_item_keeps_its_stock()
    {
        await using var db = await TestDatabase.CreateAsync();
        var item = (await db.CreateTeeAsync(stockEach: 3)).Items[0];

        item.Price = 25m;
        item.StockQuantity = 999; // ignored: stock changes on the stock screen
        var saved = await db.Products.SaveAsync(item, db.Admin.Id);

        Assert.Equal((25m, 3), (saved.Price, saved.StockQuantity));
    }

    [Fact]
    public async Task Stock_adjustments_and_counts()
    {
        await using var db = await TestDatabase.CreateAsync();
        var v = (await db.CreateTeeAsync(stockEach: 5)).Items[0];

        await db.Inventory.AdjustStockAsync(v.Id, -2, StockMovementType.Damaged, "Stained", db.Manager.Id);
        await Assert.ThrowsAsync<BusinessRuleException>(() => db.Inventory.AdjustStockAsync(v.Id, -10, StockMovementType.Adjustment, null, db.Manager.Id));
        await db.Inventory.SetCountedStockAsync(v.Id, 7, "Cycle count", db.Manager.Id);

        Assert.Equal(7, (await db.GetItemAsync(v.Id)).StockQuantity);
        var movements = await db.Inventory.GetMovementsAsync(v.Id);
        Assert.Equal(new[] { 5, -2, 4 }, movements.Select(m => m.QuantityChange).Reverse().ToArray());
        Assert.Equal(7, movements.First().QuantityAfter);
        Assert.Equal(StockMovementType.StockCount, movements.First().Type);

        var low = await db.Inventory.GetStockAsync(lowStockOnly: true);
        Assert.DoesNotContain(low, x => x.Id == v.Id);
    }

    [Fact]
    public async Task Purchase_order_partial_then_full_receipt()
    {
        await using var db = await TestDatabase.CreateAsync();
        var tee = await db.CreateTeeAsync(stockEach: 0);
        var supplier = await db.Suppliers.SaveAsync(new Supplier { Name = "Acme Apparel" });

        var po = await db.PurchaseOrders.SaveAsync(new PurchaseOrder
        {
            SupplierId = supplier.Id,
            Lines =
            [
                new PurchaseOrderLine { ProductId = tee.Items[0].Id, QuantityOrdered = 10, UnitCost = 7.5m },
                new PurchaseOrderLine { ProductId = tee.Items[1].Id, QuantityOrdered = 5, UnitCost = 8m },
            ],
        }, db.Manager.Id);
        Assert.Equal(PurchaseOrderStatus.Draft, po.Status);
        Assert.Equal(115m, po.Total);
        await db.PurchaseOrders.MarkOrderedAsync(po.Id);

        var line1 = po.Lines.Single(l => l.ProductId == tee.Items[0].Id);
        var line2 = po.Lines.Single(l => l.ProductId == tee.Items[1].Id);

        po = await db.PurchaseOrders.ReceiveAsync(po.Id, new Dictionary<int, int> { [line1.Id] = 6 }, db.Manager.Id);
        Assert.Equal(PurchaseOrderStatus.PartiallyReceived, po.Status);

        po = await db.PurchaseOrders.ReceiveAsync(po.Id, new Dictionary<int, int> { [line1.Id] = 4, [line2.Id] = 5 }, db.Manager.Id);
        Assert.Equal(PurchaseOrderStatus.Received, po.Status);

        var v0 = await db.GetItemAsync(tee.Items[0].Id);
        Assert.Equal(10, v0.StockQuantity);
        Assert.Equal(7.5m, v0.Cost); // the supplier's new cost
        var v1 = await db.GetItemAsync(tee.Items[1].Id);
        Assert.Equal(5, v1.StockQuantity);
        Assert.Equal(8m, v1.Cost);

        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            db.PurchaseOrders.ReceiveAsync(po.Id, new Dictionary<int, int> { [line1.Id] = 1 }, db.Manager.Id));
    }
}
