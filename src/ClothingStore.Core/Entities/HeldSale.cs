namespace ClothingStore.Core.Entities;

/// <summary>A parked cart that can be resumed later (customer went to fitting room, etc.).</summary>
public class HeldSale : Entity
{
    public string Label { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public int UserId { get; set; }
    public int? CustomerId { get; set; }

    /// <summary>JSON serialized cart contents.</summary>
    public string Payload { get; set; } = "";
}
