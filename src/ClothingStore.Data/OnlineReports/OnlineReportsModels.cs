namespace ClothingStore.Data.OnlineReports;

// What the POS uploads to the shop's reports website (cloud/reports-worker). Sent as camelCase JSON; the website only
// displays these, so every figure here is the POS's own (ReportService). No customer names or phone numbers.

/// <summary>The shop, for the website's header and number formatting, and the month's sales target if one is set.</summary>
public sealed record StoreDoc(string Name, string Currency, bool Lbp, decimal LbpRate, string Language, decimal? MonthlyTarget = null)
{
    /// <summary>Google Search Console's verification code, shown on the website's page (see OnlineReportsConfig).</summary>
    public string? GoogleVerification { get; init; }
}

/// <summary>
/// A row of a report table: a payment method, category, product, cashier, size… For products, also what it sold in the
/// period before (for the up/down arrows).
/// </summary>
public sealed record ReportRow(string Name, int Qty, decimal Amount, int Count, decimal Profit, decimal Share)
{
    public int? PreviousQty { get; init; }
    public decimal? PreviousAmount { get; init; }
}

public sealed record ReportTotals(
    decimal TotalSales, decimal GrossSales, decimal Discounts, decimal Tax, decimal NetRevenue, decimal CostOfGoods,
    decimal GrossProfit, decimal MarginPercent, int Transactions, int ItemsSold, decimal AverageBasket, decimal Refunds,
    int ReturnsCount, int VoidedCount, decimal DeliveryFees);

/// <summary>Percent change against the same length of time just before (null when there was nothing before).</summary>
public sealed record ReportChange(decimal? Sales, decimal? Transactions, decimal? Profit);

/// <summary>A day's sales; Profit as on the Reports page (net revenue minus cost, returns on the day they came back).</summary>
public sealed record DayPoint(string Date, int Count, decimal Total, decimal Profit);

/// <summary>Sales in one hour of one weekday (0 = Monday) over the period: when the shop is busy.</summary>
public sealed record HeatCell(int Weekday, int Hour, int Count, decimal Total);

/// <summary>Counts only, never who: sales with a customer attached, how many customers, and how many had bought before.</summary>
public sealed record CustomerCounts(int Sales, int Customers, int Returning);

public sealed record HourPoint(int Hour, int Count, decimal Total);

public sealed record SettlementDoc(DateTime Date, string? Courier, string Method, int Orders, decimal Expected, decimal Received);

public sealed record DrawerCloseDoc(
    DateTime ClosedAt, string Cashier, string? Till, decimal Expected, decimal? Counted, decimal ExpectedLbp, decimal? CountedLbp);

/// <summary>A full report for a period, a day or a month (the POS Reports page, without customer names).</summary>
public sealed record ReportDoc
{
    /// <summary>First and last day covered (yyyy-MM-dd, both included).</summary>
    public required string From { get; init; }
    public required string To { get; init; }
    public required ReportTotals Totals { get; init; }
    public required ReportChange Change { get; init; }
    public IReadOnlyList<DayPoint> ByDay { get; init; } = [];
    public IReadOnlyList<HourPoint> ByHour { get; init; } = [];

    /// <summary>The same length of time just before, day by day (lined up with <see cref="ByDay"/> by position).</summary>
    public IReadOnlyList<DayPoint> PreviousByDay { get; init; } = [];

    /// <summary>For a single day: the same weekday a week before, hour by hour.</summary>
    public IReadOnlyList<HourPoint> PreviousByHour { get; init; } = [];
    public IReadOnlyList<HeatCell> Heatmap { get; init; } = [];
    public CustomerCounts? Customers { get; init; }
    public IReadOnlyList<ReportRow> Payments { get; init; } = [];
    public IReadOnlyList<ReportRow> Categories { get; init; } = [];
    public IReadOnlyList<ReportRow> Products { get; init; } = [];
    public IReadOnlyList<ReportRow> Cashiers { get; init; } = [];
    public IReadOnlyList<ReportRow> Sizes { get; init; } = [];
    public IReadOnlyList<ReportRow> Channels { get; init; } = [];
    public IReadOnlyList<ReportRow> ReturnsByReason { get; init; } = [];
    public IReadOnlyList<ReportRow> TopReturned { get; init; } = [];
    public IReadOnlyList<ReportRow> RefundsByMethod { get; init; } = [];
    public IReadOnlyList<SettlementDoc> Settlements { get; init; } = [];
    public IReadOnlyList<DrawerCloseDoc> DrawerCloses { get; init; } = [];
}

public sealed record OpenDrawerDoc(string? Till, string Cashier, DateTime OpenedAt, int Sales, decimal ExpectedCash, decimal ExpectedCashLbp)
{
    /// <summary>What the drawer's sales came to (every payment method).</summary>
    public decimal TotalSales { get; init; }
}

public sealed record SaleLineDoc(string Product, string Variant, int Qty, decimal Amount);

public sealed record RecentSaleDoc(string Receipt, DateTime Time, string? Cashier, decimal Total, int Items, string Status, string? Channel)
{
    public IReadOnlyList<SaleLineDoc> Lines { get; init; } = [];
}

/// <summary>What is happening now: open drawers, the last sales, recent drawer closes, low stock.</summary>
public sealed record LiveDoc(
    IReadOnlyList<OpenDrawerDoc> OpenDrawers, IReadOnlyList<RecentSaleDoc> RecentSales, IReadOnlyList<DrawerCloseDoc> RecentCloses,
    int LowStockCount, int OutOfStockCount)
{
    /// <summary>The last 14 days including today, for the small trend lines.</summary>
    public IReadOnlyList<DayPoint> Trend14 { get; init; } = [];
}

/// <summary>A variant to reorder, with what it sold in the last 30 days and how many days its stock lasts at that pace.</summary>
public sealed record LowStockDoc(string Product, string Variant, string Sku, int Stock, int Reorder)
{
    public int Sold30 { get; init; }
    public int? DaysLeft { get; init; }
}

public sealed record SlowMoverDoc(string Product, string Variant, int Stock, decimal Value, DateTime? LastSold);

public sealed record StockDoc(
    int Skus, int Units, decimal CostValue, decimal RetailValue, int LowStockCount, int OutOfStockCount, int SlowSinceDays,
    IReadOnlyList<ReportRow> ByCategory, IReadOnlyList<LowStockDoc> LowStock, IReadOnlyList<SlowMoverDoc> SlowMovers);

/// <summary>
/// Every active item, for looking stock up from the website ("do we have the black abaya in 54?"). Compact, as it is
/// the biggest thing the POS sends: one entry per product, its variants as rows of
/// [size, colour, SKU, barcode, in stock, reorder level, own price or null, sold in the last 30 days].
/// Split in parts so a big shop's list still fits; selling prices only, never costs.
/// </summary>
public sealed record CatalogDoc(int Part, int Parts, IReadOnlyList<CatalogProduct> Products);

/// <param name="N">Name.</param>
/// <param name="C">Category.</param>
/// <param name="B">Brand.</param>
/// <param name="S">Style code.</param>
/// <param name="P">Price (a variant with its own price has it in its row).</param>
/// <param name="V">Variants: see <see cref="CatalogDoc"/>.</param>
public sealed record CatalogProduct(string N, string? C, string? B, string? S, decimal P, IReadOnlyList<object?[]> V);

/// <summary>
/// An account allowed to sign in (managers and admins). Never the password: the PBKDF2 salt the browser needs, and a
/// SHA-256 of the stored PBKDF2 key that the website compares with what the browser works out.
/// </summary>
public sealed record OnlineUser(string Username, string Name, string Role, string Salt, int Iterations, string Verifier);
