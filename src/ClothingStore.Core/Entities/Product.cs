namespace ClothingStore.Core.Entities;

/// <summary>
/// A style (e.g. "Slim Fit Oxford Shirt"). Sellable units are its <see cref="ProductVariant"/>s
/// (one per size/colour combination), each with its own SKU, barcode and stock level.
/// </summary>
public class Product : Entity
{
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public string? Brand { get; set; }
    public string? StyleCode { get; set; }
    public Gender Gender { get; set; } = Gender.Unisex;
    public string? Season { get; set; }
    public string? Material { get; set; }

    public int CategoryId { get; set; }
    public Category? Category { get; set; }

    public int? SupplierId { get; set; }
    public Supplier? Supplier { get; set; }

    /// <summary>Default selling price for all variants (variants may override).</summary>
    public decimal Price { get; set; }

    /// <summary>Default unit cost for all variants (variants may override).</summary>
    public decimal Cost { get; set; }

    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public List<ProductVariant> Variants { get; set; } = [];
}
