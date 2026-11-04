namespace UniPos.Core.Entities;

/// <summary>
/// A question asked when an item is ordered, with its answers: "Size" (pick 1: Small, Medium +$0.50, Large +$1),
/// "Fillings" (pick 1 to 3), "Extras" (any), "Remove" (No onions, No sugar). Shared by every item it is attached to.
/// </summary>
public class ModifierGroup : Entity
{
    public string Name { get; set; } = "";

    /// <summary>At least this many must be picked; 0 = optional.</summary>
    public int MinChoices { get; set; }

    /// <summary>At most this many may be picked; 0 = no limit.</summary>
    public int MaxChoices { get; set; } = 1;

    public bool IsActive { get; set; } = true;

    public List<Modifier> Options { get; set; } = [];

    public bool IsRequired => MinChoices > 0;
}

/// <summary>One answer in a <see cref="ModifierGroup"/>.</summary>
public class Modifier : Entity
{
    public int ModifierGroupId { get; set; }
    public ModifierGroup? ModifierGroup { get; set; }

    public string Name { get; set; } = "";

    /// <summary>Added to the item's price (0 for most).</summary>
    public decimal Price { get; set; }

    /// <summary>Added to the item's cost.</summary>
    public decimal Cost { get; set; }

    /// <summary>Ran out for now (no more bananas): shown but can't be picked.</summary>
    public bool SoldOut { get; set; }

    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
}
