namespace ClothingStore.Core.Entities;

/// <summary>
/// Money a delivery company handed over for online orders it collected (usually several at once). The sales it
/// covers point to it through <see cref="Sale.DeliverySettlementId"/>.
/// </summary>
public class DeliverySettlement : Entity
{
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public int UserId { get; set; }
    public User? User { get; set; }

    /// <summary>Drawer the cash went into (cash settlements only).</summary>
    public int? ShiftId { get; set; }

    public string? Courier { get; set; }
    public SettlementMethod Method { get; set; }

    /// <summary>What the covered sales still owed (dollars).</summary>
    public decimal Expected { get; set; }

    /// <summary>What actually came in, in dollars (for LBP: its dollar value at the rate used).</summary>
    public decimal Received { get; set; }

    /// <summary>Pounds actually received, for <see cref="SettlementMethod.CashLbp"/>.</summary>
    public decimal ReceivedLbp { get; set; }

    public decimal ExchangeRate { get; set; }

    /// <summary>Received minus expected: negative when the company kept a fee or paid short.</summary>
    public decimal Difference => Received - Expected;

    public string? Reference { get; set; }

    public List<Sale> Sales { get; set; } = [];
}
