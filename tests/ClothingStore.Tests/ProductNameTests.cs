using ClothingStore.Core;
using ClothingStore.Core.Entities;

namespace ClothingStore.Tests;

/// <summary>Every product has its own name.</summary>
public class ProductNameTests
{
    [Fact]
    public async Task Two_products_cannot_have_the_same_name()
    {
        await using var db = await TestDatabase.CreateAsync();
        var category = await db.Categories.SaveAsync(new Category { Name = "Tops" });
        Product Make(string name) => new() { Name = name, CategoryId = category.Id, Price = 10, Variants = [new ProductVariant()] };

        var first = await db.Products.SaveAsync(Make("Abaya Butterfly"), db.Admin.Id);

        await Assert.ThrowsAsync<BusinessRuleException>(() => db.Products.SaveAsync(Make(" abaya butterfly "), db.Admin.Id));
        await db.Products.SaveAsync(first, db.Admin.Id); // saving the same product again is fine
        await db.Products.SaveAsync(Make("Abaya Butterfly 2"), db.Admin.Id);
    }

    [Fact]
    public async Task A_deactivated_products_name_is_still_taken()
    {
        await using var db = await TestDatabase.CreateAsync();
        var category = await db.Categories.SaveAsync(new Category { Name = "Tops" });
        var old = await db.Products.SaveAsync(new Product { Name = "Linen Shirt", CategoryId = category.Id, Price = 10, Variants = [new ProductVariant()] }, db.Admin.Id);
        old.IsActive = false;
        await db.Products.SaveAsync(old, db.Admin.Id);

        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            db.Products.SaveAsync(new Product { Name = "LINEN SHIRT", CategoryId = category.Id, Price = 12, Variants = [new ProductVariant()] }, db.Admin.Id));
    }
}
