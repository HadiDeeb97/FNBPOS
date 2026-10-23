using ClothingStore.Core;
using ClothingStore.Core.Entities;
using ClothingStore.Data.Services;

namespace ClothingStore.Tests;

/// <summary>Delivery companies and drivers saved with their details.</summary>
public class DeliveryPartnerTests
{
    [Fact]
    public async Task Partners_are_unique_by_name_and_offered_before_names_from_old_orders()
    {
        await using var db = await TestDatabase.CreateAsync();
        var tee = await db.CreateTeeAsync();
        await db.Sales.CompleteSaleAsync(new CheckoutRequest
        {
            UserId = db.Cashier.Id, CustomerId = await db.ShopperIdAsync(), Lines = [new CheckoutLine(tee.Variants[0].Id, 1)],
            Channel = SalesChannel.WhatsApp, Courier = "Old Courier", Payments = [new PaymentInput(PaymentMethod.Delivery, 22m)],
        });

        var toters = await db.Deliveries.SavePartnerAsync(new DeliveryPartner
        {
            Name = " Toters ", Kind = DeliveryPartnerKind.Company, Phone = "01 234 567", DefaultFee = 3.456m,
        });
        Assert.Equal(("Toters", 3.46m), (toters.Name, toters.DefaultFee));
        await db.Deliveries.SavePartnerAsync(new DeliveryPartner { Name = "Abu Ali", Kind = DeliveryPartnerKind.Driver });

        await Assert.ThrowsAsync<BusinessRuleException>(() => db.Deliveries.SavePartnerAsync(new DeliveryPartner { Name = "TOTERS" }));
        await Assert.ThrowsAsync<BusinessRuleException>(() => db.Deliveries.SavePartnerAsync(new DeliveryPartner { Name = "  " }));
        await Assert.ThrowsAsync<BusinessRuleException>(() => db.Deliveries.SavePartnerAsync(new DeliveryPartner { Name = "X", DefaultFee = -1 }));

        Assert.Equal(["Abu Ali", "Toters", "Old Courier"], await db.Deliveries.GetCouriersAsync());
    }

    [Fact]
    public async Task Renaming_a_partner_moves_its_orders_and_one_with_orders_is_deactivated_not_deleted()
    {
        await using var db = await TestDatabase.CreateAsync();
        var tee = await db.CreateTeeAsync();
        var partner = await db.Deliveries.SavePartnerAsync(new DeliveryPartner { Name = "Toters" });
        var unused = await db.Deliveries.SavePartnerAsync(new DeliveryPartner { Name = "Never used" });
        await db.Sales.CompleteSaleAsync(new CheckoutRequest
        {
            UserId = db.Cashier.Id, CustomerId = await db.ShopperIdAsync(), Lines = [new CheckoutLine(tee.Variants[0].Id, 1)],
            Channel = SalesChannel.Instagram, Courier = "Toters", Payments = [new PaymentInput(PaymentMethod.Delivery, 22m)],
        });

        partner.Name = "Toters Lebanon";
        await db.Deliveries.SavePartnerAsync(partner);
        var balance = Assert.Single(await db.Deliveries.GetBalancesAsync());
        Assert.Equal(("Toters Lebanon", 22m), (balance.Courier, balance.Owed));

        Assert.False(await db.Deliveries.DeletePartnerAsync(partner.Id));
        Assert.True(await db.Deliveries.DeletePartnerAsync(unused.Id));
        var left = Assert.Single(await db.Deliveries.GetPartnersAsync(includeInactive: true));
        Assert.False(left.IsActive);
        Assert.Empty(await db.Deliveries.GetPartnersAsync());
    }
}
