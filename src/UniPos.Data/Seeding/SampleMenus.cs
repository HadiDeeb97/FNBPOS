using UniPos.Core;
using UniPos.Core.Entities;
using UniPos.Core.Features;
using UniPos.Data.Services;

namespace UniPos.Data.Seeding;

/// <summary>
/// A starter menu for each kind of place: categories, stations (where tickets print), modifier groups and items, so a
/// new store can sell at once and then edit prices and names. Offered on the first-run screen while the menu is empty.
/// </summary>
internal static class SampleMenus
{
    /// <param name="Groups">The modifier groups asked, in order.</param>
    /// <param name="Station">Where it is made; null for packaged goods (no ticket).</param>
    /// <param name="Stock">Opening stock of a counted item; null for items made to order.</param>
    private sealed record Item(string Name, string Category, decimal Price, decimal Cost, string? Station, string[] Groups,
        int? Stock = null, string? Barcode = null);

    private sealed record Group(string Name, int Min, int Max, (string Name, decimal Price)[] Options);

    private const string Kitchen = "Kitchen", Bar = "Bar";

    // Shared groups, used by several menus.
    private static readonly Group Size = new("Size", 1, 1, [("Small", 0m), ("Medium", 0.50m), ("Large", 1.00m)]);
    private static readonly Group Milk = new("Milk", 1, 1, [("Full fat", 0m), ("Skimmed", 0m), ("Oat", 0.50m)]);
    private static readonly Group Sugar = new("Sugar", 0, 1, [("No sugar", 0m), ("Less sugar", 0m), ("Extra sugar", 0m)]);
    private static readonly Group Remove = new("Remove", 0, 0, [("No onions", 0m), ("No mayo", 0m), ("No tomatoes", 0m), ("No pickles", 0m)]);
    private static readonly Group Extras = new("Extras", 0, 0, [("Extra cheese", 0.75m), ("Mushrooms", 0.50m), ("Jalapeños", 0.25m)]);

    private static readonly Dictionary<Venue, (Group[] Groups, Item[] Items)> Menus = new()
    {
        [Venue.CounterService] = (
        [
            Size, Milk, Sugar, Remove, Extras,
            new("Fillings", 1, 3, [("Nutella", 0m), ("Lotus", 0.50m), ("Banana", 0.50m), ("Strawberry", 0.75m), ("Kinder", 1.00m)]),
            new("Extra fillings", 0, 2, [("Banana", 0.50m), ("Strawberry", 0.75m), ("White chocolate", 0.75m)]),
        ],
        [
            new("Nutella crepe", "Sweet crepes", 4.50m, 1.40m, Kitchen, ["Extra fillings"]),
            new("Lotus crepe", "Sweet crepes", 5.00m, 1.60m, Kitchen, ["Extra fillings"]),
            new("Build your own crepe", "Sweet crepes", 3.50m, 0.90m, Kitchen, ["Fillings"]),
            new("Chicken & cheese crepe", "Savoury crepes", 6.50m, 2.20m, Kitchen, ["Extras", "Remove"]),
            new("Turkey & cheese crepe", "Savoury crepes", 6.00m, 2.00m, Kitchen, ["Extras", "Remove"]),
            new("Halloumi crepe", "Savoury crepes", 5.50m, 1.80m, Kitchen, ["Extras", "Remove"]),
            new("Espresso", "Hot drinks", 2.00m, 0.40m, Bar, ["Sugar"]),
            new("Cappuccino", "Hot drinks", 3.00m, 0.70m, Bar, ["Size", "Milk", "Sugar"]),
            new("Latte", "Hot drinks", 3.25m, 0.75m, Bar, ["Size", "Milk", "Sugar"]),
            new("Tea", "Hot drinks", 1.50m, 0.20m, Bar, ["Sugar"]),
            new("Fresh orange juice", "Cold drinks", 3.50m, 1.10m, Bar, ["Size"]),
            new("Lemonade", "Cold drinks", 2.75m, 0.60m, Bar, ["Size", "Sugar"]),
            new("Iced coffee", "Cold drinks", 3.75m, 0.90m, Bar, ["Size", "Milk", "Sugar"]),
            new("Water 500 ml", "Snacks", 0.75m, 0.25m, null, [], Stock: 48, Barcode: "5281006400016"),
            new("Soft drink can", "Snacks", 1.25m, 0.55m, null, [], Stock: 36, Barcode: "5449000000996"),
            new("Potato chips", "Snacks", 1.00m, 0.40m, null, [], Stock: 24, Barcode: "5281031100011"),
        ]),

        [Venue.Cafe] = (
        [
            Size, Milk, Sugar,
            new("Shots", 0, 2, [("Extra shot", 0.75m), ("Decaf", 0m)]),
            new("Syrup", 0, 1, [("Vanilla", 0.50m), ("Caramel", 0.50m), ("Hazelnut", 0.50m)]),
            new("Warm it up", 0, 1, [("Warmed", 0m)]),
        ],
        [
            new("Espresso", "Coffee", 2.00m, 0.40m, Bar, ["Shots", "Sugar"]),
            new("Americano", "Coffee", 2.50m, 0.45m, Bar, ["Size", "Shots", "Sugar"]),
            new("Cappuccino", "Coffee", 3.00m, 0.70m, Bar, ["Size", "Milk", "Shots", "Syrup", "Sugar"]),
            new("Latte", "Coffee", 3.25m, 0.75m, Bar, ["Size", "Milk", "Shots", "Syrup", "Sugar"]),
            new("Flat white", "Coffee", 3.25m, 0.80m, Bar, ["Milk", "Sugar"]),
            new("Hot chocolate", "Coffee", 3.00m, 0.80m, Bar, ["Size", "Milk"]),
            new("Tea", "Tea", 1.75m, 0.20m, Bar, ["Sugar"]),
            new("Iced latte", "Cold drinks", 3.75m, 0.90m, Bar, ["Size", "Milk", "Syrup", "Sugar"]),
            new("Fresh orange juice", "Cold drinks", 3.50m, 1.10m, Bar, ["Size"]),
            new("Croissant", "Pastries", 1.75m, 0.60m, null, ["Warm it up"], Stock: 20),
            new("Pain au chocolat", "Pastries", 2.00m, 0.70m, null, ["Warm it up"], Stock: 20),
            new("Blueberry muffin", "Pastries", 2.25m, 0.80m, null, ["Warm it up"], Stock: 12),
            new("Cheese manousheh", "Bakery", 2.50m, 0.90m, Kitchen, []),
            new("Zaatar manousheh", "Bakery", 1.50m, 0.40m, Kitchen, []),
            new("Water 500 ml", "Drinks", 0.75m, 0.25m, null, [], Stock: 48, Barcode: "5281006400016"),
        ]),

        [Venue.Restaurant] = (
        [
            Remove,
            new("Cooking", 1, 1, [("Rare", 0m), ("Medium", 0m), ("Well done", 0m)]),
            new("Side", 1, 1, [("Fries", 0m), ("Rice", 0m), ("Salad", 0m), ("Mashed potatoes", 0.50m)]),
            new("Add", 0, 0, [("Extra cheese", 1.00m), ("Fried egg", 1.00m), ("Avocado", 1.50m)]),
        ],
        [
            new("Hummus", "Starters", 4.00m, 1.00m, Kitchen, []),
            new("Fattoush", "Starters", 5.00m, 1.40m, Kitchen, ["Remove"]),
            new("Tabbouleh", "Starters", 5.00m, 1.30m, Kitchen, ["Remove"]),
            new("Grilled chicken", "Mains", 12.00m, 4.00m, Kitchen, ["Side", "Remove"]),
            new("Beef burger", "Mains", 11.00m, 3.80m, Kitchen, ["Cooking", "Side", "Add", "Remove"]),
            new("Mixed grill", "Mains", 16.00m, 6.00m, Kitchen, ["Cooking", "Side"]),
            new("Penne arrabbiata", "Mains", 10.00m, 2.50m, Kitchen, ["Add"]),
            new("Chocolate fondant", "Desserts", 6.00m, 1.80m, Kitchen, []),
            new("Fresh lemonade", "Drinks", 3.50m, 0.70m, Bar, []),
            new("Soft drink", "Drinks", 2.00m, 0.55m, null, [], Stock: 48),
            new("Water 1.5 L", "Drinks", 1.50m, 0.40m, null, [], Stock: 24),
        ]),

        [Venue.Bar] = (
        [
            new("Measure", 1, 1, [("Single", 0m), ("Double", 4.00m)]),
            new("Mixer", 0, 1, [("Cola", 0.50m), ("Soda", 0.50m), ("Tonic", 0.75m)]),
            new("Ice", 0, 1, [("No ice", 0m), ("Extra ice", 0m)]),
        ],
        [
            new("Almaza", "Beer", 4.00m, 1.40m, null, [], Stock: 72),
            new("Heineken", "Beer", 5.00m, 1.80m, null, [], Stock: 48),
            new("Mojito", "Cocktails", 9.00m, 2.50m, Bar, ["Ice"]),
            new("Margarita", "Cocktails", 9.00m, 2.60m, Bar, ["Ice"]),
            new("Whisky", "Spirits", 7.00m, 2.00m, Bar, ["Measure", "Mixer", "Ice"]),
            new("Vodka", "Spirits", 6.00m, 1.70m, Bar, ["Measure", "Mixer", "Ice"]),
            new("Arak", "Spirits", 5.00m, 1.30m, Bar, ["Ice"]),
            new("Fries", "Bites", 4.00m, 0.90m, Kitchen, []),
            new("Chicken wings", "Bites", 8.00m, 2.50m, Kitchen, []),
            new("Mixed nuts", "Bites", 3.00m, 1.00m, null, [], Stock: 30),
        ]),

        [Venue.FoodTruck] = (
        [
            Remove, Extras,
            new("Make it a meal", 0, 1, [("Meal (fries + drink)", 3.00m)]),
        ],
        [
            new("Classic burger", "Burgers", 7.00m, 2.40m, Kitchen, ["Make it a meal", "Extras", "Remove"]),
            new("Chicken burger", "Burgers", 6.50m, 2.10m, Kitchen, ["Make it a meal", "Extras", "Remove"]),
            new("Falafel wrap", "Wraps", 4.00m, 1.00m, Kitchen, ["Make it a meal", "Remove"]),
            new("Shawarma wrap", "Wraps", 5.00m, 1.60m, Kitchen, ["Make it a meal", "Remove"]),
            new("Fries", "Sides", 3.00m, 0.60m, Kitchen, []),
            new("Soft drink can", "Drinks", 1.25m, 0.55m, null, [], Stock: 48, Barcode: "5449000000996"),
            new("Water 500 ml", "Drinks", 0.75m, 0.25m, null, [], Stock: 48, Barcode: "5281006400016"),
        ]),
    };

    /// <summary>Adds the starter menu of <paramref name="venue"/> to <paramref name="db"/> (saved by the caller).</summary>
    public static void Add(PosDbContext db, Venue venue)
    {
        var (groups, items) = Menus.TryGetValue(venue, out var menu) ? menu : Menus[Venue.CounterService];

        var categories = items.Select(i => i.Category).Distinct().ToDictionary(n => n, n => new Category { Name = n });
        db.Categories.AddRange(categories.Values);

        var stationNames = items.Select(i => i.Station).OfType<string>().Distinct().ToList();
        var stations = stationNames.Select((n, i) => new Station { Name = n, SortOrder = i }).ToDictionary(s => s.Name);
        db.Stations.AddRange(stations.Values);

        var modifierGroups = groups.ToDictionary(g => g.Name, g => new ModifierGroup
        {
            Name = g.Name, MinChoices = g.Min, MaxChoices = g.Max,
            Options = g.Options.Select((o, i) => new Modifier { Name = o.Name, Price = o.Price, Cost = Money.Round(o.Price * 0.3m), SortOrder = i }).ToList(),
        });
        db.ModifierGroups.AddRange(modifierGroups.Values);

        var code = ProductService.FirstCode;
        foreach (var i in items)
        {
            var product = new Product
            {
                Name = i.Name,
                Category = categories[i.Category],
                Station = i.Station is null ? null : stations[i.Station],
                Price = i.Price,
                Cost = i.Cost,
                Sku = (code++).ToString(),
                Barcode = i.Barcode,
                TrackStock = i.Stock is not null,
                ReorderLevel = i.Stock is null ? 0 : 6,
                ModifierGroups = i.Groups.Select((g, order) => new ProductModifierGroup { ModifierGroup = modifierGroups[g], SortOrder = order }).ToList(),
            };
            db.Products.Add(product);
            if (i.Stock is { } stock) StockLedger.Apply(db, product, stock, StockMovementType.InitialStock, null, notes: "Sample menu opening stock");
        }
    }
}
