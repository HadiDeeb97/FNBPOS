using ClothingStore.Core;
using ClothingStore.Data.Services;

namespace ClothingStore.Tests;

/// <summary>Online orders paid through a delivery company. The tee sells for 22.00 (20 + 10% tax).</summary>
public class DeliveryPaymentTests
{
    private static async Task<Core.Entities.Sale> SellAsync(TestDatabase db, int variantId, int qty, string courier, decimal fee = 3m, int? shiftId = null) =>
        await db.Sales.CompleteSaleAsync(new CheckoutRequest
        {
            UserId = db.Cashier.Id, ShiftId = shiftId, CustomerId = await db.ShopperIdAsync(),
            Lines = [new CheckoutLine(variantId, qty)],
            Channel = SalesChannel.Instagram, DeliveryFee = fee, Courier = courier,
            Payments = [new PaymentInput(PaymentMethod.Delivery, 22m * qty + fee)],
        });

    [Fact]
    public async Task Delivery_money_is_owed_not_cash_until_the_company_pays_it_into_the_drawer()
    {
        await using var db = await TestDatabase.CreateAsync();
        var tee = await db.CreateTeeAsync();
        var shift = await db.OpenShiftAsync();

        var a = await SellAsync(db, tee.Variants[0].Id, 1, "Toters", shiftId: shift.Id);   // owes 25
        var b = await SellAsync(db, tee.Variants[1].Id, 2, "Toters", shiftId: shift.Id);   // owes 47
        var c = await SellAsync(db, tee.Variants[2].Id, 1, "Abu Ali", shiftId: shift.Id);  // owes 25

        Assert.Equal(PaymentMethod.Delivery, a.Payments.Single().Method);
        Assert.Equal("Toters", a.Courier);
        Assert.Equal(100m, (await db.Shifts.GetSummaryAsync(shift.Id)).ExpectedCash); // nothing in the drawer yet

        var balances = await db.Deliveries.GetBalancesAsync();
        Assert.Equal(("Toters", 2, 72m), (balances[0].Courier, balances[0].Orders, balances[0].Owed));
        Assert.Equal(3, (await db.Deliveries.GetAsync(paid: false)).Count);

        // Toters pays for its two orders but keeps $2: the difference is recorded.
        var settlement = await db.Deliveries.SettleAsync(new SettleDeliveriesRequest
        {
            SaleIds = [a.Id, b.Id], UserId = db.Cashier.Id, ShiftId = shift.Id, Method = SettlementMethod.Cash, Received = 70m,
        });
        Assert.Equal((72m, 70m, -2m, "Toters"), (settlement.Expected, settlement.Received, settlement.Difference, settlement.Courier));
        Assert.Equal(100m + 70m, (await db.Shifts.GetSummaryAsync(shift.Id)).ExpectedCash);

        var owed = Assert.Single(await db.Deliveries.GetAsync(paid: false));
        Assert.Equal((c.Id, 25m), (owed.SaleId, owed.Owed));
        Assert.Equal(2, (await db.Deliveries.GetAsync(paid: true)).Count);
        await Assert.ThrowsAsync<BusinessRuleException>(() => db.Deliveries.SettleAsync(new SettleDeliveriesRequest
        {
            SaleIds = [a.Id], UserId = db.Cashier.Id, ShiftId = shift.Id, Method = SettlementMethod.Cash,
        }));

        // A transfer needs no drawer.
        await db.Deliveries.SettleAsync(new SettleDeliveriesRequest { SaleIds = [c.Id], UserId = db.Manager.Id, Method = SettlementMethod.Transfer });
        Assert.Empty(await db.Deliveries.GetBalancesAsync());
        Assert.Equal(170m, (await db.Shifts.GetSummaryAsync(shift.Id)).ExpectedCash);
    }

    [Fact]
    public async Task Delivery_payment_is_refused_in_store_and_cash_settlement_needs_an_open_shift()
    {
        await using var db = await TestDatabase.CreateAsync();
        var tee = await db.CreateTeeAsync();
        await Assert.ThrowsAsync<BusinessRuleException>(() => db.Sales.CompleteSaleAsync(new CheckoutRequest
        {
            UserId = db.Cashier.Id, Lines = [new CheckoutLine(tee.Variants[0].Id, 1)],
            Payments = [new PaymentInput(PaymentMethod.Delivery, 22m)],
        }));

        var sale = await SellAsync(db, tee.Variants[0].Id, 1, "Toters");
        await Assert.ThrowsAsync<BusinessRuleException>(() => db.Deliveries.SettleAsync(new SettleDeliveriesRequest
        {
            SaleIds = [sale.Id], UserId = db.Cashier.Id, Method = SettlementMethod.Cash,
        }));
    }

    [Fact]
    public async Task Pounds_from_the_company_go_into_the_lbp_drawer()
    {
        await using var db = await TestDatabase.CreateAsync();
        var tee = await db.CreateTeeAsync();
        var shift = await db.OpenShiftAsync();
        var sale = await SellAsync(db, tee.Variants[0].Id, 1, "Toters", fee: 0m); // owes 22 = 1,969,000 LBP

        var settlement = await db.Deliveries.SettleAsync(new SettleDeliveriesRequest
        {
            SaleIds = [sale.Id], UserId = db.Cashier.Id, ShiftId = shift.Id, Method = SettlementMethod.CashLbp, ExchangeRate = 89_500m,
        });
        Assert.Equal((1_969_000m, 22m), (settlement.ReceivedLbp, settlement.Received));
        Assert.Equal(1_969_000m, (await db.Shifts.GetSummaryAsync(shift.Id)).ExpectedCashLbp);
    }

    [Fact]
    public async Task Returning_an_unpaid_delivery_order_lowers_what_is_owed_and_after_payment_refunds_cash()
    {
        await using var db = await TestDatabase.CreateAsync();
        var tee = await db.CreateTeeAsync();
        var shift = await db.OpenShiftAsync();
        var sale = await SellAsync(db, tee.Variants[0].Id, 2, "Toters", fee: 0m); // owes 44

        var first = await db.Returns.ProcessReturnAsync(new ReturnRequest
        {
            SaleId = sale.Id, UserId = db.Cashier.Id, ShiftId = shift.Id, Lines = [new ReturnLineRequest(sale.Lines[0].Id, 1)],
        });
        Assert.Equal((RefundMethod.Delivery, 22m), (first.Refunds.Single().Method, first.Refunds.Single().Amount));
        Assert.Equal(22m, (await db.Deliveries.GetAsync(paid: false)).Single().Owed);
        Assert.Equal(100m, (await db.Shifts.GetSummaryAsync(shift.Id)).ExpectedCash); // no cash paid out

        await db.Deliveries.SettleAsync(new SettleDeliveriesRequest
        {
            SaleIds = [sale.Id], UserId = db.Cashier.Id, ShiftId = shift.Id, Method = SettlementMethod.Cash,
        });
        var second = await db.Returns.ProcessReturnAsync(new ReturnRequest
        {
            SaleId = sale.Id, UserId = db.Cashier.Id, ShiftId = shift.Id, Lines = [new ReturnLineRequest(sale.Lines[0].Id, 1)],
        });
        Assert.Equal(RefundMethod.Cash, second.Refunds.Single().Method);
        Assert.Equal(100m + 22m - 22m, (await db.Shifts.GetSummaryAsync(shift.Id)).ExpectedCash);
    }

    /// <summary>The customer paid the courier and brings the item back to the shop: refunded here, the courier still owes it all.</summary>
    [Fact]
    public async Task A_customer_who_already_paid_the_courier_is_refunded_at_the_counter()
    {
        await using var db = await TestDatabase.CreateAsync();
        var tee = await db.CreateTeeAsync();
        var shift = await db.OpenShiftAsync();
        var sale = await SellAsync(db, tee.Variants[0].Id, 2, "Toters", fee: 0m); // owes 44

        var plan = await db.Returns.PlanAsync(sale.Id, [new ReturnLineRequest(sale.Lines[0].Id, 1)], RefundDestination.OriginalPayment,
            customerPaidCourier: true);
        Assert.Equal((22m, 0m), (plan.CashOut, plan.OffDelivery));

        var ret = await db.Returns.ProcessReturnAsync(new ReturnRequest
        {
            SaleId = sale.Id, UserId = db.Cashier.Id, ShiftId = shift.Id, CustomerPaidCourier = true,
            Lines = [new ReturnLineRequest(sale.Lines[0].Id, 1)],
        });
        Assert.Equal((RefundMethod.Cash, 22m), (ret.Refunds.Single().Method, ret.Refunds.Single().Amount));
        Assert.Equal(44m, (await db.Deliveries.GetAsync(paid: false)).Single().Owed);         // courier still owes all of it
        Assert.Equal(100m - 22m, (await db.Shifts.GetSummaryAsync(shift.Id)).ExpectedCash); // paid out of the drawer
    }

    [Fact]
    public async Task Orders_are_found_by_the_delivery_invoice_number_for_scanning()
    {
        await using var db = await TestDatabase.CreateAsync();
        var tee = await db.CreateTeeAsync();
        var sale = await db.Sales.CompleteSaleAsync(new CheckoutRequest
        {
            UserId = db.Cashier.Id, Lines = [new CheckoutLine(tee.Variants[0].Id, 1)],
            Channel = SalesChannel.WhatsApp, Courier = "Toters", DeliveryReference = " tt-88231 ", CustomerId = await db.ShopperIdAsync(),
            Payments = [new PaymentInput(PaymentMethod.Delivery, 22m)],
        });
        Assert.Equal("TT-88231", sale.DeliveryReference);

        var found = await db.Deliveries.FindAsync("tt-88231");
        Assert.Equal((sale.Id, 22m, false), (found!.SaleId, found.Owed, found.IsPaid));
        Assert.Equal(sale.Id, (await db.Deliveries.FindAsync(sale.ReceiptNumber))!.SaleId);
        Assert.Null(await db.Deliveries.FindAsync("nope"));
        Assert.Single(await db.Deliveries.GetAsync(paid: false, text: "88231"));
        Assert.Single(await db.Sales.SearchAsync(DateTime.Today, DateTime.Today.AddDays(1), "TT-88231"));
    }
}

public class DeliveryCashMixupTests
{
    [Fact]
    public async Task Cash_left_in_the_box_when_the_delivery_company_pays_everything_is_refused()
    {
        await using var db = await TestDatabase.CreateAsync();
        var tee = await db.CreateTeeAsync();
        var shopper = await db.ShopperIdAsync();
        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() => db.Sales.CompleteSaleAsync(new CheckoutRequest
        {
            UserId = db.Cashier.Id, CustomerId = shopper, Lines = [new CheckoutLine(tee.Variants[0].Id, 1)],
            Channel = SalesChannel.WhatsApp, Courier = "test",
            Payments = [new PaymentInput(PaymentMethod.Delivery, 22m), new PaymentInput(PaymentMethod.Cash, 22m)],
        }));
        Assert.Contains("cash", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Upgrading_clears_the_phantom_cash_on_sales_already_recorded_that_way()
    {
        await using var db = await TestDatabase.CreateAsync();
        var tee = await db.CreateTeeAsync();
        var delivery = await db.Sales.CompleteSaleAsync(new CheckoutRequest
        {
            UserId = db.Cashier.Id, CustomerId = await db.ShopperIdAsync(), Lines = [new CheckoutLine(tee.Variants[0].Id, 1)],
            Channel = SalesChannel.WhatsApp, Courier = "test", Payments = [new PaymentInput(PaymentMethod.Delivery, 22m)],
        });
        var cash = await db.Sales.CompleteSaleAsync(new CheckoutRequest
        {
            UserId = db.Cashier.Id, Lines = [new CheckoutLine(tee.Variants[1].Id, 1)], Payments = [new PaymentInput(PaymentMethod.Cash, 30m)],
        });

        await using var ctx = await db.Factory.CreateDbContextAsync();
        var migrator = Microsoft.EntityFrameworkCore.Infrastructure.AccessorExtensions.GetService<Microsoft.EntityFrameworkCore.Migrations.IMigrator>(ctx);
        await migrator.MigrateAsync("SaleConcurrencyChecks");
        // How the earlier version saved it: the $22 left in the cash box all became change.
        await Microsoft.EntityFrameworkCore.RelationalDatabaseFacadeExtensions.ExecuteSqlAsync(ctx.Database,
            $"UPDATE Sales SET CashTendered = 22, ChangeGiven = 21, ChangeGivenLbp = 89000 WHERE Id = {delivery.Id}");
        await migrator.MigrateAsync();

        var fixedSale = await db.Sales.GetAsync(delivery.Id);
        Assert.Equal((0m, 0m, 0m, 0m), (fixedSale!.CashTendered, fixedSale.ChangeGiven, fixedSale.CashTenderedLbp, fixedSale.ChangeGivenLbp));
        var untouched = await db.Sales.GetAsync(cash.Id);
        Assert.Equal((30m, 8m), (untouched!.CashTendered, untouched.ChangeGiven));
    }
}
