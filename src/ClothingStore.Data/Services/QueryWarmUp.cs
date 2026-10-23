namespace ClothingStore.Data.Services;

/// <summary>
/// The first time each query runs, EF Core compiles it and SQL Server builds a plan, which costs 50-300 ms per
/// query (measured). Running the queries the screens use once, in the background while the sign-in screen is
/// shown, makes every screen fast on first open instead of only on the second.
/// </summary>
public class QueryWarmUp(
    ProductService products, InventoryService inventory, CustomerService customers, SalesService sales,
    ReturnService returns, ReportService reports, CategoryService categories, SupplierService suppliers,
    ShiftService shifts, PurchaseOrderService purchaseOrders, BackupService backups, DeliveryService deliveries)
{
    public async Task RunAsync(CancellationToken ct = default)
    {
        var today = DateTime.Today;
        var tomorrow = today.AddDays(1);
        // Query shapes differ with and without a search text, so warm both.
        Func<Task>[] queries =
        [
            () => categories.GetAllAsync(ct: ct),
            () => products.SearchAsync(null, null, ct: ct),
            () => products.SearchAsync("~", null, ct: ct),
            () => products.SearchAsync(null, 0, ct: ct),
            () => products.SearchVariantsAsync("~", ct: ct),
            () => products.FindByCodeAsync("~", ct),
            () => customers.SearchAsync(null, ct: ct),
            () => customers.SearchAsync("~", ct: ct),
            () => customers.SearchAsync("70000000", ct: ct), // phone-number shape
            () => customers.GetRegionsAsync(ct),
            () => sales.GetHeldAsync(ct),
            () => sales.SearchAsync(today, tomorrow, ct: ct),
            () => sales.SearchAsync(today, tomorrow, "~", ct: ct),
            () => sales.SearchAsync(today, tomorrow, "70000000", ct: ct),
            () => sales.GetSummaryAsync(today, tomorrow, ct: ct),
            () => sales.GetSummaryAsync(today, tomorrow, "~", ct: ct),
            () => sales.GetLinesAsync(0, ct),
            () => sales.GetByReceiptAsync("~", ct),
            () => returns.SearchAsync(today, tomorrow, ct),
            () => shifts.GetOpenShiftAsync(0, ct),
            () => shifts.GetTillShiftAsync(0, "~", ct: ct),
            () => shifts.GetShiftsAsync(today, tomorrow, ct: ct),
            () => inventory.GetStockAsync(ct: ct),
            () => inventory.GetStockAsync("~", ct: ct),
            () => reports.GetInventoryValuationAsync(ct),
            () => reports.GetSalesReportAsync(today, tomorrow, ct),
            () => reports.GetStockAlertsAsync(today, tomorrow, ct),
            () => deliveries.GetAsync(false, ct: ct),
            () => deliveries.GetAsync(false, text: "~", ct: ct),
            () => deliveries.GetBalancesAsync(ct),
            () => deliveries.GetPartnersAsync(ct: ct),
            () => deliveries.GetCouriersAsync(ct),
            () => suppliers.GetAllAsync(ct: ct),
            () => purchaseOrders.GetAllAsync(ct: ct),
            () => backups.GetRecentAsync(ct: ct),
        ];
        foreach (var query in queries)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                await query();
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Warming up is best effort; the real screen will report any problem.
            }
        }
    }
}
