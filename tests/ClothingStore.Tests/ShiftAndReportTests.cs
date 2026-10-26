using ClothingStore.Core;
using ClothingStore.Data.Services;

namespace ClothingStore.Tests;

public class ShiftAndReportTests
{
    [Fact]
    public async Task Shift_close_computes_expected_cash_and_variance()
    {
        await using var db = await TestDatabase.CreateAsync();
        var tee = await db.CreateTeeAsync();
        var shift = await db.OpenShiftAsync(openingFloat: 150m);
        await Assert.ThrowsAsync<BusinessRuleException>(() => db.OpenShiftAsync());

        await db.Sales.CompleteSaleAsync(new CheckoutRequest
        {
            UserId = db.Cashier.Id, ShiftId = shift.Id,
            Lines = [new CheckoutLine(tee.Variants[0].Id, 1)], Payments = [new PaymentInput(PaymentMethod.Cash, 50m)],
        }); // 22 cash
        await db.Sales.CompleteSaleAsync(new CheckoutRequest
        {
            UserId = db.Cashier.Id, ShiftId = shift.Id,
            Lines = [new CheckoutLine(tee.Variants[1].Id, 2)], Payments = [new PaymentInput(PaymentMethod.Card, 44m)],
        });
        await db.Shifts.AddCashMovementAsync(shift.Id, CashMovementType.PayOut, 12.5m, "Window cleaner", db.Cashier.Id);
        await db.Shifts.AddCashMovementAsync(shift.Id, CashMovementType.PayIn, 20m, "Change from bank", db.Cashier.Id);

        var closed = await db.Shifts.CloseShiftAsync(shift.Id, 179m, "Short a coin");

        Assert.Equal(150m + 22m - 12.5m + 20m, closed.ExpectedCash);
        Assert.Equal(-0.5m, closed.Variance);
        Assert.Equal(2, closed.SalesCount);
        Assert.Equal(44m, closed.Payments[PaymentMethod.Card]);
        Assert.Null(await db.Shifts.GetOpenShiftAsync(db.Cashier.Id));
        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            db.Shifts.AddCashMovementAsync(shift.Id, CashMovementType.PayIn, 1m, "late", db.Cashier.Id));
    }

    [Fact]
    public async Task Sales_report_totals_profit_and_breakdowns()
    {
        await using var db = await TestDatabase.CreateAsync();
        var tee = await db.CreateTeeAsync(price: 20m, cost: 8m);
        var shift = await db.OpenShiftAsync();

        var sale = await db.Sales.CompleteSaleAsync(new CheckoutRequest
        {
            UserId = db.Cashier.Id, ShiftId = shift.Id,
            Lines = [new CheckoutLine(tee.Variants[0].Id, 2), new CheckoutLine(tee.Variants[1].Id, 1)],
            Payments = [new PaymentInput(PaymentMethod.Card, 66m)],
        }); // 60 net + 6 tax
        await db.Returns.ProcessReturnAsync(new ReturnRequest
        {
            SaleId = sale.Id, UserId = db.Cashier.Id, ShiftId = shift.Id,
            Lines = [new ReturnLineRequest(sale.Lines[1].Id, 1)],
        }); // refund 22 (20 net + 2 tax), restocked

        var report = await db.Reports.GetSalesReportAsync(DateTime.Today, DateTime.Today.AddDays(1));

        Assert.Equal(1, report.Transactions);
        Assert.Equal(3, report.ItemsSold);
        Assert.Equal(66m, report.TotalSales);
        Assert.Equal(60m, report.NetSales);
        Assert.Equal(22m, report.Refunds);
        Assert.Equal(40m, report.NetRevenue);
        Assert.Equal(16m, report.CostOfGoods);
        Assert.Equal(24m, report.GrossProfit);
        Assert.Equal("Card", Assert.Single(report.ByPaymentMethod).Name);
        // Product figures are net of returns, so they add up to the overall figures.
        var top = Assert.Single(report.TopProducts);
        Assert.Equal((2, 40m, 24m), (top.Quantity, top.Amount, top.Profit));

        var valuation = await db.Reports.GetInventoryValuationAsync();
        Assert.Equal(30 - 3 + 1, valuation.Units);
        Assert.Equal(28 * 8m, valuation.CostValue);
    }

    [Fact]
    public async Task Users_last_admin_is_protected_and_login_works()
    {
        await using var db = await TestDatabase.CreateAsync();

        Assert.NotNull(await db.Users.AuthenticateAsync("ADMIN", "admin123")); // usernames are case-insensitive
        Assert.Null(await db.Users.AuthenticateAsync("admin", "wrong"));

        db.Admin.Role = UserRole.Manager;
        await Assert.ThrowsAsync<BusinessRuleException>(() => db.Users.SaveAsync(db.Admin, null, db.Admin.Id));

        await db.Users.ChangePasswordAsync(db.Cashier.Id, "secret1", "newsecret");
        Assert.NotNull(await db.Users.AuthenticateAsync("cashier", "newsecret"));
        Assert.Null(await db.Users.AuthorizeAsync("cashier", "newsecret", Core.Security.Permission.VoidSales));
        Assert.NotNull(await db.Users.AuthorizeAsync("manager", "secret2", Core.Security.Permission.VoidSales));
    }

    [Fact]
    public async Task Demo_seed_produces_a_usable_catalogue()
    {
        await using var db = await TestDatabase.CreateAsync();
        await new Data.Seeding.DatabaseInitializer(db.Factory).InitializeAsync(seedDemoData: true);
        // Products already existed from nothing -> demo seeded now.
        var products = await db.Products.SearchAsync();
        Assert.True(products.Count >= 15);
        var hit = await db.Products.SearchVariantsAsync("oxford blue");
        Assert.NotEmpty(hit);
        Assert.All(hit, v => Assert.Equal("Classic Oxford Shirt", v.Product!.Name));
        var byCode = await db.Products.FindByCodeAsync(hit[0].Barcode!);
        Assert.Equal(hit[0].Id, byCode!.Id);
    }
}
