using UniPos.Core;
using UniPos.Core.Entities;
using UniPos.Core.Security;
using UniPos.Data.Services;

namespace UniPos.Data.Seeding;

/// <summary>Sample menu so a fresh install is usable for training and demos: crepes, drinks and a few packaged items.</summary>
internal static class DemoDataSeeder
{
    /// <param name="Groups">Names of the modifier groups the item offers, in the order the till asks them.</param>
    /// <param name="Stock">Opening stock of a counted (packaged) item; null for items made to order.</param>
    private sealed record DemoItem(string Name, string Category, decimal Price, decimal Cost, string[] Groups, int? Stock = null, string? Barcode = null);

    private static ModifierGroup Group(string name, int min, int max, params (string Name, decimal Price)[] options) => new()
    {
        Name = name,
        MinChoices = min,
        MaxChoices = max,
        Options = options.Select((o, i) => new Modifier { Name = o.Name, Price = o.Price, Cost = Money.Round(o.Price * 0.3m), SortOrder = i }).ToList(),
    };

    public static async Task SeedAsync(PosDbContext db, CancellationToken ct)
    {
        string[] categoryNames = ["Sweet crepes", "Savoury crepes", "Hot drinks", "Cold drinks", "Snacks"];
        var categories = categoryNames.ToDictionary(n => n, n => new Category { Name = n });
        db.Categories.AddRange(categories.Values);

        var supplier = new Supplier { Name = "Beirut Food Supplies", ContactName = "Rami Khoury", Phone = "01 555 140", Email = "orders@beirutfood.example" };
        db.Suppliers.Add(supplier);

        ModifierGroup[] groups =
        [
            Group("Size", 1, 1, ("Small", 0m), ("Medium", 0.50m), ("Large", 1.00m)),
            Group("Fillings", 1, 3, ("Nutella", 0m), ("Lotus", 0.50m), ("Banana", 0.50m), ("Strawberry", 0.75m), ("Kinder", 1.00m)),
            Group("Extra fillings", 0, 2, ("Banana", 0.50m), ("Strawberry", 0.75m), ("White chocolate", 0.75m)),
            Group("Extras", 0, 0, ("Extra cheese", 0.75m), ("Mushrooms", 0.50m), ("Jalapeños", 0.25m)),
            Group("Remove", 0, 0, ("No onions", 0m), ("No mayo", 0m), ("No tomatoes", 0m)),
            Group("Milk", 1, 1, ("Full fat", 0m), ("Skimmed", 0m), ("Oat", 0.50m)),
            Group("Sugar", 0, 1, ("No sugar", 0m), ("Less sugar", 0m), ("Extra sugar", 0m)),
        ];
        db.ModifierGroups.AddRange(groups);
        var byName = groups.ToDictionary(g => g.Name);

        DemoItem[] items =
        [
            new("Nutella crepe", "Sweet crepes", 4.50m, 1.40m, ["Extra fillings"]),
            new("Lotus crepe", "Sweet crepes", 5.00m, 1.60m, ["Extra fillings"]),
            new("Build your own crepe", "Sweet crepes", 3.50m, 0.90m, ["Fillings"]),
            new("Chicken & cheese crepe", "Savoury crepes", 6.50m, 2.20m, ["Extras", "Remove"]),
            new("Turkey & cheese crepe", "Savoury crepes", 6.00m, 2.00m, ["Extras", "Remove"]),
            new("Halloumi crepe", "Savoury crepes", 5.50m, 1.80m, ["Extras", "Remove"]),
            new("Espresso", "Hot drinks", 2.00m, 0.40m, ["Sugar"]),
            new("Cappuccino", "Hot drinks", 3.00m, 0.70m, ["Size", "Milk", "Sugar"]),
            new("Latte", "Hot drinks", 3.25m, 0.75m, ["Size", "Milk", "Sugar"]),
            new("Tea", "Hot drinks", 1.50m, 0.20m, ["Sugar"]),
            new("Fresh orange juice", "Cold drinks", 3.50m, 1.10m, ["Size"]),
            new("Lemonade", "Cold drinks", 2.75m, 0.60m, ["Size", "Sugar"]),
            new("Iced coffee", "Cold drinks", 3.75m, 0.90m, ["Size", "Milk", "Sugar"]),
            new("Water 500 ml", "Snacks", 0.75m, 0.25m, [], Stock: 48, Barcode: "5281006400016"),
            new("Soft drink can", "Snacks", 1.25m, 0.55m, [], Stock: 36, Barcode: "5449000000996"),
            new("Potato chips", "Snacks", 1.00m, 0.40m, [], Stock: 24, Barcode: "5281031100011"),
        ];

        var code = ProductService.FirstCode;
        foreach (var i in items)
        {
            var product = new Product
            {
                Name = i.Name,
                Category = categories[i.Category],
                Price = i.Price,
                Cost = i.Cost,
                Sku = (code++).ToString(),
                Barcode = i.Barcode,
                TrackStock = i.Stock is not null,
                ReorderLevel = i.Stock is null ? 0 : 6,
                Supplier = i.Stock is null ? null : supplier,
                ModifierGroups = i.Groups.Select((g, order) => new ProductModifierGroup { ModifierGroup = byName[g], SortOrder = order }).ToList(),
            };
            db.Products.Add(product);
            if (i.Stock is { } stock) StockLedger.Apply(db, product, stock, StockMovementType.InitialStock, null, notes: "Demo opening stock");
        }

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
