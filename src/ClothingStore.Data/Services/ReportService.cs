using ClothingStore.Core;
using ClothingStore.Core.Entities;
using Microsoft.EntityFrameworkCore;
using ClothingStore.Core.Localization;

namespace ClothingStore.Data.Services;

/// <param name="Cost">Cost of the items (for profit columns); 0 where it doesn't apply.</param>
public sealed record NamedAmount(string Name, int Quantity, decimal Amount, int Count = 0, decimal Cost = 0)
{
    /// <summary>This row's part of the total amount of its table, in percent.</summary>
    public decimal SharePercent { get; init; }

    public decimal Profit => Amount - Cost;
    public decimal MarginPercent => Amount == 0 ? 0 : Math.Round(Profit / Amount * 100m, 1);
}

public sealed record DailySales(DateTime Date, int Transactions, decimal Total);

public sealed record HourlySales(int Hour, int Transactions, decimal Total);

public sealed record SettlementRow(DateTime Date, string? Courier, SettlementMethod Method, int Orders, decimal Expected, decimal Received)
{
    public decimal Difference => Received - Expected;
}

/// <summary>Units in or out of the period's sales: sold (positive) or returned (negative).</summary>
internal sealed record ItemFlow(string Product, string? Category, string? Size, int Quantity, decimal Net, decimal Cost);

public sealed record CashMovementRow(DateTime Date, string User, CashMovementType Type, CashCurrency Currency, decimal Amount, string Reason);

public sealed record ShiftCloseRow(
    DateTime ClosedAt, string Cashier, decimal Expected, decimal? Counted, decimal ExpectedLbp, decimal? CountedLbp)
{
    public decimal? Variance => Counted - Expected;
    public decimal? VarianceLbp => CountedLbp - ExpectedLbp;
}

internal sealed record StockLevel(int Id, string Product, string Size, string Color, string Sku, int Stock, int ReorderLevel, decimal Cost);

public sealed record StockAlertRow(string Product, string Variant, string Sku, int Stock, int ReorderLevel, decimal CostValue, DateTime? LastSold);

/// <summary>Variants to reorder, and stock that didn't sell in the period.</summary>
public sealed record StockAlerts(IReadOnlyList<StockAlertRow> LowStock, IReadOnlyList<StockAlertRow> SlowMovers);

public sealed record SalesReport
{
    public DateTime From { get; init; }
    public DateTime To { get; init; }
    public int Transactions { get; init; }
    public int ItemsSold { get; init; }
    public decimal GrossSales { get; init; }
    public decimal Discounts { get; init; }
    public decimal Tax { get; init; }

    /// <summary>Total collected from customers (tax included).</summary>
    public decimal TotalSales { get; init; }

    /// <summary>Sales excluding tax.</summary>
    public decimal NetSales { get; init; }

    public int ReturnsCount { get; init; }
    public decimal Refunds { get; init; }
    public decimal RefundTax { get; init; }

    /// <summary>Net sales excluding tax, after refunds.</summary>
    public decimal NetRevenue { get; init; }

    public decimal CostOfGoods { get; init; }
    public decimal GrossProfit => NetRevenue - CostOfGoods;
    public decimal MarginPercent => NetRevenue == 0 ? 0 : Math.Round(GrossProfit / NetRevenue * 100m, 1);
    public decimal AverageBasket => Transactions == 0 ? 0 : Money.Round(TotalSales / Transactions);
    public int VoidedCount { get; init; }

    public IReadOnlyList<NamedAmount> ByPaymentMethod { get; init; } = [];
    public IReadOnlyList<NamedAmount> ByCategory { get; init; } = [];
    public IReadOnlyList<NamedAmount> TopProducts { get; init; } = [];
    public IReadOnlyList<NamedAmount> ByCashier { get; init; } = [];
    public IReadOnlyList<NamedAmount> BySize { get; init; } = [];

    /// <summary>In store vs WhatsApp, Instagram... (Count = sales).</summary>
    public IReadOnlyList<NamedAmount> ByChannel { get; init; } = [];

    /// <summary>Delivery charges collected on online orders (part of <see cref="TotalSales"/>).</summary>
    public decimal DeliveryFees { get; init; }
    public IReadOnlyList<DailySales> ByDay { get; init; } = [];
    public IReadOnlyList<HourlySales> ByHour { get; init; } = [];
    public IReadOnlyList<NamedAmount> TopCustomers { get; init; } = [];

    // Returns
    public IReadOnlyList<NamedAmount> ReturnsByReason { get; init; } = [];
    public IReadOnlyList<NamedAmount> TopReturned { get; init; } = [];
    public IReadOnlyList<NamedAmount> RefundsByMethod { get; init; } = [];

    // Delivery companies
    public IReadOnlyList<SettlementRow> Settlements { get; init; } = [];

    // Cash
    public IReadOnlyList<CashMovementRow> CashMovements { get; init; } = [];
    public IReadOnlyList<ShiftCloseRow> ShiftCloses { get; init; } = [];

    // The same length of time just before, for comparison.
    public decimal PreviousTotalSales { get; init; }
    public int PreviousTransactions { get; init; }
    public decimal PreviousGrossProfit { get; init; }

    public decimal? SalesChangePercent => Change(TotalSales, PreviousTotalSales);
    public decimal? TransactionsChangePercent => Change(Transactions, PreviousTransactions);
    public decimal? ProfitChangePercent => Change(GrossProfit, PreviousGrossProfit);

    private static decimal? Change(decimal now, decimal before) =>
        before == 0 ? null : Math.Round((now - before) / Math.Abs(before) * 100m, 1);
}

public sealed record InventoryValuation(
    int Skus,
    int Units,
    decimal CostValue,
    decimal RetailValue,
    int LowStockCount,
    int OutOfStockCount,
    IReadOnlyList<NamedAmount> ByCategoryCost);

public class ReportService(IDbContextFactory<PosDbContext> factory)
{
    public async Task<SalesReport> GetSalesReportAsync(DateTime from, DateTime to, CancellationToken ct = default)
    {
        var report = await BuildAsync(from, to, details: true, ct);
        // Same length of time just before, for the "vs previous period" figures.
        var previous = await BuildAsync(from - (to - from), from, details: false, ct);
        return report with
        {
            PreviousTotalSales = previous.TotalSales,
            PreviousTransactions = previous.Transactions,
            PreviousGrossProfit = previous.GrossProfit,
        };
    }

    private async Task<SalesReport> BuildAsync(DateTime from, DateTime to, bool details, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);

        // Only the columns the report needs: whole sales with their products made long ranges slow.
        var sales = await db.Sales.AsNoTracking()
            .Where(s => s.CreatedAt >= from && s.CreatedAt < to)
            .Select(s => new
            {
                s.Id, s.Status, s.CreatedAt, s.Subtotal, s.DiscountTotal, s.TaxTotal, s.Total, s.Channel, s.DeliveryFee,
                Cashier = s.User != null ? s.User.FullName : null,
                Customer = s.Customer != null ? s.Customer.FirstName + " " + s.Customer.LastName : null,
                s.CustomerId,
            })
            .ToListAsync(ct);
        var completed = sales.Where(s => s.Status == SaleStatus.Completed).ToList();

        var lines = await db.SaleLines.AsNoTracking()
            .Where(l => l.Sale!.CreatedAt >= from && l.Sale.CreatedAt < to && l.Sale.Status == SaleStatus.Completed)
            .Select(l => new
            {
                l.SaleId, l.Quantity, l.LineTotal, l.TaxAmount, l.UnitCost, l.ProductName, l.CategoryName,
                Size = l.ProductVariant != null ? l.ProductVariant.Size : null,
            })
            .ToListAsync(ct);

        // SQL Server sums decimals exactly, so payment totals are grouped on the server.
        var payments = await db.Payments.AsNoTracking()
            .Where(p => p.Sale!.CreatedAt >= from && p.Sale.CreatedAt < to && p.Sale.Status == SaleStatus.Completed)
            .GroupBy(p => p.Method)
            .Select(g => new { Method = g.Key, Amount = g.Sum(p => p.Amount), Count = g.Count() })
            .ToListAsync(ct);

        var returns = await db.Returns.AsNoTracking()
            .Where(r => r.CreatedAt >= from && r.CreatedAt < to)
            .Select(r => new { r.TotalRefund, r.TaxRefund, r.Reason })
            .ToListAsync(ct);
        var returnedCost = (await db.ReturnLines.AsNoTracking()
                .Where(rl => rl.SaleReturn!.CreatedAt >= from && rl.SaleReturn.CreatedAt < to && rl.Restocked)
                .Select(rl => new { rl.Quantity, UnitCost = rl.SaleLine != null ? rl.SaleLine.UnitCost : 0m })
                .ToListAsync(ct))
            .Sum(rl => rl.Quantity * rl.UnitCost);

        // Items sold minus items returned in the period, so category / product / size figures add up to the overall
        // figures (which take returns off too). Returned units only take their cost back when they went back on the shelf.
        var returnedItems = await db.ReturnLines.AsNoTracking()
            .Where(rl => rl.SaleReturn!.CreatedAt >= from && rl.SaleReturn.CreatedAt < to)
            .Select(rl => new
            {
                rl.Quantity, Net = rl.RefundAmount - rl.TaxAmount, Cost = rl.Restocked && rl.SaleLine != null ? rl.SaleLine.UnitCost * rl.Quantity : 0m,
                ProductName = rl.SaleLine != null ? rl.SaleLine.ProductName : "",
                CategoryName = rl.SaleLine != null ? rl.SaleLine.CategoryName : null,
                Size = rl.SaleLine != null && rl.SaleLine.ProductVariant != null ? rl.SaleLine.ProductVariant.Size : null,
            })
            .ToListAsync(ct);
        var items = lines
            .Select(l => new ItemFlow(l.ProductName, l.CategoryName, l.Size, l.Quantity, l.LineTotal - l.TaxAmount, l.UnitCost * l.Quantity))
            .Concat(returnedItems.Select(r => new ItemFlow(r.ProductName, r.CategoryName, r.Size, -r.Quantity, -r.Net, -r.Cost)))
            .ToList();

        var itemsBySale = lines.GroupBy(l => l.SaleId).ToDictionary(g => g.Key, g => g.Sum(l => l.Quantity));
        var extra = details ? await DetailsAsync(db, from, to, ct) : null;
        var totalSales = completed.Sum(s => s.Total);
        var tax = completed.Sum(s => s.TaxTotal);
        var refunds = returns.Sum(r => r.TotalRefund);
        var refundTax = returns.Sum(r => r.TaxRefund);

        return new SalesReport
        {
            From = from,
            To = to,
            Transactions = completed.Count,
            VoidedCount = sales.Count - completed.Count,
            ItemsSold = lines.Sum(l => l.Quantity),
            GrossSales = completed.Sum(s => s.Subtotal),
            Discounts = completed.Sum(s => s.DiscountTotal),
            Tax = tax,
            TotalSales = totalSales,
            NetSales = totalSales - tax,
            ReturnsCount = returns.Count,
            Refunds = refunds,
            RefundTax = refundTax,
            NetRevenue = totalSales - tax - (refunds - refundTax),
            CostOfGoods = lines.Sum(l => l.UnitCost * l.Quantity) - returnedCost,

            ByPaymentMethod = WithShares(payments
                .Select(p => new NamedAmount(Loc.EnumText(p.Method), 0, p.Amount, p.Count))
                .OrderByDescending(x => x.Amount)),

            ByCategory = WithShares(items
                .GroupBy(l => l.Category ?? Loc.T("Reports.Uncategorised"))
                .Select(g => new NamedAmount(g.Key, g.Sum(l => l.Quantity), g.Sum(l => l.Net), 0, g.Sum(l => l.Cost)))
                .OrderByDescending(x => x.Amount)),

            TopProducts = items
                .GroupBy(l => l.Product)
                .Select(g => new NamedAmount(g.Key, g.Sum(l => l.Quantity), g.Sum(l => l.Net), 0, g.Sum(l => l.Cost)))
                .Where(x => x.Quantity > 0)
                .OrderByDescending(x => x.Quantity).ThenByDescending(x => x.Amount)
                .Take(100).ToList(),

            BySize = WithShares(items
                .GroupBy(l => string.IsNullOrWhiteSpace(l.Size) ? "-" : l.Size)
                .Select(g => new NamedAmount(g.Key, g.Sum(l => l.Quantity), g.Sum(l => l.Net)))
                .OrderByDescending(x => x.Quantity)),

            ByCashier = WithShares(completed
                .GroupBy(s => s.Cashier ?? "?")
                .Select(g => new NamedAmount(g.Key, g.Sum(s => itemsBySale.GetValueOrDefault(s.Id)), g.Sum(s => s.Total), g.Count()))
                .OrderByDescending(x => x.Amount)),

            DeliveryFees = completed.Sum(s => s.DeliveryFee),

            ByChannel = WithShares(completed
                .GroupBy(s => s.Channel)
                .Select(g => new NamedAmount(Loc.EnumText(g.Key), g.Sum(s => itemsBySale.GetValueOrDefault(s.Id)), g.Sum(s => s.Total), g.Count()))
                .OrderByDescending(x => x.Amount)),

            ByDay = completed
                .GroupBy(s => s.CreatedAt.Date)
                .Select(g => new DailySales(g.Key, g.Count(), g.Sum(s => s.Total)))
                .OrderBy(d => d.Date).ToList(),

            ByHour = completed
                .GroupBy(s => s.CreatedAt.Hour)
                .Select(g => new HourlySales(g.Key, g.Count(), g.Sum(s => s.Total)))
                .OrderBy(h => h.Hour).ToList(),

            // By customer, not by name: two customers called the same are two rows.
            TopCustomers = completed
                .Where(s => s.CustomerId != null)
                .GroupBy(s => s.CustomerId)
                .Select(g => new NamedAmount(g.First().Customer!.Trim(), g.Sum(s => itemsBySale.GetValueOrDefault(s.Id)), g.Sum(s => s.Total), g.Count()))
                .OrderByDescending(x => x.Amount).Take(50).ToList(),

            ReturnsByReason = returns
                .GroupBy(r => string.IsNullOrWhiteSpace(r.Reason) ? Loc.T("Reports.NoReason") : r.Reason.Trim())
                .Select(g => new NamedAmount(g.Key, 0, g.Sum(r => r.TotalRefund), g.Count()))
                .OrderByDescending(x => x.Count).ToList(),

            TopReturned = extra?.TopReturned ?? [],
            RefundsByMethod = extra?.RefundsByMethod ?? [],
            Settlements = extra?.Settlements ?? [],
            CashMovements = extra?.CashMovements ?? [],
            ShiftCloses = extra?.ShiftCloses ?? [],
        };
    }

    private sealed record Details(
        IReadOnlyList<NamedAmount> TopReturned, IReadOnlyList<NamedAmount> RefundsByMethod, IReadOnlyList<SettlementRow> Settlements,
        IReadOnlyList<CashMovementRow> CashMovements, IReadOnlyList<ShiftCloseRow> ShiftCloses);

    /// <summary>The parts only shown for the chosen period (not needed for the comparison).</summary>
    private static async Task<Details> DetailsAsync(PosDbContext db, DateTime from, DateTime to, CancellationToken ct)
    {
        var returnedLines = await db.ReturnLines.AsNoTracking()
            .Where(rl => rl.SaleReturn!.CreatedAt >= from && rl.SaleReturn.CreatedAt < to)
            .GroupBy(rl => rl.SaleLine!.ProductName)
            .Select(g => new { Name = g.Key, Qty = g.Sum(x => x.Quantity), Amount = g.Sum(x => x.RefundAmount), Count = g.Count() })
            .OrderByDescending(x => x.Qty).Take(50)
            .ToListAsync(ct);

        var refundMethods = await db.ReturnRefunds.AsNoTracking()
            .Where(r => r.SaleReturn!.CreatedAt >= from && r.SaleReturn.CreatedAt < to)
            .GroupBy(r => r.Method)
            .Select(g => new { Method = g.Key, Amount = g.Sum(r => r.Amount), Count = g.Count() })
            .ToListAsync(ct);

        var settlements = await db.DeliverySettlements.AsNoTracking()
            .Where(d => d.CreatedAt >= from && d.CreatedAt < to)
            .OrderByDescending(d => d.CreatedAt)
            .Select(d => new SettlementRow(d.CreatedAt, d.Courier, d.Method, d.Sales.Count, d.Expected, d.Received))
            .ToListAsync(ct);

        var movements = await db.CashMovements.AsNoTracking()
            .Where(m => m.CreatedAt >= from && m.CreatedAt < to)
            .OrderByDescending(m => m.CreatedAt)
            .Select(m => new CashMovementRow(
                m.CreatedAt, db.Users.Where(u => u.Id == m.UserId).Select(u => u.FullName).FirstOrDefault() ?? "",
                m.Type, m.Currency, m.Amount, m.Reason))
            .ToListAsync(ct);

        var closes = await db.Shifts.AsNoTracking()
            .Where(s => s.Status == ShiftStatus.Closed && s.ClosedAt >= from && s.ClosedAt < to)
            .OrderByDescending(s => s.ClosedAt)
            .Select(s => new ShiftCloseRow(s.ClosedAt!.Value, s.User != null ? s.User.FullName : "", s.ExpectedCash, s.CountedCash, s.ExpectedCashLbp, s.CountedCashLbp))
            .ToListAsync(ct);

        return new Details(
            returnedLines.Select(x => new NamedAmount(x.Name, x.Qty, x.Amount, x.Count)).ToList(),
            refundMethods.Select(x => new NamedAmount(Loc.EnumText(x.Method), 0, x.Amount, x.Count)).OrderByDescending(x => x.Amount).ToList(),
            settlements, movements, closes);
    }

    /// <summary>Variants at or below their reorder level, and stock with no sales between the two dates.</summary>
    public async Task<StockAlerts> GetStockAlertsAsync(DateTime from, DateTime to, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var variants = await db.ProductVariants.AsNoTracking()
            .Where(v => v.IsActive && v.Product!.IsActive)
            .Select(v => new StockLevel(v.Id, v.Product!.Name, v.Size, v.Color, v.Sku, v.StockQuantity, v.ReorderLevel, v.CostOverride ?? v.Product.Cost))
            .ToListAsync(ct);

        var lastSold = await db.SaleLines.AsNoTracking()
            .Where(l => l.Sale!.Status == SaleStatus.Completed)
            .GroupBy(l => l.ProductVariantId)
            .Select(g => new { Id = g.Key, Last = g.Max(l => l.Sale!.CreatedAt) })
            .ToDictionaryAsync(x => x.Id, x => x.Last, ct);

        StockAlertRow Row(StockLevel v) => new(
            v.Product, Core.Entities.ProductVariant.DescribeVariant(v.Size, v.Color), v.Sku, v.Stock, v.ReorderLevel,
            v.Stock > 0 ? v.Stock * v.Cost : 0m, lastSold.TryGetValue(v.Id, out var last) ? last : null);

        // Sold at any time inside the dates (not just "last sold" inside them: an item sold in the period and again
        // since would otherwise look unsold for a past period).
        var soldInPeriod = (await db.SaleLines.AsNoTracking()
            .Where(l => l.Sale!.Status == SaleStatus.Completed && l.Sale.CreatedAt >= from && l.Sale.CreatedAt < to)
            .Select(l => l.ProductVariantId).Distinct().ToListAsync(ct)).ToHashSet();
        bool SoldInPeriod(StockLevel v) => soldInPeriod.Contains(v.Id);

        var low = variants.Where(v => v.Stock <= v.ReorderLevel)
            .OrderBy(v => v.Stock).ThenBy(v => v.Product)
            .Take(500).Select(Row).ToList();
        var slow = variants
            .Where(v => v.Stock > 0 && !SoldInPeriod(v))
            .OrderByDescending(v => v.Stock * v.Cost)
            .Take(500).Select(Row).ToList();
        return new StockAlerts(low, slow);
    }

    public async Task<InventoryValuation> GetInventoryValuationAsync(CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var variants = await db.ProductVariants.AsNoTracking()
            .Include(v => v.Product).ThenInclude(p => p!.Category)
            .Where(v => v.IsActive && v.Product!.IsActive)
            .ToListAsync(ct);

        var inStock = variants.Where(v => v.StockQuantity > 0).ToList();
        return new InventoryValuation(
            Skus: variants.Count,
            Units: inStock.Sum(v => v.StockQuantity),
            CostValue: inStock.Sum(v => v.StockQuantity * v.EffectiveCost),
            RetailValue: inStock.Sum(v => v.StockQuantity * v.EffectivePrice),
            LowStockCount: variants.Count(v => v.IsLowStock && v.StockQuantity > 0),
            OutOfStockCount: variants.Count(v => v.StockQuantity <= 0),
            ByCategoryCost: inStock
                .GroupBy(v => v.Product!.Category?.Name ?? Loc.T("Reports.Uncategorised"))
                .Select(g => new NamedAmount(g.Key, g.Sum(v => v.StockQuantity), g.Sum(v => v.StockQuantity * v.EffectiveCost)))
                .OrderByDescending(x => x.Amount).ToList());
    }

    /// <summary>Adds each row's share of the table's total amount.</summary>
    private static List<NamedAmount> WithShares(IEnumerable<NamedAmount> rows)
    {
        var list = rows.ToList();
        var total = list.Sum(r => r.Amount);
        return list.Select(r => r with { SharePercent = total == 0 ? 0 : Math.Round(r.Amount / total * 100m, 1) }).ToList();
    }
}
