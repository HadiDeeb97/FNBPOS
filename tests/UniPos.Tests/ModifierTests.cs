using UniPos.Core;
using UniPos.Core.Entities;
using UniPos.Core.Menu;
using UniPos.Data.Services;
using Microsoft.EntityFrameworkCore;

namespace UniPos.Tests;

/// <summary>The options asked when an item is ordered, checked the same way at the till and at checkout.</summary>
public class ModifierRulesTests
{
    private static int _ids;

    private static ModifierGroup Group(string name, int min, int max, params (string Name, decimal Price)[] options)
    {
        var id = ++_ids;
        return new ModifierGroup
        {
            Id = id, Name = name, MinChoices = min, MaxChoices = max,
            Options = options.Select((o, i) => new Modifier { Id = id * 100 + i, ModifierGroupId = id, Name = o.Name, Price = o.Price, SortOrder = i }).ToList(),
        };
    }

    /// <summary>A crepe asking fillings (1 to 3) and then extras (any).</summary>
    private static (Product Crepe, ModifierGroup Fillings, ModifierGroup Extras) Crepe()
    {
        var fillings = Group("Fillings", 1, 3, ("Nutella", 0m), ("Banana", 0.5m), ("Lotus", 0.5m), ("Kinder", 1m));
        var extras = Group("Extras", 0, 0, ("Whipped cream", 0.25m), ("Nuts", 0.5m));
        var crepe = new Product
        {
            Name = "Build your own crepe", Price = 3.5m, Cost = 1m,
            ModifierGroups =
            [
                new ProductModifierGroup { ModifierGroup = extras, ModifierGroupId = extras.Id, SortOrder = 1 },
                new ProductModifierGroup { ModifierGroup = fillings, ModifierGroupId = fillings.Id, SortOrder = 0 },
            ],
        };
        return (crepe, fillings, extras);
    }

    [Fact]
    public void A_required_group_must_be_answered_and_a_limit_kept()
    {
        var (crepe, fillings, extras) = Crepe();
        Assert.Contains("Fillings", ModifierRules.Problem(crepe, []));
        Assert.Contains("Fillings", ModifierRules.Problem(crepe, fillings.Options.Select(o => o.Id).ToList())); // 4 of at most 3
        Assert.Null(ModifierRules.Problem(crepe, [fillings.Options[0].Id]));
        Assert.Null(ModifierRules.Problem(crepe, [fillings.Options[0].Id, .. extras.Options.Select(o => o.Id)])); // extras: any number
    }

    [Fact]
    public void Options_sold_out_switched_off_or_from_another_item_are_refused()
    {
        var (crepe, fillings, _) = Crepe();
        var (_, otherFillings, _) = Crepe();
        Assert.NotNull(ModifierRules.Problem(crepe, [fillings.Options[0].Id, fillings.Options[0].Id]));
        Assert.NotNull(ModifierRules.Problem(crepe, [otherFillings.Options[0].Id]));

        fillings.Options[1].SoldOut = true;
        Assert.Contains("Banana", ModifierRules.Problem(crepe, [fillings.Options[1].Id]));
        fillings.Options[2].IsActive = false;
        Assert.NotNull(ModifierRules.Problem(crepe, [fillings.Options[2].Id]));
    }

    [Fact]
    public void A_group_switched_off_is_not_asked()
    {
        var (crepe, fillings, _) = Crepe();
        fillings.IsActive = false;
        Assert.Null(ModifierRules.Problem(crepe, [])); // its "pick 1" no longer applies
        Assert.Equal(["Extras"], ModifierRules.ActiveGroups(crepe).Select(g => g.Name));
    }

    [Fact]
    public void The_options_are_priced_and_described_in_the_items_order()
    {
        var (crepe, fillings, extras) = Crepe();
        int[] picked = [extras.Options[1].Id, fillings.Options[3].Id, fillings.Options[1].Id];

        var ordered = ModifierRules.Ordered(crepe, picked);
        Assert.Equal("Banana, Kinder · Nuts", ModifierRules.Describe(ordered)); // fillings first (the item's order), then each group's
        Assert.Equal(5.50m, ModifierRules.Price(crepe, ordered));              // 3.50 + 0.50 + 1.00 + 0.50
    }
}

public class ModifierCheckoutTests
{
    /// <summary>A latte asking a size (required) and sugar (optional), the way the demo menu has it.</summary>
    private static async Task<(TestDatabase Db, Product Latte, ModifierGroup Size, ModifierGroup Sugar)> SetUpAsync()
    {
        var db = await TestDatabase.CreateAsync();
        var size = await db.Modifiers.SaveGroupAsync(new ModifierGroup
        {
            Name = "Size", MinChoices = 1, MaxChoices = 1,
            Options = [new() { Name = "Small" }, new() { Name = "Large", Price = 1m, Cost = 0.3m }],
        });
        var sugar = await db.Modifiers.SaveGroupAsync(new ModifierGroup
        {
            Name = "Sugar", MinChoices = 0, MaxChoices = 1, Options = [new() { Name = "No sugar" }, new() { Name = "Extra sugar" }],
        });
        var category = await db.Categories.SaveAsync(new Category { Name = "Hot drinks" });
        var latte = await db.Products.SaveAsync(new Product
        {
            Name = "Latte", CategoryId = category.Id, Price = 3.25m, Cost = 0.75m,
            ModifierGroups = [new() { ModifierGroupId = size.Id }, new() { ModifierGroupId = sugar.Id }],
        }, db.Admin.Id);
        return (db, latte, size, sugar);
    }

    private static CheckoutRequest Order(TestDatabase db, params CheckoutLine[] lines) => new()
    {
        UserId = db.Cashier.Id, Lines = lines, Payments = [new PaymentInput(PaymentMethod.Cash, 50m)],
        ExchangeRate = 89_500m, // the cents of the change come back in pounds, at the rate shown
    };

    [Fact]
    public async Task Options_are_priced_on_the_server_and_kept_with_the_sale()
    {
        var (db, latte, size, sugar) = await SetUpAsync();
        await using var _ = db;
        var large = size.Options.Single(o => o.Name == "Large");
        var noSugar = sugar.Options.Single(o => o.Name == "No sugar");

        var sale = await db.Sales.CompleteSaleAsync(Order(db, new CheckoutLine(latte.Id, 2, Options: [noSugar.Id, large.Id])));

        var line = Assert.Single(sale.Lines);
        Assert.Equal(("Large · No sugar", 4.25m, 1.05m, (decimal?)null), (line.Options, line.UnitPrice, line.UnitCost, line.ListPrice));
        Assert.Equal(9.35m, sale.Total); // 2 × 4.25 + 10% tax
        await using var ctx = await db.Factory.CreateDbContextAsync();
        var saved = await ctx.SaleLineModifiers.Where(m => m.SaleLineId == line.Id).OrderBy(m => m.Id).ToListAsync();
        Assert.Equal([("Size", "Large", 1m), ("Sugar", "No sugar", 0m)], saved.Select(m => (m.GroupName, m.Name, m.Price)));
    }

    [Fact]
    public async Task A_missing_size_or_a_sold_out_item_or_option_is_refused()
    {
        var (db, latte, size, _) = await SetUpAsync();
        await using var _ = db;
        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() => db.Sales.CompleteSaleAsync(Order(db, new CheckoutLine(latte.Id, 1))));
        Assert.Contains("Size", ex.Message);

        var large = size.Options.Single(o => o.Name == "Large");
        size.Options.Single(o => o.Id == large.Id).SoldOut = true;
        await db.Modifiers.SaveGroupAsync(size);
        await Assert.ThrowsAsync<BusinessRuleException>(() => db.Sales.CompleteSaleAsync(Order(db, new CheckoutLine(latte.Id, 1, Options: [large.Id]))));

        await db.Products.SetSoldOutAsync(latte.Id, true);
        var small = size.Options.Single(o => o.Name == "Small");
        ex = await Assert.ThrowsAsync<BusinessRuleException>(() => db.Sales.CompleteSaleAsync(Order(db, new CheckoutLine(latte.Id, 1, Options: [small.Id]))));
        Assert.Contains("sold out", ex.Message);
    }

    [Fact]
    public async Task A_typed_price_replaces_the_price_with_options()
    {
        var (db, latte, size, _) = await SetUpAsync();
        await using var _ = db;
        var large = size.Options.Single(o => o.Name == "Large");

        var sale = await db.Sales.CompleteSaleAsync(Order(db, new CheckoutLine(latte.Id, 1, UnitPrice: 5m, Options: [large.Id])) with { UserId = db.Manager.Id });

        var line = Assert.Single(sale.Lines);
        Assert.Equal((5m, (decimal?)4.25m), (line.UnitPrice, line.ListPrice));
    }

    [Fact]
    public async Task The_report_counts_each_option_and_what_it_added()
    {
        var (db, latte, size, _) = await SetUpAsync();
        await using var _ = db;
        var (small, large) = (size.Options.Single(o => o.Name == "Small"), size.Options.Single(o => o.Name == "Large"));
        await db.Sales.CompleteSaleAsync(Order(db, new CheckoutLine(latte.Id, 3, Options: [large.Id]), new CheckoutLine(latte.Id, 1, Options: [small.Id])));

        var report = await db.Reports.GetSalesReportAsync(DateTime.Today, DateTime.Today.AddDays(1));
        Assert.Equal([("Size: Large", 3, 3m), ("Size: Small", 1, 0m)], report.ByOption.Select(o => (o.Name, o.Quantity, o.Amount)));
    }

    [Fact]
    public async Task Removing_an_option_or_group_keeps_past_sales_and_stops_asking()
    {
        var (db, latte, size, sugar) = await SetUpAsync();
        await using var _ = db;
        var large = size.Options.Single(o => o.Name == "Large");
        var sale = await db.Sales.CompleteSaleAsync(Order(db, new CheckoutLine(latte.Id, 1, Options: [large.Id])));

        size.Options.RemoveAll(o => o.Id == large.Id);
        await db.Modifiers.SaveGroupAsync(size);
        await db.Modifiers.DeleteGroupAsync(sugar.Id);

        Assert.Equal("Large", Assert.Single((await db.Sales.GetAsync(sale.Id))!.Lines).Options);
        await using var ctx = await db.Factory.CreateDbContextAsync();
        Assert.Null((await ctx.SaleLineModifiers.SingleAsync()).ModifierId); // kept by name
        var item = (await db.Products.GetAsync(latte.Id))!;
        Assert.Equal(["Size"], ModifierRules.ActiveGroups(item).Select(g => g.Name));
    }

    [Fact]
    public async Task A_group_must_make_sense_to_be_saved()
    {
        await using var db = await TestDatabase.CreateAsync();
        Modifier[] two = [new() { Name = "A" }, new() { Name = "B" }];
        await Assert.ThrowsAsync<BusinessRuleException>(() => db.Modifiers.SaveGroupAsync(new ModifierGroup { Name = " ", Options = [.. two] }));
        await Assert.ThrowsAsync<BusinessRuleException>(() => db.Modifiers.SaveGroupAsync(new ModifierGroup { Name = "Empty" }));
        await Assert.ThrowsAsync<BusinessRuleException>(() => db.Modifiers.SaveGroupAsync(new ModifierGroup { Name = "X", MinChoices = 2, MaxChoices = 1, Options = [.. two] }));
        await Assert.ThrowsAsync<BusinessRuleException>(() => db.Modifiers.SaveGroupAsync(new ModifierGroup { Name = "X", MinChoices = 3, MaxChoices = 0, Options = [.. two] }));
        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            db.Modifiers.SaveGroupAsync(new ModifierGroup { Name = "X", Options = [new() { Name = "Oat" }, new() { Name = "oat" }] }));

        await db.Modifiers.SaveGroupAsync(new ModifierGroup { Name = "Milk", Options = [.. two] });
        await Assert.ThrowsAsync<BusinessRuleException>(() => db.Modifiers.SaveGroupAsync(new ModifierGroup { Name = "MILK", Options = [new() { Name = "C" }] }));
    }
}
