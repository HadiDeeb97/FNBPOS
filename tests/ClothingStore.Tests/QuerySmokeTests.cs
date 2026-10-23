using ClothingStore.Core;
using ClothingStore.Core.Entities;
using ClothingStore.Data.Seeding;
using ClothingStore.Data.Services;
using Microsoft.EntityFrameworkCore;

namespace ClothingStore.Tests;

/// <summary>
/// Runs every read query the UI uses, with filters, against the demo catalogue so that
/// any LINQ the SQL Server provider can't translate fails here rather than at the till.
/// </summary>
public class QuerySmokeTests
{
    [Fact]
    public async Task A_new_shop_starts_empty_with_only_the_admin_account()
    {
        await using var db = await TestDatabase.CreateAsync(); // created without demo data, as a release build does
        await using var ctx = await db.Factory.CreateDbContextAsync();
        Assert.False(await ctx.Products.AnyAsync());
        Assert.False(await ctx.Customers.AnyAsync());
        Assert.False(await ctx.Sales.AnyAsync());
        Assert.Equal("admin", (await ctx.Users.SingleAsync(u => u.Role == UserRole.Admin)).Username);
        Assert.True(await ctx.Regions.AnyAsync()); // Lebanon's governorates come ready
    }

    [Fact]
    public async Task All_screen_queries_translate_and_return_data()
    {
        await using var db = await TestDatabase.CreateAsync();
        await new DatabaseInitializer(db.Factory).InitializeAsync(seedDemoData: true);

        var supplier = (await db.Suppliers.GetAllAsync()).First();
        var categories = await db.Categories.GetAllAsync();
        var products = await db.Products.SearchAsync("jeans", categories.First(c => c.Name.Contains("Jeans")).Id);
        Assert.NotEmpty(products);
        Assert.NotEmpty(await db.Products.SearchAsync("indigo lab"));    // brand
        Assert.NotEmpty(await db.Products.SearchAsync(products[0].Variants[0].Sku));
        Assert.NotEmpty(await db.Products.SearchAsync(null, null, includeInactive: true));

        var variant = products[0].Variants.First(v => v.StockQuantity > 1);
        Assert.NotNull(await db.Products.GetAsync(products[0].Id));
        Assert.Single(await db.Products.GetVariantsAsync([variant.Id, variant.Id]));
        Assert.NotEmpty(await db.Inventory.GetStockAsync("jeans 32"));
        Assert.NotNull(await db.Inventory.GetStockAsync(null, categories[0].Id, lowStockOnly: true));
        Assert.NotEmpty(await db.Inventory.GetMovementsAsync(null, DateTime.Today, DateTime.Today.AddDays(1)));

        var customer = (await db.Customers.SearchAsync("sarah 0101")).Single();
        Assert.Empty(await db.Customers.SearchAsync("100%_no_match"));

        var shift = await db.OpenShiftAsync();
        var sale = await db.Sales.CompleteSaleAsync(new CheckoutRequest
        {
            UserId = db.Cashier.Id, ShiftId = shift.Id, CustomerId = customer.Id,
            Lines = [new CheckoutLine(variant.Id, 1)],
            Payments = [new PaymentInput(PaymentMethod.Cash, 500m)],
            ExchangeRate = 89_500m, // the cents of the change come back in pounds, at the rate shown
        });

        var today = DateTime.Today;
        var tomorrow = today.AddDays(1);
        Assert.Single(await db.Sales.SearchAsync(today, tomorrow));
        Assert.Single(await db.Sales.SearchAsync(today, tomorrow, "sarah"));
        Assert.Single(await db.Sales.SearchAsync(today, tomorrow, variant.Sku));
        Assert.Single(await db.Sales.SearchAsync(today, tomorrow, sale.ReceiptNumber, includeVoided: false));
        Assert.NotNull(await db.Sales.GetByReceiptAsync(sale.ReceiptNumber.ToLowerInvariant()));
        Assert.Single(await db.Customers.GetPurchaseHistoryAsync(customer.Id));

        await db.Returns.ProcessReturnAsync(new ReturnRequest
        {
            SaleId = sale.Id, UserId = db.Cashier.Id, ShiftId = shift.Id, RefundTo = RefundDestination.StoreCredit,
            Lines = [new ReturnLineRequest(sale.Lines[0].Id, 1)],
        });
        var returns = await db.Returns.SearchAsync(today, tomorrow);
        Assert.Single(returns);
        Assert.NotNull(await db.Returns.GetAsync(returns[0].Id));

        var po = await db.PurchaseOrders.SaveAsync(new PurchaseOrder
        {
            SupplierId = supplier.Id,
            Lines = [new PurchaseOrderLine { ProductVariantId = variant.Id, QuantityOrdered = 3, UnitCost = 10m }],
        }, db.Manager.Id);
        Assert.Single(await db.PurchaseOrders.GetAllAsync());
        Assert.Single(await db.PurchaseOrders.GetAllAsync(PurchaseOrderStatus.Draft));
        await db.PurchaseOrders.DeleteDraftAsync(po.Id);

        Assert.Single(await db.Shifts.GetShiftsAsync(today, tomorrow));
        Assert.Single(await db.Shifts.GetShiftsAsync(today, tomorrow, db.Cashier.Id));
        Assert.NotNull(await db.Shifts.GetOpenShiftAsync(db.Cashier.Id));

        var report = await db.Reports.GetSalesReportAsync(today, tomorrow);
        Assert.Equal(1, report.Transactions);
        Assert.NotEmpty(report.BySize);
        Assert.NotEmpty(report.ByDay);
        var valuation = await db.Reports.GetInventoryValuationAsync();
        Assert.True(valuation.CostValue > 0);

        Assert.NotEmpty(await db.Products.GenerateBarcodesAsync(2));
        Assert.True(await db.Users.IsFirstRunAsync()); // default admin still has the initial password
    }

    [Fact]
    public async Task Startup_turns_off_auto_close_which_sql_express_enables_by_default()
    {
        await using var db = await TestDatabase.CreateAsync();
        await using (var ctx = await db.Factory.CreateDbContextAsync())
            await ctx.Database.ExecuteSqlRawAsync("ALTER DATABASE CURRENT SET AUTO_CLOSE ON;");

        await new DatabaseInitializer(db.Factory).InitializeAsync(seedDemoData: false);

        await using var check = await db.Factory.CreateDbContextAsync();
        var autoClose = await check.Database
            .SqlQueryRaw<int>("SELECT CAST(DATABASEPROPERTYEX(DB_NAME(), 'IsAutoClose') AS int) AS [Value]")
            .ToListAsync();
        Assert.Equal(0, autoClose.Single());
    }

    [Fact]
    public async Task Query_warm_up_runs_every_screen_query_without_errors()
    {
        await using var db = await TestDatabase.CreateAsync();
        await new DatabaseInitializer(db.Factory).InitializeAsync(seedDemoData: true);
        var warmUp = new QueryWarmUp(db.Products, db.Inventory, db.Customers, db.Sales, db.Returns, db.Reports,
            db.Categories, db.Suppliers, db.Shifts, db.PurchaseOrders, new BackupService(db.Factory), db.Deliveries);

        await warmUp.RunAsync(); // swallows errors by design, so also run one query directly to prove the DB is usable
        Assert.NotEmpty(await db.Products.SearchAsync(null, null));
    }
}
