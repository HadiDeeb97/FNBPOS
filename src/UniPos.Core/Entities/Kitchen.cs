namespace UniPos.Core.Entities;

/// <summary>Where items are made (Kitchen, Bar, Drinks): each has its own printer for the tickets of its items.</summary>
public class Station : Entity
{
    public string Name { get; set; } = "";

    /// <summary>
    /// The Windows printer its tickets go to, the same for every till (a shared network printer); a till can use
    /// another in its own settings. Null: tickets wait on the Kitchen page until a printer is set.
    /// </summary>
    public string? PrinterName { get; set; }

    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }
}

/// <summary>
/// What one station has to make for one order. Saved with the sale before it is printed, so an order is never lost when
/// a printer is off or out of paper: it waits as "not printed" on the Kitchen page and can be printed again.
/// </summary>
public class KitchenTicket : Entity
{
    public int SaleId { get; set; }
    public Sale? Sale { get; set; }

    public int StationId { get; set; }
    public Station? Station { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    /// <summary>Copies from the sale, as the kitchen reads them.</summary>
    public int? OrderNumber { get; set; }
    public OrderType OrderType { get; set; }
    public string? CallName { get; set; }

    /// <summary>When it last printed; null while it hasn't.</summary>
    public DateTime? PrintedAt { get; set; }
    public int PrintCount { get; set; }

    /// <summary>Why the last attempt failed (printer off, no printer set...); null after a good print.</summary>
    public string? PrintError { get; set; }

    public List<KitchenTicketLine> Lines { get; set; } = [];

    public bool IsPrinted => PrintedAt is not null;
}

public class KitchenTicketLine : Entity
{
    public int KitchenTicketId { get; set; }
    public KitchenTicket? KitchenTicket { get; set; }

    public string ProductName { get; set; } = "";
    public string Options { get; set; } = "";
    public string? Note { get; set; }
    public int Quantity { get; set; }
}
