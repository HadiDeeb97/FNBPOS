using ClothingStore.Core;
using ClothingStore.Core.Localization;
using ClothingStore.Data.Services;

namespace ClothingStore.Tests;

/// <summary>The tee sells for 22.00 (20 + 10% tax), cost 8.</summary>
public class FullReportTests
{
    [Fact]
    public async Task Report_has_profit_customers_hours_returns_deliveries_cash_and_a_comparison()
    {
        await using var db = await TestDatabase.CreateAsync();
        var tee = await db.CreateTeeAsync(price: 20m, cost: 8m);
        var shift = await db.OpenShiftAsync();
        var customer = await db.Customers.SaveAsync(new Core.Entities.Customer { FirstName = "Rana", LastName = "Haddad", Phone = "03111222" });

        var sale = await db.Sales.CompleteSaleAsync(new CheckoutRequest
        {
            UserId = db.Cashier.Id, ShiftId = shift.Id, CustomerId = customer.Id,
            Lines = [new CheckoutLine(tee.Variants[0].Id, 2)], Payments = [new PaymentInput(PaymentMethod.Card, 44m)],
        });
        var online = await db.Sales.CompleteSaleAsync(new CheckoutRequest
        {
            UserId = db.Cashier.Id, ShiftId = shift.Id, Channel = SalesChannel.Instagram, Courier = "Toters", CustomerId = customer.Id,
            Lines = [new CheckoutLine(tee.Variants[1].Id, 1)], Payments = [new PaymentInput(PaymentMethod.Delivery, 22m)],
        });
        await db.Returns.ProcessReturnAsync(new ReturnRequest
        {
            SaleId = sale.Id, UserId = db.Cashier.Id, ShiftId = shift.Id, Reason = "Wrong size",
            Lines = [new ReturnLineRequest(sale.Lines[0].Id, 1)],
        });
        await db.Deliveries.SettleAsync(new SettleDeliveriesRequest
        {
            SaleIds = [online.Id], UserId = db.Cashier.Id, ShiftId = shift.Id, Method = SettlementMethod.Cash, Received = 20m,
        });
        await db.Shifts.AddCashMovementAsync(shift.Id, CashMovementType.PayOut, 5m, "Coffee", db.Cashier.Id);
        await db.Shifts.CloseShiftAsync(shift.Id, 115m, null, 0m);

        var report = await db.Reports.GetSalesReportAsync(DateTime.Today, DateTime.Today.AddDays(1));

        // Sizes and payments are filled, with each row's share of the total. Sizes are net of returns (2 S sold, 1 back).
        Assert.Equal([("S", 1), ("M", 1)], report.BySize.Select(x => (x.Name, x.Quantity)));
        Assert.Equal(100m, report.BySize.Sum(x => x.SharePercent));
        var payments = report.ByPaymentMethod.ToDictionary(x => x.Name);
        Assert.Equal((44m, 66.7m), (payments[Loc.EnumText(PaymentMethod.Card)].Amount, payments[Loc.EnumText(PaymentMethod.Card)].SharePercent));
        Assert.Equal(22m, payments[Loc.EnumText(PaymentMethod.Delivery)].Amount);

        var product = Assert.Single(report.TopProducts);
        // Net of the returned tee (20 net, its cost back as it was restocked), so it matches the overall profit.
        Assert.Equal((2, 40m, 16m, 24m), (product.Quantity, product.Amount, product.Cost, product.Profit));
        Assert.Equal("Rana Haddad", Assert.Single(report.TopCustomers).Name);
        Assert.Equal(DateTime.Now.Hour, Assert.Single(report.ByHour).Hour);
        Assert.Equal(("Wrong size", 1), (Assert.Single(report.ReturnsByReason).Name, report.ReturnsByReason[0].Count));
        Assert.Equal(1, Assert.Single(report.TopReturned).Quantity);
        var settlement = Assert.Single(report.Settlements);
        Assert.Equal(("Toters", 1, -2m), (settlement.Courier, settlement.Orders, settlement.Difference));
        Assert.Equal("Coffee", Assert.Single(report.CashMovements).Reason);
        var close = Assert.Single(report.ShiftCloses);
        Assert.Equal(100m + 20m - 5m, close.Expected); // float + delivery cash - pay-out (the refund went back to the card)
        Assert.Equal(0m, report.PreviousTotalSales);
        Assert.Null(report.SalesChangePercent);
    }

    [Fact]
    public async Task Stock_alerts_list_low_stock_and_items_that_did_not_sell()
    {
        await using var db = await TestDatabase.CreateAsync();
        var tee = await db.CreateTeeAsync(stockEach: 3, cost: 8m); // reorder level 2
        await db.Sales.CompleteSaleAsync(new CheckoutRequest
        {
            UserId = db.Cashier.Id, Lines = [new CheckoutLine(tee.Variants[0].Id, 2)], Payments = [new PaymentInput(PaymentMethod.Card, 44m)],
        });

        var alerts = await db.Reports.GetStockAlertsAsync(DateTime.Today, DateTime.Today.AddDays(1));
        var low = Assert.Single(alerts.LowStock);
        Assert.Equal((tee.Variants[0].Sku, 1), (low.Sku, low.Stock));
        Assert.NotNull(low.LastSold);
        Assert.Equal(2, alerts.SlowMovers.Count); // the two sizes that didn't sell
        Assert.All(alerts.SlowMovers, s => Assert.Equal(24m, s.CostValue));
    }
}
