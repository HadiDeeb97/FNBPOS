namespace ClothingStore.Core.Entities;

public class SaleReturn : Entity
{
    public string ReturnNumber { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public int SaleId { get; set; }
    public Sale? Sale { get; set; }

    public int UserId { get; set; }
    public User? User { get; set; }

    public int? CustomerId { get; set; }
    public Customer? Customer { get; set; }

    public int? ShiftId { get; set; }

    public decimal TotalRefund { get; set; }
    public decimal TaxRefund { get; set; }

    /// <summary>LBP per dollar used for any part paid out in pounds; 0 when none was.</summary>
    public decimal ExchangeRate { get; set; }
    public string? Reason { get; set; }

    /// <summary>Points the customer paid with that were given back.</summary>
    public int LoyaltyPointsRestored { get; set; }

    /// <summary>Points earned on the sale that were taken back because the purchase was refunded.</summary>
    public int LoyaltyPointsRemoved { get; set; }

    public List<SaleReturnLine> Lines { get; set; } = [];

    /// <summary>How <see cref="TotalRefund"/> was paid out, one row per original tender.</summary>
    public List<SaleReturnRefund> Refunds { get; set; } = [];
}

public class SaleReturnRefund : Entity
{
    public int SaleReturnId { get; set; }
    public SaleReturn? SaleReturn { get; set; }

    /// <summary>The tender on the original sale this money came from.</summary>
    public PaymentMethod Source { get; set; }

    /// <summary>How it was given back.</summary>
    public RefundMethod Method { get; set; }

    /// <summary>Dollar value of this part of the refund.</summary>
    public decimal Amount { get; set; }

    /// <summary>Lebanese pounds actually handed out, for <see cref="RefundMethod.CashLbp"/>.</summary>
    public decimal AmountLbp { get; set; }
}

public class SaleReturnLine : Entity
{
    public int SaleReturnId { get; set; }
    public SaleReturn? SaleReturn { get; set; }

    public int SaleLineId { get; set; }
    public SaleLine? SaleLine { get; set; }

    public int Quantity { get; set; }
    public decimal RefundAmount { get; set; }
    public decimal TaxAmount { get; set; }
    public bool Restocked { get; set; }
}
