namespace ClothingStore.Core.Entities;

public class Category : Entity
{
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;

    public List<Product> Products { get; set; } = [];

    public override string ToString() => Name;
}
