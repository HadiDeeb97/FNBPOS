using UniPos.Core;
using UniPos.Core.Entities;
using UniPos.Core.Features;
using UniPos.Core.Receipts;
using UniPos.Data.Services;
using Microsoft.EntityFrameworkCore;

namespace UniPos.Tests;

/// <summary>Counter service: order numbers, eat in or takeaway, item notes and the tickets each station prints.</summary>
public class KitchenTests
{
    /// <summary>A crepe made in the kitchen, a lemonade at the bar and a can of soda handed over as it is.</summary>
    private static async Task<(TestDatabase Db, Product Crepe, Product Lemonade, Product Soda)> SetUpAsync()
    {
        var db = await TestDatabase.CreateAsync();
        var kitchen = await db.Kitchen.SaveStationAsync(new Station { Name = "Kitchen", PrinterName = "Kitchen printer" });
        var bar = await db.Kitchen.SaveStationAsync(new Station { Name = "Bar" });
        var category = await db.Categories.SaveAsync(new Category { Name = "Menu" });
        var crepe = await db.Products.SaveAsync(new Product { Name = "Nutella crepe", CategoryId = category.Id, Price = 4m, StationId = kitchen.Id }, db.Admin.Id);
        var lemonade = await db.Products.SaveAsync(new Product { Name = "Lemonade", CategoryId = category.Id, Price = 2m, StationId = bar.Id }, db.Admin.Id);
        var soda = await db.Products.SaveAsync(new Product { Name = "Soda can", CategoryId = category.Id, Price = 1m }, db.Admin.Id);
        return (db, crepe, lemonade, soda);
    }

    private static CheckoutRequest Order(TestDatabase db, params CheckoutLine[] lines) => new()
    {
        UserId = db.Cashier.Id, Lines = lines, Payments = [new PaymentInput(PaymentMethod.Cash, 50m)], ExchangeRate = 89_500m,
    };

    private static Task SwitchOffAsync(TestDatabase db, Feature feature) =>
        db.Features.SaveAsync(new FeatureChoices(Venue.CounterService, new Dictionary<Feature, bool?> { [feature] = false }), db.Admin.Id);

    [Fact]
    public async Task Orders_are_numbered_from_1_each_day()
    {
        var (db, crepe, _, _) = await SetUpAsync();
        await using var _ = db;
        var numbers = new List<int?>();
        for (var i = 0; i < 3; i++) numbers.Add((await db.Sales.CompleteSaleAsync(Order(db, new CheckoutLine(crepe.Id, 1)))).OrderNumber);
        Assert.Equal([1, 2, 3], numbers);

        // Yesterday's orders don't count.
        await using (var ctx = await db.Factory.CreateDbContextAsync())
            await ctx.Sales.ExecuteUpdateAsync(s => s.SetProperty(x => x.OrderDay, DateTime.Today.AddDays(-1)));
        Assert.Equal(1, (await db.Sales.CompleteSaleAsync(Order(db, new CheckoutLine(crepe.Id, 1)))).OrderNumber);
    }

    [Fact]
    public async Task No_order_number_when_switched_off()
    {
        var (db, crepe, _, _) = await SetUpAsync();
        await using var _ = db;
        await SwitchOffAsync(db, Feature.OrderNumbers);
        var sale = await db.Sales.CompleteSaleAsync(Order(db, new CheckoutLine(crepe.Id, 1)));
        Assert.Equal(((int?)null, (DateTime?)null), (sale.OrderNumber, sale.OrderDay));
    }

    [Fact]
    public async Task Takeaway_and_the_call_name_are_kept_and_takeaway_is_ignored_when_switched_off()
    {
        var (db, crepe, _, _) = await SetUpAsync();
        await using var _ = db;
        var sale = await db.Sales.CompleteSaleAsync(Order(db, new CheckoutLine(crepe.Id, 1)) with { OrderType = OrderType.Takeaway, CallName = "  Rami " });
        Assert.Equal((OrderType.Takeaway, "Rami"), (sale.OrderType, sale.CallName));

        await SwitchOffAsync(db, Feature.Takeaway);
        sale = await db.Sales.CompleteSaleAsync(Order(db, new CheckoutLine(crepe.Id, 1)) with { OrderType = OrderType.Takeaway });
        Assert.Equal(OrderType.EatIn, sale.OrderType);

        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            db.Sales.CompleteSaleAsync(Order(db, new CheckoutLine(crepe.Id, 1)) with { CallName = new string('x', 61) }));
        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            db.Sales.CompleteSaleAsync(Order(db, new CheckoutLine(crepe.Id, 1, Note: new string('x', 201)))));
    }

    [Fact]
    public async Task Each_station_gets_a_ticket_with_its_own_items_and_notes()
    {
        var (db, crepe, lemonade, soda) = await SetUpAsync();
        await using var _ = db;
        var sale = await db.Sales.CompleteSaleAsync(Order(db,
            new CheckoutLine(crepe.Id, 2, Note: "no nuts"), new CheckoutLine(lemonade.Id, 1), new CheckoutLine(soda.Id, 3))
            with { OrderType = OrderType.Takeaway, CallName = "Rami" });

        Assert.Equal("no nuts", sale.Lines.Single(l => l.ProductId == crepe.Id).Note);
        var tickets = await db.Kitchen.GetForSaleAsync(sale.Id);
        Assert.Equal(["Bar", "Kitchen"], tickets.Select(t => t.Station).Order());
        var kitchen = tickets.Single(t => t.Station == "Kitchen");
        Assert.Equal(("Kitchen printer", sale.ReceiptNumber, sale.OrderNumber, OrderType.Takeaway, "Rami"),
            (kitchen.StationPrinter, kitchen.ReceiptNumber, kitchen.Ticket.OrderNumber, kitchen.Ticket.OrderType, kitchen.Ticket.CallName));
        var line = Assert.Single(kitchen.Ticket.Lines);
        Assert.Equal(("Nutella crepe", 2, "no nuts"), (line.ProductName, line.Quantity, line.Note));
        Assert.Equal("Lemonade", Assert.Single(tickets.Single(t => t.Station == "Bar").Ticket.Lines).ProductName);
    }

    [Fact]
    public async Task No_tickets_for_ready_made_items_a_station_switched_off_or_the_feature_off()
    {
        var (db, crepe, lemonade, soda) = await SetUpAsync();
        await using var _ = db;
        var sale = await db.Sales.CompleteSaleAsync(Order(db, new CheckoutLine(soda.Id, 1)));
        Assert.Empty(await db.Kitchen.GetForSaleAsync(sale.Id));

        var bar = (await db.Kitchen.GetStationsAsync()).Single(s => s.Name == "Bar");
        bar.IsActive = false;
        await db.Kitchen.SaveStationAsync(bar);
        sale = await db.Sales.CompleteSaleAsync(Order(db, new CheckoutLine(lemonade.Id, 1)));
        Assert.Empty(await db.Kitchen.GetForSaleAsync(sale.Id));

        await SwitchOffAsync(db, Feature.KitchenTickets);
        sale = await db.Sales.CompleteSaleAsync(Order(db, new CheckoutLine(crepe.Id, 1)));
        Assert.Empty(await db.Kitchen.GetForSaleAsync(sale.Id));
    }

    [Fact]
    public async Task A_ticket_that_failed_to_print_waits_until_it_prints()
    {
        var (db, crepe, lemonade, _) = await SetUpAsync();
        await using var _ = db;
        var sale = await db.Sales.CompleteSaleAsync(Order(db, new CheckoutLine(crepe.Id, 1), new CheckoutLine(lemonade.Id, 1)));
        var tickets = await db.Kitchen.GetForSaleAsync(sale.Id);
        Assert.Equal(2, await db.Kitchen.CountNotPrintedAsync());

        await db.Kitchen.RecordPrintAsync(tickets[0].Ticket.Id, error: null);
        await db.Kitchen.RecordPrintAsync(tickets[1].Ticket.Id, "The printer is off");
        Assert.Equal(1, await db.Kitchen.CountNotPrintedAsync());
        var waiting = Assert.Single(await db.Kitchen.GetAsync(DateTime.Today, DateTime.Today.AddDays(1), notPrintedOnly: true));
        Assert.Equal(("The printer is off", 0), (waiting.Ticket.PrintError, waiting.Ticket.PrintCount));

        await db.Kitchen.RecordPrintAsync(waiting.Ticket.Id, error: null);
        Assert.Equal(0, await db.Kitchen.CountNotPrintedAsync());
        var printed = (await db.Kitchen.GetForSaleAsync(sale.Id)).Single(t => t.Ticket.Id == waiting.Ticket.Id).Ticket;
        Assert.Equal(((string?)null, 1, true), (printed.PrintError, printed.PrintCount, printed.IsPrinted));
    }

    [Fact]
    public async Task A_station_with_tickets_is_switched_off_rather_than_deleted()
    {
        var (db, crepe, _, _) = await SetUpAsync();
        await using var _ = db;
        var stations = await db.Kitchen.GetStationsAsync();
        await Assert.ThrowsAsync<BusinessRuleException>(() => db.Kitchen.SaveStationAsync(new Station { Name = "kitchen" }));
        await Assert.ThrowsAsync<BusinessRuleException>(() => db.Kitchen.SaveStationAsync(new Station { Name = " " }));

        await db.Sales.CompleteSaleAsync(Order(db, new CheckoutLine(crepe.Id, 1)));
        Assert.False(await db.Kitchen.DeleteStationAsync(stations.Single(s => s.Name == "Kitchen").Id));
        Assert.True(await db.Kitchen.DeleteStationAsync(stations.Single(s => s.Name == "Bar").Id));
        Assert.Equal([("Kitchen", false)], (await db.Kitchen.GetStationsAsync(includeInactive: true)).Select(s => (s.Name, s.IsActive)));
    }

    [Fact]
    public void The_ticket_shows_the_order_how_it_is_served_and_each_item_without_prices()
    {
        var ticket = new KitchenTicket
        {
            CreatedAt = new DateTime(2026, 11, 5, 13, 7, 0), OrderNumber = 42, OrderType = OrderType.Takeaway, CallName = "Rami", PrintCount = 1,
            Lines =
            [
                new() { ProductName = "Nutella crepe", Quantity = 2, Options = "Banana · Whipped cream", Note = "no nuts" },
                new() { ProductName = "Lemonade", Quantity = 1, Options = "" },
            ],
        };
        var text = string.Join("\n", KitchenTicketFormatter.Format(ticket, "Kitchen", "R-0001", 32, "en"));

        foreach (var part in new[] { "KITCHEN", "REPRINT", "ORDER 42", "TAKEAWAY", "Rami", "R-0001", "2 x Nutella crepe", "Banana · Whipped cream", "! no nuts", "1 x Lemonade", "3 item(s)" })
            Assert.Contains(part, text);
        Assert.DoesNotContain("$", text);
    }
}

/// <summary>Starter menus for each kind of place.</summary>
public class SampleMenuTests
{
    [Theory]
    [InlineData(Venue.CounterService)]
    [InlineData(Venue.Cafe)]
    [InlineData(Venue.Restaurant)]
    [InlineData(Venue.Bar)]
    [InlineData(Venue.FoodTruck)]
    public async Task A_sample_menu_goes_into_an_empty_store_only(Venue venue)
    {
        await using var db = await TestDatabase.CreateAsync();
        Assert.True(await db.Products.IsMenuEmptyAsync());

        await db.Products.AddSampleMenuAsync(venue);

        Assert.False(await db.Products.IsMenuEmptyAsync());
        await using var ctx = await db.Factory.CreateDbContextAsync();
        Assert.True(await ctx.Products.CountAsync() >= 5);
        Assert.True(await ctx.Products.AnyAsync(p => p.StationId != null));
        await Assert.ThrowsAsync<BusinessRuleException>(() => db.Products.AddSampleMenuAsync(venue));
    }

    [Fact]
    public void Each_kind_of_place_starts_with_its_own_counter_features()
    {
        Assert.Equal((false, false), (FeatureSet.Resolve(Venue.Bar, [], null).Takeaway, FeatureSet.Resolve(Venue.Bar, [], null).OrderNumbers));
        Assert.False(FeatureSet.Resolve(Venue.Restaurant, [], null).OrderNumbers);
        var counter = FeatureSet.Resolve(Venue.CounterService, [], null);
        Assert.Equal((true, true, true), (counter.KitchenTickets, counter.OrderNumbers, counter.Takeaway));
        Assert.True(FeatureCatalog.CanVaryByTill(Feature.Takeaway));
    }
}
