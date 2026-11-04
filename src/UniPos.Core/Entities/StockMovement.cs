namespace UniPos.Core.Entities;

public class StockMovement : Entity
{
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public int ProductId { get; set; }
    public Product? Product { get; set; }

    public int? UserId { get; set; }
    public User? User { get; set; }

    public StockMovementType Type { get; set; }
    public int QuantityChange { get; set; }
    public int QuantityAfter { get; set; }

    /// <summary>Receipt / return / PO number the movement relates to.</summary>
    public string? Reference { get; set; }
    public string? Notes { get; set; }
}
