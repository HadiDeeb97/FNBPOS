namespace UniPos.Core.Features;

/// <summary>
/// A part of the program a store can switch on or off. Each venue has sensible defaults (<see cref="Presets"/>); an
/// admin can change any of them for the whole store, and some for one till only (<see cref="FeatureCatalog.CanVaryByTill"/>).
/// Stored by name, so new ones can be added anywhere in the list.
/// </summary>
public enum Feature
{
    /// <summary>Online orders (WhatsApp, Instagram, phone...) with a delivery fee and delivery company, and the Deliveries page.</summary>
    Delivery,

    /// <summary>Suppliers and purchase orders.</summary>
    Purchasing,
}

/// <summary>The kind of place the POS runs in; it decides which features are on until an admin changes them.</summary>
public enum Venue
{
    /// <summary>Every feature on; the manager picks.</summary>
    Custom = 0,

    /// <summary>Crepe shop, fast food, juice bar: order and pay at the counter.</summary>
    CounterService = 1,

    /// <summary>Café or bakery.</summary>
    Cafe = 2,

    Restaurant = 3,
    Bar = 4,
    FoodTruck = 5,
}

public static class FeatureCatalog
{
    public static IReadOnlyList<Feature> All { get; } = Enum.GetValues<Feature>();

    /// <summary>
    /// Features one till may have differently from the rest of the store (a till by the phone takes online orders, the
    /// bar's till doesn't). The others belong to the store's own records, so every till sees the same.
    /// </summary>
    public static bool CanVaryByTill(Feature feature) => feature is Feature.Delivery;
}

/// <summary>Which features each kind of place starts with.</summary>
public static class Presets
{
    /// <summary>In the order the setup screen offers them.</summary>
    public static IReadOnlyList<Venue> All { get; } =
        [Venue.CounterService, Venue.Cafe, Venue.Restaurant, Venue.Bar, Venue.FoodTruck, Venue.Custom];

    /// <summary>Whether <paramref name="feature"/> is on for <paramref name="venue"/>; a store not set up yet has everything on.</summary>
    public static bool IsOn(Venue? venue, Feature feature) => (venue, feature) switch
    {
        // Drinks are served on the spot; a food truck moves and buys at the market.
        (Venue.Bar, Feature.Delivery) => false,
        (Venue.FoodTruck, Feature.Delivery) => false,
        (Venue.FoodTruck, Feature.Purchasing) => false,
        _ => true,
    };
}
