using UniPos.Core;
using UniPos.Core.Entities;
using UniPos.Core.Features;
using UniPos.Core.Security;

namespace UniPos.Data.Seeding;

/// <summary>Sample data so a fresh install is usable for training and demos: a crepe and coffee menu, customers and users.</summary>
internal static class DemoDataSeeder
{
    public static async Task SeedAsync(PosDbContext db, CancellationToken ct)
    {
        SampleMenus.Add(db, Venue.CounterService);

        // The packaged goods come from a supplier, for trying purchase orders.
        var supplier = new Supplier { Name = "Beirut Food Supplies", ContactName = "Rami Khoury", Phone = "01 555 140", Email = "orders@beirutfood.example" };
        db.Suppliers.Add(supplier);
        foreach (var product in db.Products.Local.Where(p => p.TrackStock)) product.Supplier = supplier;

        db.Customers.AddRange(
            new Customer { FirstName = "Sarah", LastName = "Johnson", Phone = "555-0101", Email = "sarah.j@example.com", LoyaltyPoints = 320, StoreCredit = 15m },
            new Customer { FirstName = "Michael", LastName = "Chen", Phone = "555-0102", Email = "mchen@example.com", LoyaltyPoints = 85 },
            new Customer { FirstName = "Amira", LastName = "Haddad", Phone = "555-0103", LoyaltyPoints = 1240 });

        if (!db.Users.Any(u => u.Username == "manager"))
            db.Users.Add(new User { Username = "manager", FullName = "Store Manager", Role = UserRole.Manager, PasswordHash = PasswordHasher.Hash("manager123"), MustChangePassword = true });
        if (!db.Users.Any(u => u.Username == "cashier"))
            db.Users.Add(new User { Username = "cashier", FullName = "Front Cashier", Role = UserRole.Cashier, PasswordHash = PasswordHasher.Hash("cashier123"), MustChangePassword = true });

        await db.SaveChangesAsync(ct);
    }
}
