using ClothingStore.Core;
using ClothingStore.Core.Entities;
using ClothingStore.Data.Services;

namespace ClothingStore.Tests;

public class CustomerAddressTests
{
    [Fact]
    public async Task Lebanons_governorates_come_ready_and_more_states_can_be_added()
    {
        await using var db = await TestDatabase.CreateAsync();
        var regions = await db.Customers.GetRegionsAsync();
        Assert.Equal(["Beirut", "Mount Lebanon", "North", "Akkar", "Bekaa", "Baalbek-Hermel", "South", "Nabatieh"], regions.Select(r => r.Name));
        Assert.Equal("بيروت", regions[0].NameAr);

        var added = await db.Customers.AddRegionAsync("  Keserwan ");
        Assert.Equal("Keserwan", added.Name);
        Assert.Equal(added.Id, (await db.Customers.AddRegionAsync("keserwan")).Id); // same name: no duplicate
        Assert.Equal(9, (await db.Customers.GetRegionsAsync()).Count);
        await Assert.ThrowsAsync<BusinessRuleException>(() => db.Customers.AddRegionAsync(" "));
    }

    [Fact]
    public async Task Customers_keep_an_address_and_state_and_show_what_they_spent()
    {
        await using var db = await TestDatabase.CreateAsync();
        var beirut = (await db.Customers.GetRegionsAsync())[0];
        var rana = await db.Customers.SaveAsync(new Customer { FirstName = "Rana", Phone = "03111222", Address = " Hamra, Bliss St ", RegionId = beirut.Id });
        await db.Customers.SaveAsync(new Customer { FirstName = "Maya", Phone = "03333444" });

        var tee = await db.CreateTeeAsync();
        var sale = await db.Sales.CompleteSaleAsync(new CheckoutRequest
        {
            UserId = db.Cashier.Id, CustomerId = rana.Id, Lines = [new CheckoutLine(tee.Variants[0].Id, 2)], Payments = [new PaymentInput(PaymentMethod.Card, 44m)],
        });
        await db.Returns.ProcessReturnAsync(new ReturnRequest
        {
            SaleId = sale.Id, UserId = db.Cashier.Id, Lines = [new ReturnLineRequest(sale.Lines[0].Id, 1)],
        });

        var found = Assert.Single(await db.Customers.SearchAsync("bliss"));
        Assert.Equal(("Hamra, Bliss St", "Beirut", 22m, 1), (found.Address, found.Region!.Name, found.TotalSpent, found.Visits));
        Assert.Equal("Hamra, Bliss St, Beirut", found.FullAddress);
        Assert.NotNull(found.LastVisit);
        Assert.Single(await db.Customers.SearchAsync(regionId: beirut.Id));
        Assert.Equal(0m, (await db.Customers.SearchAsync("Maya")).Single().TotalSpent);
    }
}
