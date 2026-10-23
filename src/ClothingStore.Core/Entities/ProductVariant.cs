namespace ClothingStore.Core.Entities;

public class ProductVariant : Entity
{
    public int ProductId { get; set; }
    public Product? Product { get; set; }

    public string Size { get; set; } = "";
    public string Color { get; set; } = "";
    public string Sku { get; set; } = "";
    public string? Barcode { get; set; }

    /// <summary>Overrides <see cref="Product.Price"/> when set (e.g. XXL priced higher).</summary>
    public decimal? PriceOverride { get; set; }

    /// <summary>Overrides <see cref="Product.Cost"/> when set.</summary>
    public decimal? CostOverride { get; set; }

    public int StockQuantity { get; set; }
    public int ReorderLevel { get; set; } = 2;
    public bool IsActive { get; set; } = true;

    /// <summary>Optimistic concurrency token so two tills can't oversell the same unit.</summary>
    public int Version { get; set; }

    public decimal EffectivePrice => PriceOverride ?? Product?.Price ?? 0m;
    public decimal EffectiveCost => CostOverride ?? Product?.Cost ?? 0m;
    public bool IsLowStock => StockQuantity <= ReorderLevel;

    public string Description => DescribeVariant(Size, Color);

    public string DisplayName => Product is null ? Description : $"{Product.Name} ({Description})";

    public static string DescribeVariant(string? size, string? color)
    {
        var parts = new[] { size, color }.Where(p => !string.IsNullOrWhiteSpace(p));
        return string.Join(" / ", parts);
    }
}
