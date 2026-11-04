using UniPos.Core;
using UniPos.Core.Entities;
using UniPos.Core.Features;
using UniPos.Data.Services;
using Microsoft.EntityFrameworkCore;

namespace UniPos.Tests;

/// <summary>What each kind of place starts with, and how store and till choices override it.</summary>
public class FeatureSetTests
{
    [Fact]
    public void A_store_not_set_up_has_everything_on()
    {
        var set = FeatureSet.Resolve(null, [], "till-1");
        Assert.All(FeatureCatalog.All, f => Assert.True(set.IsOn(f)));
    }

    [Theory]
    [InlineData(Venue.Restaurant, true, true)]
    [InlineData(Venue.CounterService, true, true)]
    [InlineData(Venue.Bar, false, true)]
    [InlineData(Venue.FoodTruck, false, false)]
    public void Each_kind_of_place_starts_with_its_own_features(Venue venue, bool delivery, bool purchasing)
    {
        var set = FeatureSet.Resolve(venue, [], null);
        Assert.Equal((delivery, purchasing), (set.Delivery, set.Purchasing));
    }

    [Fact]
    public void A_till_choice_beats_the_store_choice_which_beats_the_venue()
    {
        FeatureSwitch[] switches =
        [
            new() { Feature = Feature.Delivery, Value = FeatureSwitch.On },                       // the store: on (a bar: off by default)
            new() { Feature = Feature.Delivery, TillId = "bar-till", Value = FeatureSwitch.Off }, // but not at the bar's till
        ];
        Assert.True(FeatureSet.Resolve(Venue.Bar, switches, "front-till").Delivery);
        Assert.False(FeatureSet.Resolve(Venue.Bar, switches, "bar-till").Delivery);
        Assert.True(FeatureSet.Resolve(Venue.Bar, switches, null).Delivery);
    }

    [Fact]
    public void A_till_cannot_differ_on_a_store_wide_feature()
    {
        FeatureSwitch[] switches = [new() { Feature = Feature.Purchasing, TillId = "till-1", Value = FeatureSwitch.Off }];
        Assert.True(FeatureSet.Resolve(Venue.Restaurant, switches, "till-1").Purchasing);
    }
}

public class FeatureServiceTests
{
    private static Dictionary<Feature, bool?> Store(bool? delivery = null, bool? purchasing = null) =>
        new() { [Feature.Delivery] = delivery, [Feature.Purchasing] = purchasing };

    [Fact]
    public async Task Store_and_till_choices_are_kept_and_read_back_per_till()
    {
        await using var db = await TestDatabase.CreateAsync();
        await db.Features.SaveAsync(new FeatureChoices(Venue.Cafe, Store(purchasing: false), "till-A",
            new Dictionary<Feature, bool?> { [Feature.Delivery] = false }), db.Admin.Id);

        Assert.Equal((false, false), Pair(await db.Features.GetAsync("till-A")));
        Assert.Equal((true, false), Pair(await db.Features.GetAsync("till-B")));

        var overview = await db.Features.GetOverviewAsync("till-A");
        Assert.Equal(Venue.Cafe, overview.Venue);
        var delivery = overview.Features.Single(f => f.Feature == Feature.Delivery);
        Assert.Equal((true, (bool?)null, (bool?)false), (delivery.VenueDefault, delivery.Store, delivery.Till));
        Assert.Equal(Venue.Cafe, (await db.Settings.GetAsync()).Venue);

        static (bool, bool) Pair(FeatureSet s) => (s.Delivery, s.Purchasing);
    }

    [Fact]
    public async Task A_choice_that_matches_the_kind_of_place_is_not_stored()
    {
        await using var db = await TestDatabase.CreateAsync();
        await db.Features.SaveAsync(new FeatureChoices(Venue.Bar, Store(delivery: false, purchasing: true)), db.Admin.Id);

        await using var ctx = await db.Factory.CreateDbContextAsync();
        Assert.Empty(await ctx.FeatureSwitches.ToListAsync());

        // So switching the store to a restaurant gives it a restaurant's features.
        await db.Features.SaveAsync(new FeatureChoices(Venue.Restaurant, Store()), db.Admin.Id);
        Assert.True((await db.Features.GetAsync(null)).Delivery);
    }

    [Fact]
    public async Task Only_admins_change_the_features_like_the_rest_of_the_settings()
    {
        await using var db = await TestDatabase.CreateAsync();
        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            db.Features.SaveAsync(new FeatureChoices(Venue.Bar, Store()), db.Cashier.Id));
        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            db.Features.SaveAsync(new FeatureChoices(Venue.Bar, Store()), db.Manager.Id));
        await db.Features.SaveAsync(new FeatureChoices(Venue.Bar, Store()), db.Admin.Id);
    }

    [Fact]
    public async Task Saving_the_settings_page_keeps_the_kind_of_place()
    {
        await using var db = await TestDatabase.CreateAsync();
        var stale = (await db.Settings.GetAsync()).Clone(); // opened before the venue was chosen
        await db.Features.SaveAsync(new FeatureChoices(Venue.Bar, Store()), db.Admin.Id);

        stale.StoreName = "Night Owl";
        await db.Settings.SaveAsync(stale);
        Assert.Equal(Venue.Bar, (await db.Settings.GetAsync()).Venue);
    }

    [Fact]
    public async Task An_online_order_is_refused_at_a_till_where_online_orders_are_off()
    {
        await using var db = await TestDatabase.CreateAsync();
        await db.Features.SaveAsync(new FeatureChoices(Venue.Restaurant, Store(), "bar-till",
            new Dictionary<Feature, bool?> { [Feature.Delivery] = false }), db.Admin.Id);
        var tee = await db.CreateTeeAsync();
        var shopper = await db.ShopperIdAsync();

        CheckoutRequest Order(string till) => new()
        {
            UserId = db.Cashier.Id, TillId = till, CustomerId = shopper, Lines = [new CheckoutLine(tee.Items[0].Id, 1)],
            Channel = SalesChannel.WhatsApp, Payments = [new PaymentInput(PaymentMethod.Cash, 22m)],
        };

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() => db.Sales.CompleteSaleAsync(Order("bar-till")));
        Assert.Contains("switched off", ex.Message);
        await db.Sales.CompleteSaleAsync(Order("front-till"));

        // In-store sales go on as usual at the bar's till.
        await db.Sales.CompleteSaleAsync(new CheckoutRequest
        {
            UserId = db.Cashier.Id, TillId = "bar-till", Lines = [new CheckoutLine(tee.Items[1].Id, 1)],
            Payments = [new PaymentInput(PaymentMethod.Cash, 22m)],
        });
    }

    [Fact]
    public async Task Online_orders_stay_on_while_a_delivery_company_owes_money()
    {
        await using var db = await TestDatabase.CreateAsync();
        var tee = await db.CreateTeeAsync();
        await db.Sales.CompleteSaleAsync(new CheckoutRequest
        {
            UserId = db.Cashier.Id, CustomerId = await db.ShopperIdAsync(), Lines = [new CheckoutLine(tee.Items[0].Id, 1)],
            Channel = SalesChannel.Instagram, Courier = "Toters", Payments = [new PaymentInput(PaymentMethod.Delivery, 22m)],
        });

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            db.Features.SaveAsync(new FeatureChoices(Venue.Restaurant, Store(delivery: false)), db.Admin.Id));
        Assert.Contains("Deliveries", ex.Message);
        // Not at one till either: its Deliveries page would hide the money too.
        await Assert.ThrowsAsync<BusinessRuleException>(() => db.Features.SaveAsync(new FeatureChoices(Venue.Restaurant, Store(), "till-1",
            new Dictionary<Feature, bool?> { [Feature.Delivery] = false }), db.Admin.Id));
        // Nor by choosing a kind of place that has no delivery.
        await Assert.ThrowsAsync<BusinessRuleException>(() => db.Features.SaveAsync(new FeatureChoices(Venue.Bar, Store()), db.Admin.Id));
        Assert.True((await db.Features.GetAsync(null)).Delivery);
    }

    [Fact]
    public async Task Purchasing_stays_on_while_goods_are_ordered_and_off_means_no_new_orders()
    {
        await using var db = await TestDatabase.CreateAsync();
        var tee = await db.CreateTeeAsync(stockEach: 0);
        var supplier = await db.Suppliers.SaveAsync(new Supplier { Name = "Flour Mill" });
        PurchaseOrder Order() => new()
        {
            SupplierId = supplier.Id,
            Lines = [new PurchaseOrderLine { ProductId = tee.Items[0].Id, QuantityOrdered = 4, UnitCost = 5m }],
        };

        var po = await db.PurchaseOrders.SaveAsync(Order(), db.Manager.Id);
        await db.PurchaseOrders.MarkOrderedAsync(po.Id);
        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            db.Features.SaveAsync(new FeatureChoices(Venue.Cafe, Store(purchasing: false)), db.Admin.Id));
        Assert.Contains("receive or cancel", ex.Message);

        await db.PurchaseOrders.CancelAsync(po.Id);
        var draft = await db.PurchaseOrders.SaveAsync(Order(), db.Manager.Id);
        await db.Features.SaveAsync(new FeatureChoices(Venue.Cafe, Store(purchasing: false)), db.Admin.Id);

        await Assert.ThrowsAsync<BusinessRuleException>(() => db.PurchaseOrders.SaveAsync(Order(), db.Manager.Id));
        await Assert.ThrowsAsync<BusinessRuleException>(() => db.PurchaseOrders.MarkOrderedAsync(draft.Id));
        // The draft is still there for when purchasing is switched on again.
        Assert.NotNull(await db.PurchaseOrders.GetAsync(draft.Id));
    }
}
