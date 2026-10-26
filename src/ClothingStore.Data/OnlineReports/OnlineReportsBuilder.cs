using System.Security.Cryptography;
using ClothingStore.Core;
using ClothingStore.Core.Localization;
using ClothingStore.Data.Services;
using Microsoft.EntityFrameworkCore;

namespace ClothingStore.Data.OnlineReports;

/// <summary>
/// Turns the POS's reports into the documents the website shows. The figures come from <see cref="ReportService"/> (the
/// Reports page's own code), so the website and the POS always agree.
/// </summary>
public class OnlineReportsBuilder(IDbContextFactory<PosDbContext> factory, ReportService reports, ShiftService shifts)
{
    public const int SlowSinceDays = 60;
    private const int TopProducts = 20;
    private const int TableRows = 50;
    private const int StockRows = 200;
    private const int RecentSales = 50;
    private const int RecentCloses = 20;

    /// <summary>The standard periods, as on the Reports page's quick buttons (weeks start on Monday).</summary>
    public static readonly IReadOnlyList<string> Periods = ["today", "yesterday", "week", "lastweek", "month", "lastmonth", "year"];

    /// <summary>First day and the day after the last of a standard period.</summary>
    public static (DateTime From, DateTime To) PeriodRange(string period, DateTime today)
    {
        today = today.Date;
        var monday = today.AddDays(-((7 + (int)today.DayOfWeek - 1) % 7));
        var month = new DateTime(today.Year, today.Month, 1);
        return period switch
        {
            "today" => (today, today.AddDays(1)),
            "yesterday" => (today.AddDays(-1), today),
            "week" => (monday, today.AddDays(1)),
            "lastweek" => (monday.AddDays(-7), monday),
            "month" => (month, today.AddDays(1)),
            "lastmonth" => (month.AddMonths(-1), month),
            "year" => (new DateTime(today.Year, 1, 1), today.AddDays(1)),
            _ => throw new ArgumentOutOfRangeException(nameof(period), period, null),
        };
    }

    public async Task<StoreDoc> StoreAsync(CancellationToken ct = default)
    {
        // Read here rather than through SettingsService, whose copy the screens share: this runs in the background.
        await using var db = await factory.CreateDbContextAsync(ct);
        var s = await db.Settings.AsNoTracking().OrderBy(x => x.Id).FirstOrDefaultAsync(ct) ?? new Core.Entities.StoreSettings();
        return new StoreDoc(s.StoreName, s.CurrencySymbol, s.LbpEnabled, s.LbpRate, Loc.Normalize(s.ReceiptLanguage));
    }

    /// <summary>The report from <paramref name="from"/> up to (not including) <paramref name="to"/>.</summary>
    public async Task<ReportDoc> ReportAsync(DateTime from, DateTime to, CancellationToken ct = default)
    {
        var r = await reports.GetSalesReportAsync(from, to, ct);
        var previousFrom = from - (to - from);
        var oneDay = to - from == TimeSpan.FromDays(1);
        var products = Rows(r.TopProducts.Take(TopProducts));
        var before = await ProductSalesAsync(previousFrom, from, products.Select(p => p.Name).ToList(), ct);
        return new ReportDoc
        {
            From = Day(from),
            To = Day(to.AddDays(-1)),
            Totals = new ReportTotals(
                r.TotalSales, r.GrossSales, r.Discounts, r.Tax, r.NetRevenue, r.CostOfGoods, r.GrossProfit, r.MarginPercent,
                r.Transactions, r.ItemsSold, r.AverageBasket, r.Refunds, r.ReturnsCount, r.VoidedCount, r.DeliveryFees),
            Change = new ReportChange(r.SalesChangePercent, r.TransactionsChangePercent, r.ProfitChangePercent),
            ByDay = await DailyAsync(from, to, ct),
            ByHour = r.ByHour.Select(h => new HourPoint(h.Hour, h.Transactions, h.Total)).ToList(),
            PreviousByDay = oneDay ? [] : await DailyAsync(previousFrom, from, ct),
            PreviousByHour = oneDay ? await HourlyAsync(from.AddDays(-7), from.AddDays(-6), ct) : [],
            Heatmap = oneDay ? [] : await HeatmapAsync(from, to, ct),
            Customers = await CustomersAsync(from, to, ct),
            Payments = Rows(r.ByPaymentMethod),
            Categories = Rows(r.ByCategory),
            Products = products.Select(p => before.TryGetValue(p.Name, out var b)
                ? p with { PreviousQty = b.Qty, PreviousAmount = b.Amount }
                : p with { PreviousQty = 0, PreviousAmount = 0 }).ToList(),
            Cashiers = Rows(r.ByCashier),
            Sizes = Rows(r.BySize),
            Channels = Rows(r.ByChannel),
            ReturnsByReason = Rows(r.ReturnsByReason.Take(TableRows)),
            TopReturned = Rows(r.TopReturned.Take(TableRows)),
            RefundsByMethod = Rows(r.RefundsByMethod),
            Settlements = r.Settlements.Take(TableRows)
                .Select(s => new SettlementDoc(s.Date, s.Courier, Loc.EnumText(s.Method), s.Orders, s.Expected, s.Received)).ToList(),
            DrawerCloses = r.ShiftCloses.Take(TableRows)
                .Select(c => new DrawerCloseDoc(c.ClosedAt, c.Cashier, null, c.Expected, c.Counted, c.ExpectedLbp, c.CountedLbp)).ToList(),
        };
    }

    /// <summary>
    /// Each day's sales and profit, worked out as the Reports page does for a period (net sales minus refunds, minus the
    /// cost of what was sold, plus the cost of what came back to the shelf), so the days add up to the period's profit.
    /// A day with only returns shows too. Grouped in the database: a year is 365 small rows.
    /// </summary>
    public async Task<List<DayPoint>> DailyAsync(DateTime from, DateTime to, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var sales = await db.Sales.AsNoTracking()
            .Where(s => s.CreatedAt >= from && s.CreatedAt < to && s.Status == SaleStatus.Completed)
            .GroupBy(s => s.CreatedAt.Date)
            .Select(g => new { Date = g.Key, Count = g.Count(), Total = g.Sum(s => s.Total), Net = g.Sum(s => s.Total - s.TaxTotal) })
            .ToDictionaryAsync(x => x.Date, ct);
        var cost = await db.SaleLines.AsNoTracking()
            .Where(l => l.Sale!.CreatedAt >= from && l.Sale.CreatedAt < to && l.Sale.Status == SaleStatus.Completed)
            .GroupBy(l => l.Sale!.CreatedAt.Date)
            .Select(g => new { Date = g.Key, Cost = g.Sum(l => l.UnitCost * l.Quantity) })
            .ToDictionaryAsync(x => x.Date, x => x.Cost, ct);
        var refunds = await db.Returns.AsNoTracking()
            .Where(r => r.CreatedAt >= from && r.CreatedAt < to)
            .GroupBy(r => r.CreatedAt.Date)
            .Select(g => new { Date = g.Key, Net = g.Sum(r => r.TotalRefund - r.TaxRefund) })
            .ToDictionaryAsync(x => x.Date, x => x.Net, ct);
        var restocked = await db.ReturnLines.AsNoTracking()
            .Where(rl => rl.SaleReturn!.CreatedAt >= from && rl.SaleReturn.CreatedAt < to && rl.Restocked)
            .GroupBy(rl => rl.SaleReturn!.CreatedAt.Date)
            .Select(g => new { Date = g.Key, Cost = g.Sum(rl => rl.Quantity * (rl.SaleLine != null ? rl.SaleLine.UnitCost : 0m)) })
            .ToDictionaryAsync(x => x.Date, x => x.Cost, ct);

        return sales.Keys.Union(refunds.Keys).Order().Select(date =>
        {
            var sale = sales.GetValueOrDefault(date);
            var profit = (sale?.Net ?? 0) - refunds.GetValueOrDefault(date) - cost.GetValueOrDefault(date) + restocked.GetValueOrDefault(date);
            return new DayPoint(Day(date), sale?.Count ?? 0, sale?.Total ?? 0, profit);
        }).ToList();
    }

    private async Task<List<HourPoint>> HourlyAsync(DateTime from, DateTime to, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var hours = await db.Sales.AsNoTracking()
            .Where(s => s.CreatedAt >= from && s.CreatedAt < to && s.Status == SaleStatus.Completed)
            .GroupBy(s => s.CreatedAt.Hour)
            .Select(g => new { Hour = g.Key, Count = g.Count(), Total = g.Sum(s => s.Total) })
            .ToListAsync(ct);
        return hours.OrderBy(h => h.Hour).Select(h => new HourPoint(h.Hour, h.Count, h.Total)).ToList();
    }

    /// <summary>Sales by weekday (Monday first) and hour over the period.</summary>
    private async Task<List<HeatCell>> HeatmapAsync(DateTime from, DateTime to, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var cells = await db.Sales.AsNoTracking()
            .Where(s => s.CreatedAt >= from && s.CreatedAt < to && s.Status == SaleStatus.Completed)
            .GroupBy(s => new { s.CreatedAt.Date, s.CreatedAt.Hour })
            .Select(g => new { g.Key.Date, g.Key.Hour, Count = g.Count(), Total = g.Sum(s => s.Total) })
            .ToListAsync(ct);
        return cells
            .GroupBy(c => (Weekday: ((int)c.Date.DayOfWeek + 6) % 7, c.Hour))
            .Select(g => new HeatCell(g.Key.Weekday, g.Key.Hour, g.Sum(c => c.Count), g.Sum(c => c.Total)))
            .OrderBy(c => c.Weekday).ThenBy(c => c.Hour)
            .ToList();
    }

    /// <summary>Units and net amount per product over a period (returns taken off), for the given products only.</summary>
    private async Task<Dictionary<string, (int Qty, decimal Amount)>> ProductSalesAsync(DateTime from, DateTime to, List<string> names, CancellationToken ct)
    {
        if (names.Count == 0) return [];
        await using var db = await factory.CreateDbContextAsync(ct);
        var sold = await db.SaleLines.AsNoTracking()
            .Where(l => l.Sale!.CreatedAt >= from && l.Sale.CreatedAt < to && l.Sale.Status == SaleStatus.Completed && names.Contains(l.ProductName))
            .GroupBy(l => l.ProductName)
            .Select(g => new { Name = g.Key, Qty = g.Sum(l => l.Quantity), Amount = g.Sum(l => l.LineTotal - l.TaxAmount) })
            .ToListAsync(ct);
        var returned = await db.ReturnLines.AsNoTracking()
            .Where(rl => rl.SaleReturn!.CreatedAt >= from && rl.SaleReturn.CreatedAt < to && rl.SaleLine != null && names.Contains(rl.SaleLine.ProductName))
            .GroupBy(rl => rl.SaleLine!.ProductName)
            .Select(g => new { Name = g.Key, Qty = g.Sum(rl => rl.Quantity), Amount = g.Sum(rl => rl.RefundAmount - rl.TaxAmount) })
            .ToDictionaryAsync(x => x.Name, ct);
        return sold.ToDictionary(
            x => x.Name,
            x => (x.Qty - (returned.GetValueOrDefault(x.Name)?.Qty ?? 0), x.Amount - (returned.GetValueOrDefault(x.Name)?.Amount ?? 0)));
    }

    /// <summary>How many sales had a customer, how many customers, and how many of them had bought before the period.</summary>
    private async Task<CustomerCounts> CustomersAsync(DateTime from, DateTime to, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var inPeriod = db.Sales.AsNoTracking()
            .Where(s => s.CreatedAt >= from && s.CreatedAt < to && s.Status == SaleStatus.Completed && s.CustomerId != null);
        var sales = await inPeriod.CountAsync(ct);
        var customers = await inPeriod.Select(s => s.CustomerId).Distinct().CountAsync(ct);
        var returning = await inPeriod.Select(s => s.CustomerId).Distinct()
            .CountAsync(id => db.Sales.Any(e => e.CustomerId == id && e.Status == SaleStatus.Completed && e.CreatedAt < from), ct);
        return new CustomerCounts(sales, customers, returning);
    }

    public Task<ReportDoc> PeriodAsync(string period, DateTime today, CancellationToken ct = default)
    {
        var (from, to) = PeriodRange(period, today);
        return ReportAsync(from, to, ct);
    }

    public async Task<LiveDoc> LiveAsync(CancellationToken ct = default)
    {
        var drawers = new List<OpenDrawerDoc>();
        foreach (var shift in await shifts.GetOpenShiftsAsync(ct))
        {
            var summary = await shifts.GetSummaryAsync(shift.Id, ct);
            var cashier = shift.CurrentUser?.FullName ?? shift.User?.FullName ?? summary.Cashier;
            drawers.Add(new OpenDrawerDoc(shift.TillName, cashier, shift.OpenedAt, summary.SalesCount, summary.ExpectedCash, summary.ExpectedCashLbp)
            {
                TotalSales = summary.TotalSales,
            });
        }

        await using var db = await factory.CreateDbContextAsync(ct);
        var sales = await db.Sales.AsNoTracking()
            .OrderByDescending(s => s.CreatedAt).Take(RecentSales)
            .Select(s => new
            {
                s.Id, s.ReceiptNumber, s.CreatedAt, Cashier = s.User != null ? s.User.FullName : null, s.Total, s.Status, s.Channel,
                Items = s.Lines.Sum(l => (int?)l.Quantity) ?? 0,
            })
            .ToListAsync(ct);
        var ids = sales.Select(s => s.Id).ToList();
        var lines = (await db.SaleLines.AsNoTracking()
                .Where(l => ids.Contains(l.SaleId))
                .OrderBy(l => l.Id)
                .Select(l => new { l.SaleId, l.ProductName, l.VariantDescription, l.Quantity, l.LineTotal })
                .ToListAsync(ct))
            .ToLookup(l => l.SaleId, l => new SaleLineDoc(l.ProductName, l.VariantDescription, l.Quantity, l.LineTotal));

        var closes = await db.Shifts.AsNoTracking()
            .Where(s => s.Status == ShiftStatus.Closed && s.ClosedAt != null)
            .OrderByDescending(s => s.ClosedAt).Take(RecentCloses)
            .Select(s => new DrawerCloseDoc(
                s.ClosedAt!.Value, s.User != null ? s.User.FullName : "", s.TillName, s.ExpectedCash, s.CountedCash, s.ExpectedCashLbp, s.CountedCashLbp))
            .ToListAsync(ct);

        var active = db.ProductVariants.AsNoTracking().Where(v => v.IsActive && v.Product!.IsActive);
        var low = await active.CountAsync(v => v.StockQuantity <= v.ReorderLevel, ct);
        var outOfStock = await active.CountAsync(v => v.StockQuantity <= 0, ct);

        var today = DateTime.Today;
        return new LiveDoc(
            drawers,
            sales.Select(s => new RecentSaleDoc(
                s.ReceiptNumber, s.CreatedAt, s.Cashier, s.Total, s.Items, s.Status == SaleStatus.Voided ? "voided" : "completed",
                s.Channel == SalesChannel.InStore ? null : Loc.EnumText(s.Channel)) { Lines = lines[s.Id].ToList() }).ToList(),
            closes, low, outOfStock)
        {
            Trend14 = await DailyAsync(today.AddDays(-13), today.AddDays(1), ct),
        };
    }

    public async Task<StockDoc> StockAsync(DateTime today, CancellationToken ct = default)
    {
        var valuation = await reports.GetInventoryValuationAsync(ct);
        var alerts = await reports.GetStockAlertsAsync(today.Date.AddDays(-SlowSinceDays), today.Date.AddDays(1), ct);
        var skus = alerts.LowStock.Take(StockRows).Select(a => a.Sku).ToList();
        var since = today.Date.AddDays(-30);
        await using var db = await factory.CreateDbContextAsync(ct);
        var sold30 = await db.SaleLines.AsNoTracking()
            .Where(l => l.Sale!.Status == SaleStatus.Completed && l.Sale.CreatedAt >= since && skus.Contains(l.Sku))
            .GroupBy(l => l.Sku)
            .Select(g => new { Sku = g.Key, Qty = g.Sum(l => l.Quantity - l.ReturnedQuantity) })
            .ToDictionaryAsync(x => x.Sku, x => x.Qty, ct);
        return new StockDoc(
            valuation.Skus, valuation.Units, valuation.CostValue, valuation.RetailValue,
            alerts.LowStock.Count, valuation.OutOfStockCount, SlowSinceDays,
            Rows(valuation.ByCategoryCost.Take(TableRows)),
            alerts.LowStock.Take(StockRows).Select(a =>
            {
                var sold = sold30.GetValueOrDefault(a.Sku);
                return new LowStockDoc(a.Product, a.Variant, a.Sku, a.Stock, a.ReorderLevel)
                {
                    Sold30 = sold,
                    // At the last 30 days' pace; nothing when it hasn't sold (it isn't running out).
                    DaysLeft = a.Stock <= 0 ? 0 : sold > 0 ? (int)Math.Floor(a.Stock / (sold / 30m)) : null,
                };
            }).ToList(),
            alerts.SlowMovers.Take(StockRows).Select(a => new SlowMoverDoc(a.Product, a.Variant, a.Stock, a.CostValue, a.LastSold)).ToList());
    }

    /// <summary>Variants per part of the item list (about 100 bytes each, far below D1's 2 MB per row).</summary>
    public const int CatalogPartSize = 4000;

    /// <summary>Parts at most (40,000 variants), as many as the website reads in one request.</summary>
    public const int CatalogMaxParts = 10;

    /// <summary>
    /// Every active item with its stock, for the website's stock search: products A–Z, each with its sizes and colours.
    /// Two queries; the 30-day sales use the sales date index.
    /// </summary>
    public async Task<List<CatalogDoc>> CatalogAsync(DateTime today, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var variants = await db.ProductVariants.AsNoTracking()
            .Where(v => v.IsActive && v.Product!.IsActive)
            .Select(v => new
            {
                v.Id, v.ProductId, Product = v.Product!.Name, Category = v.Product.Category != null ? v.Product.Category.Name : null,
                v.Product.Brand, v.Product.StyleCode, ProductPrice = v.Product.Price,
                v.Size, v.Color, v.Sku, v.Barcode, v.StockQuantity, v.ReorderLevel, v.PriceOverride,
            })
            .ToListAsync(ct);
        var since = today.Date.AddDays(-30);
        var sold = await db.SaleLines.AsNoTracking()
            .Where(l => l.Sale!.Status == SaleStatus.Completed && l.Sale.CreatedAt >= since)
            .GroupBy(l => l.ProductVariantId)
            .Select(g => new { Id = g.Key, Qty = g.Sum(l => l.Quantity - l.ReturnedQuantity) })
            .ToDictionaryAsync(x => x.Id, x => x.Qty, ct);

        var products = variants
            .GroupBy(v => v.ProductId)
            .Select(g =>
            {
                var first = g.First();
                return new CatalogProduct(first.Product, first.Category, Blank(first.Brand), Blank(first.StyleCode), first.ProductPrice,
                    g.OrderBy(v => v.Color).ThenBy(v => v.Id).Select(v => new object?[]
                    {
                        v.Size, v.Color, v.Sku, Blank(v.Barcode), v.StockQuantity, v.ReorderLevel,
                        v.PriceOverride is { } own && own != first.ProductPrice ? own : null, sold.GetValueOrDefault(v.Id),
                    }).ToList());
            })
            .OrderBy(p => p.N, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        // Whole products per part, up to the part size.
        var parts = new List<List<CatalogProduct>> { new() };
        var count = 0;
        foreach (var product in products)
        {
            if (count > 0 && count + product.V.Count > CatalogPartSize)
            {
                if (parts.Count == CatalogMaxParts) break;
                parts.Add([]);
                count = 0;
            }
            parts[^1].Add(product);
            count += product.V.Count;
        }
        return parts.Select((list, i) => new CatalogDoc(i, parts.Count, list)).ToList();
    }

    private static string? Blank(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    /// <summary>
    /// The accounts allowed to sign in: active managers and admins whose password is their own (not one still to be
    /// changed at first sign-in).
    /// </summary>
    public async Task<List<OnlineUser>> UsersAsync(CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var users = await db.Users.AsNoTracking()
            .Where(u => u.IsActive && !u.MustChangePassword && (u.Role == UserRole.Manager || u.Role == UserRole.Admin))
            .OrderBy(u => u.Username)
            .Select(u => new { u.Username, u.FullName, u.Role, u.PasswordHash })
            .ToListAsync(ct);
        var result = new List<OnlineUser>();
        foreach (var u in users)
        {
            if (Verifier(u.PasswordHash) is not { } v) continue;
            result.Add(new OnlineUser(u.Username.Trim().ToLowerInvariant(), u.FullName, u.Role == UserRole.Admin ? "admin" : "manager", v.Salt, v.Iterations, v.Verifier));
        }
        return result;
    }

    /// <summary>
    /// From a stored password (<c>PBKDF2$iterations$salt$key</c>): the salt and rounds the browser needs, and a SHA-256 of
    /// the key. Someone who reads the website's database still has to guess the password through 100,000 PBKDF2 rounds.
    /// </summary>
    public static (string Salt, int Iterations, string Verifier)? Verifier(string storedHash)
    {
        var parts = storedHash.Split('$');
        if (parts.Length != 4 || parts[0] != "PBKDF2" || !int.TryParse(parts[1], out var iterations)) return null;
        try
        {
            var key = Convert.FromBase64String(parts[3]);
            if (key.Length != 32 || Convert.FromBase64String(parts[2]).Length is < 8 or > 64) return null;
            return (parts[2], iterations, Convert.ToBase64String(SHA256.HashData(key)));
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private static List<ReportRow> Rows(IEnumerable<NamedAmount> rows) =>
        rows.Select(r => new ReportRow(r.Name, r.Quantity, r.Amount, r.Count, r.Profit, r.SharePercent)).ToList();

    private static string Day(DateTime date) => date.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
}
