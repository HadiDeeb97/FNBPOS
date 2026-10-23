using ClothingStore.Core;
using ClothingStore.Core.Entities;
using ClothingStore.Data.Services;

namespace ClothingStore.Tests;

public class InventoryAndPurchasingTests
{
    [Fact]
    public async Task New_product_gets_skus_barcodes_and_opening_stock_movements()
    {
        await using var db = await TestDatabase.CreateAsync();
        var tee = await db.CreateTeeAsync(stockEach: 4);

        Assert.All(tee.Variants, v =>
        {
            Assert.False(string.IsNullOrEmpty(v.Sku));
            Assert.True(Core.Barcodes.Ean13.IsValid(v.Barcode));
            Assert.Equal(4, v.StockQuantity);
        });
        Assert.Equal(3, tee.Variants.Select(v => v.Barcode).Distinct().Count());
        Assert.Contains(tee.Variants, v => v.Sku == "BT-M-BLAC" || v.Sku == "BT-M-BLA");

        var movements = await db.Inventory.GetMovementsAsync(tee.Variants[0].Id);
        Assert.Equal(StockMovementType.InitialStock, Assert.Single(movements).Type);
    }

    [Fact]
    public async Task Duplicate_sku_is_rejected()
    {
        await using var db = await TestDatabase.CreateAsync();
        var tee = await db.CreateTeeAsync();
        var other = new Product
        {
            Name = "Other",
            CategoryId = tee.CategoryId,
            Price = 5,
            Variants = [new ProductVariant { Size = "S", Sku = tee.Variants[0].Sku }],
        };
        await Assert.ThrowsAsync<BusinessRuleException>(() => db.Products.SaveAsync(other, db.Admin.Id));
    }

    [Fact]
    public async Task Editing_a_product_keeps_stock_and_retires_sold_variants()
    {
        await using var db = await TestDatabase.CreateAsync();
        var tee = await db.CreateTeeAsync(stockEach: 3);
        var small = tee.Variants.Single(v => v.Size == "S");
        await db.Sales.CompleteSaleAsync(new CheckoutRequest
        {
            UserId = db.Admin.Id, Lines = [new CheckoutLine(small.Id, 1)], Payments = [new PaymentInput(PaymentMethod.Cash, 50m)],
        });

        tee.Price = 25m;
        tee.Variants.Remove(tee.Variants.Single(v => v.Id == small.Id));          // sold -> deactivated
        tee.Variants.Remove(tee.Variants.Single(v => v.Size == "L"));              // never sold but 3 in stock -> deactivated, not lost
        tee.Variants.Add(new ProductVariant { Size = "XL", Color = "Black", StockQuantity = 2 });
        tee.Variants.Single(v => v.Size == "M").StockQuantity = 999;               // ignored for existing variants

        var saved = await db.Products.SaveAsync(tee, db.Admin.Id);

        Assert.Equal(25m, saved.Price);
        Assert.Equal(4, saved.Variants.Count);
        Assert.False(saved.Variants.Single(v => v.Size == "S").IsActive);
        Assert.False(saved.Variants.Single(v => v.Size == "L").IsActive);
        Assert.Equal(3, saved.Variants.Single(v => v.Size == "M").StockQuantity);
        Assert.Equal(2, saved.Variants.Single(v => v.Size == "XL").StockQuantity);
    }

    [Fact]
    public async Task Stock_adjustments_and_counts()
    {
        await using var db = await TestDatabase.CreateAsync();
        var v = (await db.CreateTeeAsync(stockEach: 5)).Variants[0];

        await db.Inventory.AdjustStockAsync(v.Id, -2, StockMovementType.Damaged, "Stained", db.Manager.Id);
        await Assert.ThrowsAsync<BusinessRuleException>(() => db.Inventory.AdjustStockAsync(v.Id, -10, StockMovementType.Adjustment, null, db.Manager.Id));
        await db.Inventory.SetCountedStockAsync(v.Id, 7, "Cycle count", db.Manager.Id);

        Assert.Equal(7, (await db.GetVariantAsync(v.Id)).StockQuantity);
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
                new PurchaseOrderLine { ProductVariantId = tee.Variants[0].Id, QuantityOrdered = 10, UnitCost = 7.5m },
                new PurchaseOrderLine { ProductVariantId = tee.Variants[1].Id, QuantityOrdered = 5, UnitCost = 8m },
            ],
        }, db.Manager.Id);
        Assert.Equal(PurchaseOrderStatus.Draft, po.Status);
        Assert.Equal(115m, po.Total);
        await db.PurchaseOrders.MarkOrderedAsync(po.Id);

        var line1 = po.Lines.Single(l => l.ProductVariantId == tee.Variants[0].Id);
        var line2 = po.Lines.Single(l => l.ProductVariantId == tee.Variants[1].Id);

        po = await db.PurchaseOrders.ReceiveAsync(po.Id, new Dictionary<int, int> { [line1.Id] = 6 }, db.Manager.Id);
        Assert.Equal(PurchaseOrderStatus.PartiallyReceived, po.Status);

        po = await db.PurchaseOrders.ReceiveAsync(po.Id, new Dictionary<int, int> { [line1.Id] = 4, [line2.Id] = 5 }, db.Manager.Id);
        Assert.Equal(PurchaseOrderStatus.Received, po.Status);

        var v0 = await db.GetVariantAsync(tee.Variants[0].Id);
        Assert.Equal(10, v0.StockQuantity);
        Assert.Equal(7.5m, v0.CostOverride); // differs from product cost 8 -> override
        var v1 = await db.GetVariantAsync(tee.Variants[1].Id);
        Assert.Equal(5, v1.StockQuantity);
        Assert.Null(v1.CostOverride); // same as product cost

        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            db.PurchaseOrders.ReceiveAsync(po.Id, new Dictionary<int, int> { [line1.Id] = 1 }, db.Manager.Id));
    }
}
