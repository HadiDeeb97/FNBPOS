using ClothingStore.Core;
using ClothingStore.Core.Barcodes;
using ClothingStore.Core.Entities;
using ClothingStore.Core.Security;
using ClothingStore.Data.Services;

namespace ClothingStore.Data.Seeding;

/// <summary>Sample catalogue so a fresh install is usable for training and demos.</summary>
internal static class DemoDataSeeder
{
    private sealed record DemoProduct(
        string Name, string Brand, string Category, Gender Gender, decimal Price, decimal Cost,
        string[] Sizes, string[] Colors, string Supplier, string? Material = null,
        Dictionary<string, decimal>? SizePrices = null);

    public static async Task SeedAsync(PosDbContext db, CancellationToken ct)
    {
        string[] categoryNames =
        [
            "Men's Shirts", "Dresses", "Jeans & Trousers", "T-Shirts & Tops", "Knitwear",
            "Jackets & Coats", "Activewear", "Kids", "Accessories", "Footwear",
        ];
        var categories = categoryNames.ToDictionary(n => n, n => new Category { Name = n });
        db.Categories.AddRange(categories.Values);

        var suppliers = new[]
        {
            new Supplier { Name = "Urban Threads Wholesale", ContactName = "Dana Lee", Phone = "555-0140", Email = "orders@urbanthreads.example" },
            new Supplier { Name = "Nordic Knit Co.", ContactName = "Erik Holm", Phone = "555-0141", Email = "sales@nordicknit.example" },
            new Supplier { Name = "Denim Works Ltd.", ContactName = "Priya Shah", Phone = "555-0142", Email = "trade@denimworks.example" },
        }.ToDictionary(s => s.Name);
        db.Suppliers.AddRange(suppliers.Values);

        const string urban = "Urban Threads Wholesale", knit = "Nordic Knit Co.", denim = "Denim Works Ltd.";
        string[] adult = ["S", "M", "L", "XL"];
        DemoProduct[] products =
        [
            new("Classic Oxford Shirt", "Hartwell", "Men's Shirts", Gender.Men, 49.99m, 18m, adult, ["White", "Light Blue"], urban, "100% Cotton"),
            new("Linen Summer Shirt", "Hartwell", "Men's Shirts", Gender.Men, 59.99m, 22m, ["M", "L", "XL"], ["Beige", "Navy"], urban, "100% Linen"),
            new("Slim Fit Stretch Jeans", "Indigo Lab", "Jeans & Trousers", Gender.Men, 69.99m, 25m, ["30", "32", "34", "36"], ["Indigo", "Black"], denim, "98% Cotton, 2% Elastane"),
            new("High-Rise Mom Jeans", "Indigo Lab", "Jeans & Trousers", Gender.Women, 64.99m, 23m, ["24", "26", "28", "30"], ["Light Wash", "Mid Wash"], denim, "100% Cotton"),
            new("Essential Crew Tee", "Basics+", "T-Shirts & Tops", Gender.Unisex, 19.99m, 5m, ["XS", "S", "M", "L", "XL", "XXL"], ["Black", "White", "Grey"], urban, "Organic Cotton",
                new() { ["XXL"] = 22.99m }),
            new("Floral Wrap Dress", "Maison Rue", "Dresses", Gender.Women, 79.99m, 28m, ["XS", "S", "M", "L"], ["Red Floral", "Blue Floral"], urban, "Viscose"),
            new("Little Black Dress", "Maison Rue", "Dresses", Gender.Women, 89.99m, 32m, ["XS", "S", "M", "L"], ["Black"], urban, "Polyester Crepe"),
            new("Merino Crew Sweater", "Fjord", "Knitwear", Gender.Unisex, 89.99m, 35m, adult, ["Charcoal", "Camel", "Forest Green"], knit, "100% Merino Wool"),
            new("Quilted Puffer Jacket", "Fjord", "Jackets & Coats", Gender.Unisex, 149.99m, 60m, adult, ["Black", "Olive"], knit, "Recycled Nylon"),
            new("Wool Blend Overcoat", "Hartwell", "Jackets & Coats", Gender.Men, 229.99m, 95m, ["M", "L", "XL"], ["Camel", "Charcoal"], knit, "70% Wool"),
            new("Performance Leggings", "Motion", "Activewear", Gender.Women, 44.99m, 14m, ["XS", "S", "M", "L"], ["Black", "Plum"], urban, "Nylon/Spandex"),
            new("Kids Graphic Hoodie", "Little Ones", "Kids", Gender.Kids, 34.99m, 11m, ["4Y", "6Y", "8Y", "10Y", "12Y"], ["Navy", "Red"], urban, "Cotton Fleece"),
            new("Leather Belt", "Hartwell", "Accessories", Gender.Men, 39.99m, 12m, ["M", "L"], ["Brown", "Black"], urban, "Full-grain Leather"),
            new("Wool Beanie", "Fjord", "Accessories", Gender.Unisex, 24.99m, 7m, ["One Size"], ["Black", "Grey", "Mustard"], knit, "Wool Blend"),
            new("Canvas Low-Top Sneakers", "Strider", "Footwear", Gender.Unisex, 59.99m, 24m, ["38", "39", "40", "41", "42", "43", "44"], ["White", "Black"], urban, "Canvas"),
        ];

        var random = new Random(42);
        long barcodeSeq = 1;
        var usedSkus = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var p in products)
        {
            var product = new Product
            {
                Name = p.Name,
                Brand = p.Brand,
                Category = categories[p.Category],
                Supplier = suppliers[p.Supplier],
                Gender = p.Gender,
                Price = p.Price,
                Cost = p.Cost,
                Material = p.Material,
                Season = "All Season",
                StyleCode = SkuGenerator.StyleCodeFromName(p.Name),
            };

            foreach (var color in p.Colors)
            {
                foreach (var size in p.Sizes)
                {
                    var variant = new ProductVariant
                    {
                        Product = product,
                        Size = size,
                        Color = color,
                        Sku = SkuGenerator.MakeUnique(SkuGenerator.Build(product.StyleCode, size, color), usedSkus),
                        Barcode = Ean13.CreateInStore(barcodeSeq++),
                        PriceOverride = p.SizePrices?.GetValueOrDefault(size),
                        ReorderLevel = 2,
                    };
                    usedSkus.Add(variant.Sku);
                    product.Variants.Add(variant);

                    var qty = random.Next(0, 13);
                    if (qty > 0)
                        StockLedger.Apply(db, variant, qty, StockMovementType.InitialStock, null, notes: "Demo opening stock");
                }
            }
            db.Products.Add(product);
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
