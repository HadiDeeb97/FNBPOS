namespace ClothingStore.Core.Entities;

/// <summary>
/// A delivery company or driver online orders are handed to. Sales keep the name in <see cref="Sale.Courier"/>, so
/// history stays readable if a partner is later removed; renaming a partner renames it on its orders too.
/// </summary>
public class DeliveryPartner : Entity
{
    public string Name { get; set; } = "";
    public DeliveryPartnerKind Kind { get; set; }
    public string? ContactName { get; set; }
    public string? Phone { get; set; }
    public string? Phone2 { get; set; }
    public string? Address { get; set; }

    /// <summary>Delivery fee usually charged with this partner, filled in on the online order window.</summary>
    public decimal? DefaultFee { get; set; }

    public string? Notes { get; set; }
    public bool IsActive { get; set; } = true;

    public override string ToString() => Name;
}
