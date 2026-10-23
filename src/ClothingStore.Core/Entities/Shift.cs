namespace ClothingStore.Core.Entities;

/// <summary>
/// A cash-drawer session from opening float to end-of-day count (Z report). The drawer belongs to a till (PC): one open
/// shift per till. Another cashier can continue it (a hand-over) or count and close it before opening their own.
/// </summary>
public class Shift : Entity
{
    /// <summary>Who opened the drawer.</summary>
    public int UserId { get; set; }
    public User? User { get; set; }

    /// <summary>Who runs the drawer now (the opener, or whoever it was handed over to).</summary>
    public int? CurrentUserId { get; set; }
    public User? CurrentUser { get; set; }

    /// <summary>The PC (till) whose drawer this is: its PC ID. Null for shifts opened before tills were tracked.</summary>
    public string? TillId { get; set; }

    /// <summary>The PC's name, for people to read.</summary>
    public string? TillName { get; set; }

    /// <summary>Who counted and closed it (may differ from the opener).</summary>
    public int? ClosedByUserId { get; set; }
    public User? ClosedBy { get; set; }

    public List<ShiftHandover> Handovers { get; set; } = [];

    /// <summary>The cashier running the drawer now.</summary>
    public int RunningUserId => CurrentUserId ?? UserId;

    public DateTime OpenedAt { get; set; } = DateTime.Now;
    public DateTime? ClosedAt { get; set; }
    public ShiftStatus Status { get; set; } = ShiftStatus.Open;

    public decimal OpeningFloat { get; set; }

    /// <summary>Calculated at close: float + cash sales + pay-ins - pay-outs - cash refunds (dollars).</summary>
    public decimal ExpectedCash { get; set; }
    public decimal? CountedCash { get; set; }
    public decimal? Variance => CountedCash is null ? null : CountedCash - ExpectedCash;

    /// <summary>Lebanese pounds in the drawer at opening.</summary>
    public decimal OpeningFloatLbp { get; set; }

    /// <summary>Same as <see cref="ExpectedCash"/> for the Lebanese pounds in the drawer.</summary>
    public decimal ExpectedCashLbp { get; set; }
    public decimal? CountedCashLbp { get; set; }
    public decimal? VarianceLbp => CountedCashLbp is null ? null : CountedCashLbp - ExpectedCashLbp;

    public string? Notes { get; set; }

    /// <summary>Times the drawer was opened with the "Open drawer" button (no sale), shown on the Z report.</summary>
    public int NoSaleOpenings { get; set; }

    public List<CashMovement> CashMovements { get; set; } = [];
}

public class CashMovement : Entity
{
    public int ShiftId { get; set; }
    public Shift? Shift { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public CashMovementType Type { get; set; }

    /// <summary>In <see cref="Currency"/> (dollars or pounds).</summary>
    public decimal Amount { get; set; }
    public CashCurrency Currency { get; set; }
    public string Reason { get; set; } = "";
    public int UserId { get; set; }
}

/// <summary>A drawer passed from one cashier to the next without closing it.</summary>
public class ShiftHandover : Entity
{
    public int ShiftId { get; set; }
    public Shift? Shift { get; set; }
    public DateTime At { get; set; } = DateTime.Now;
    public int FromUserId { get; set; }
    public User? FromUser { get; set; }
    public int ToUserId { get; set; }
    public User? ToUser { get; set; }
}
