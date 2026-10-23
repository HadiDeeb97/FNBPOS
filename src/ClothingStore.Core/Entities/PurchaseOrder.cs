namespace ClothingStore.Core.Entities;

public class PurchaseOrder : Entity
{
    public string OrderNumber { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime? ExpectedDate { get; set; }
    public DateTime? OrderedAt { get; set; }
    public DateTime? ReceivedAt { get; set; }
    public PurchaseOrderStatus Status { get; set; } = PurchaseOrderStatus.Draft;

    public int SupplierId { get; set; }
    public Supplier? Supplier { get; set; }

    public int CreatedByUserId { get; set; }
    public string? Notes { get; set; }

    public List<PurchaseOrderLine> Lines { get; set; } = [];

    public decimal Total => Lines.Sum(l => l.LineTotal);
    public int TotalUnits => Lines.Sum(l => l.QuantityOrdered);
}

public class PurchaseOrderLine : Entity
{
    public int PurchaseOrderId { get; set; }
    public PurchaseOrder? PurchaseOrder { get; set; }

    public int ProductVariantId { get; set; }
    public ProductVariant? ProductVariant { get; set; }

    public int QuantityOrdered { get; set; }
    public int QuantityReceived { get; set; }
    public decimal UnitCost { get; set; }

    public decimal LineTotal => QuantityOrdered * UnitCost;
    public int QuantityOutstanding => Math.Max(0, QuantityOrdered - QuantityReceived);
}
