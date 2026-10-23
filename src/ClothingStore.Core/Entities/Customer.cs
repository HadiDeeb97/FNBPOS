namespace ClothingStore.Core.Entities;

public class Customer : Entity
{
    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public DateTime? Birthday { get; set; }
    public string? Notes { get; set; }

    /// <summary>Street, building, floor... (for deliveries).</summary>
    public string? Address { get; set; }

    /// <summary>State / governorate.</summary>
    public int? RegionId { get; set; }
    public Region? Region { get; set; }

    // Filled by the customer list (not stored): what they bought, net of refunds, and how often.
    public decimal TotalSpent { get; set; }
    public int Visits { get; set; }
    public DateTime? LastVisit { get; set; }
    public int LoyaltyPoints { get; set; }
    public decimal StoreCredit { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public int Version { get; set; }

    public string FullName => $"{FirstName} {LastName}".Trim();

    /// <summary>Address and state on one line, for delivery notes.</summary>
    public string? FullAddress => string.Join(", ", new[] { Address, Region?.DisplayName }.Where(s => !string.IsNullOrWhiteSpace(s))) is { Length: > 0 } a ? a : null;

    public override string ToString() =>
        string.IsNullOrWhiteSpace(Phone) ? FullName : $"{FullName} ({Phone})";
}
