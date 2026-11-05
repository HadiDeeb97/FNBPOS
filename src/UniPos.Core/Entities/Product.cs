namespace UniPos.Core.Entities;

/// <summary>
/// An item on the menu ("Nutella crepe", "Espresso", "Water 500 ml"). Choices made when it is ordered (size, fillings,
/// "no sugar") come from its <see cref="ModifierGroups"/>, which can be shared by many items.
/// </summary>
public class Product : Entity
{
    public string Name { get; set; } = "";
    public string? Description { get; set; }

    public int CategoryId { get; set; }
    public Category? Category { get; set; }

    public int? SupplierId { get; set; }
    public Supplier? Supplier { get; set; }

    /// <summary>Where it is made (kitchen, bar...): its ticket prints there. Null: no ticket (packaged goods).</summary>
    public int? StationId { get; set; }
    public Station? Station { get; set; }

    /// <summary>Price before any options.</summary>
    public decimal Price { get; set; }

    /// <summary>Cost of one, before any options.</summary>
    public decimal Cost { get; set; }

    /// <summary>Short code typed at the till or printed on reports ("101"); made automatically when left empty.</summary>
    public string Sku { get; set; } = "";

    /// <summary>The maker's barcode on packaged goods (drinks, snacks), so the scanner adds them.</summary>
    public string? Barcode { get; set; }

    /// <summary>Ran out for now: still on the menu, but can't be ordered until it is back.</summary>
    public bool SoldOut { get; set; }

    /// <summary>
    /// Counted in stock (a can of soda, a croissant from the bakery). Items made to order are not: selling them never
    /// runs out of stock, and they stay off the stock screens.
    /// </summary>
    public bool TrackStock { get; set; }

    public int StockQuantity { get; set; }
    public int ReorderLevel { get; set; } = 2;

    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    /// <summary>Optimistic concurrency token so two tills can't oversell the same unit.</summary>
    public int Version { get; set; }

    public List<ProductModifierGroup> ModifierGroups { get; set; } = [];

    public bool IsLowStock => TrackStock && StockQuantity <= ReorderLevel;
    public bool HasOptions => ModifierGroups.Count > 0;
}

/// <summary>A modifier group offered with an item, in the order the till asks them.</summary>
public class ProductModifierGroup
{
    public int ProductId { get; set; }
    public Product? Product { get; set; }

    public int ModifierGroupId { get; set; }
    public ModifierGroup? ModifierGroup { get; set; }

    public int SortOrder { get; set; }
}
