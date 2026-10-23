using ClothingStore.Core;
using ClothingStore.Core.Entities;
using ClothingStore.Core.Pricing;
using ClothingStore.Data.Services;
using Microsoft.EntityFrameworkCore;

namespace ClothingStore.Tests;

public class ReturnServiceTests
{
    private static async Task<(TestDatabase Db, Sale Sale, Shift Shift, Customer Customer)> SetupAsync()
    {
        var db = await TestDatabase.CreateAsync();
        var tee = await db.CreateTeeAsync(stockEach: 10, price: 10m);
        var customer = await db.Customers.SaveAsync(new Customer { FirstName = "Cleo" });
        var shift = await db.OpenShiftAsync();
        // 3 x 10.00 with 10% cart discount = 27.00 + 10% tax = 29.70
        var sale = await db.Sales.CompleteSaleAsync(new CheckoutRequest
        {
            UserId = db.Cashier.Id,
            ShiftId = shift.Id,
            CustomerId = customer.Id,
            Lines = [new CheckoutLine(tee.Variants[0].Id, 3)],
            CartDiscountType = DiscountType.Percent,
            CartDiscountValue = 10m,
            Payments = [new PaymentInput(PaymentMethod.Cash, 29.70m)],
        });
        return (db, sale, shift, customer);
    }

    [Fact]
    public async Task Partial_returns_reconcile_to_line_total()
    {
        var (db, sale, shift, _) = await SetupAsync();
        await using var _db = db;
        var line = sale.Lines.Single();
        Assert.Equal(29.70m, line.LineTotal);

        var first = await db.Returns.ProcessReturnAsync(new ReturnRequest
        {
            SaleId = sale.Id, UserId = db.Cashier.Id, ShiftId = shift.Id,
            Lines = [new ReturnLineRequest(line.Id, 1)],
        });
        var second = await db.Returns.ProcessReturnAsync(new ReturnRequest
        {
            SaleId = sale.Id, UserId = db.Cashier.Id, ShiftId = shift.Id,
            Lines = [new ReturnLineRequest(line.Id, 2)],
        });

        Assert.Equal(9.90m, first.TotalRefund);
        Assert.Equal(29.70m, first.TotalRefund + second.TotalRefund);
        Assert.Equal(10, (await db.GetVariantAsync(line.ProductVariantId)).StockQuantity);
        Assert.Matches(@"^RT\d{8}-0002$", second.ReturnNumber);

        await Assert.ThrowsAsync<BusinessRuleException>(() => db.Returns.ProcessReturnAsync(new ReturnRequest
        {
            SaleId = sale.Id, UserId = db.Cashier.Id, ShiftId = shift.Id,
            Lines = [new ReturnLineRequest(line.Id, 1)],
        }));

        var summary = await db.Shifts.GetSummaryAsync(shift.Id);
        Assert.Equal(100m, summary.ExpectedCash); // 100 float + 29.70 sale - 29.70 refunds
    }

    [Fact]
    public async Task Store_credit_refund_without_restock()
    {
        var (db, sale, _, customer) = await SetupAsync();
        await using var _db = db;
        var line = sale.Lines.Single();

        var ret = await db.Returns.ProcessReturnAsync(new ReturnRequest
        {
            SaleId = sale.Id, UserId = db.Cashier.Id, RefundTo = RefundDestination.StoreCredit,
            Lines = [new ReturnLineRequest(line.Id, 1, Restock: false)], Reason = "Damaged seam",
        });

        var c = await db.GetCustomerAsync(customer.Id);
        Assert.Equal(ret.TotalRefund, c.StoreCredit);
        Assert.Equal(29 - 9, c.LoyaltyPoints);
        Assert.Equal(7, (await db.GetVariantAsync(line.ProductVariantId)).StockQuantity); // not restocked
    }

    [Fact]
    public async Task Return_window_enforced_for_cashiers()
    {
        var (db, sale, shift, _) = await SetupAsync();
        await using var _db = db;
        await using (var ctx = await db.Factory.CreateDbContextAsync())
        {
            await ctx.Sales.Where(s => s.Id == sale.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.CreatedAt, DateTime.Now.AddDays(-60)));
        }

        ReturnRequest Request(int user, int? approver = null) => new()
        {
            SaleId = sale.Id, UserId = user, ShiftId = shift.Id,
            Lines = [new ReturnLineRequest(sale.Lines[0].Id, 1)], ApprovedByUserId = approver,
        };

        await Assert.ThrowsAsync<BusinessRuleException>(() => db.Returns.ProcessReturnAsync(Request(db.Cashier.Id)));
        var ret = await db.Returns.ProcessReturnAsync(Request(db.Cashier.Id, db.Manager.Id));
        Assert.Equal(9.90m, ret.TotalRefund);
    }

    [Fact]
    public async Task Voided_sales_cannot_be_returned_and_returned_sales_cannot_be_voided()
    {
        var (db, sale, shift, _) = await SetupAsync();
        await using var _db = db;

        await db.Returns.ProcessReturnAsync(new ReturnRequest
        {
            SaleId = sale.Id, UserId = db.Cashier.Id, ShiftId = shift.Id,
            Lines = [new ReturnLineRequest(sale.Lines[0].Id, 1)],
        });
        await Assert.ThrowsAsync<BusinessRuleException>(() => db.Sales.VoidSaleAsync(sale.Id, db.Manager.Id, "test"));
    }

    // ---- Refunds follow the original payment -------------------------------------------------

    /// <summary>Tee at 10.00 + 10% tax = 11.00 each, with a customer who has store credit and points.</summary>
    private static async Task<(TestDatabase Db, ProductVariant Tee, Shift Shift, Customer Customer)> SetupTenderAsync(
        decimal storeCredit = 50m, int points = 100)
    {
        var db = await TestDatabase.CreateAsync();
        var tee = (await db.CreateTeeAsync(stockEach: 10, price: 10m)).Variants[0];
        var customer = await db.Customers.SaveAsync(new Customer { FirstName = "Tess" });
        if (storeCredit > 0) await db.Customers.AdjustStoreCreditAsync(customer.Id, storeCredit, db.Manager.Id);
        await using (var ctx = await db.Factory.CreateDbContextAsync())
        {
            await ctx.Customers.Where(c => c.Id == customer.Id).ExecuteUpdateAsync(c => c.SetProperty(x => x.LoyaltyPoints, points));
        }
        return (db, tee, await db.OpenShiftAsync(), customer);
    }

    private static Task<Sale> SellAsync(TestDatabase db, ProductVariant tee, Shift shift, Customer customer, int quantity,
        params PaymentInput[] payments) =>
        db.Sales.CompleteSaleAsync(new CheckoutRequest
        {
            UserId = db.Cashier.Id, ShiftId = shift.Id, CustomerId = customer.Id,
            Lines = [new CheckoutLine(tee.Id, quantity)], Payments = payments,
        });

    private static ReturnRequest Return(TestDatabase db, Sale sale, Shift shift, int quantity,
        RefundDestination to = RefundDestination.OriginalPayment, int? approver = null) => new()
    {
        SaleId = sale.Id, UserId = db.Cashier.Id, ShiftId = shift.Id, RefundTo = to, ApprovedByUserId = approver,
        Lines = [new ReturnLineRequest(sale.Lines.Single().Id, quantity)],
    };

    [Theory]
    [InlineData(RefundDestination.OriginalPayment)]
    [InlineData(RefundDestination.Cash)]
    public async Task Store_credit_payment_is_never_refunded_as_cash(RefundDestination to)
    {
        var (db, tee, shift, customer) = await SetupTenderAsync(storeCredit: 50m);
        await using var _db = db;
        var sale = await SellAsync(db, tee, shift, customer, 1, new PaymentInput(PaymentMethod.StoreCredit, 11m));

        var ret = await db.Returns.ProcessReturnAsync(Return(db, sale, shift, 1, to));

        var refund = Assert.Single(ret.Refunds);
        Assert.Equal((PaymentMethod.StoreCredit, RefundMethod.StoreCredit, 11m), (refund.Source, refund.Method, refund.Amount));
        Assert.Equal(50m, (await db.GetCustomerAsync(customer.Id)).StoreCredit);
        Assert.Equal(100m, (await db.Shifts.GetSummaryAsync(shift.Id)).ExpectedCash); // drawer untouched
    }

    [Fact]
    public async Task Loyalty_points_payment_comes_back_as_points_and_earned_points_are_taken_back()
    {
        var (db, tee, shift, customer) = await SetupTenderAsync(storeCredit: 0, points: 100);
        await using var _db = db;
        // 11.00 = 5.00 in points (50 pts at 0.10) + 6.00 cash, which earns 6 points.
        var sale = await SellAsync(db, tee, shift, customer, 1,
            new PaymentInput(PaymentMethod.LoyaltyPoints, 5m), new PaymentInput(PaymentMethod.Cash, 6m));
        Assert.Equal(100 - 50 + 6, (await db.GetCustomerAsync(customer.Id)).LoyaltyPoints);

        var ret = await db.Returns.ProcessReturnAsync(Return(db, sale, shift, 1));

        Assert.Equal(6m, ret.Refunds.Single(r => r.Method == RefundMethod.Cash).Amount);
        Assert.Equal(5m, ret.Refunds.Single(r => r.Method == RefundMethod.LoyaltyPoints).Amount);
        Assert.Equal(50, ret.LoyaltyPointsRestored);
        Assert.Equal(6, ret.LoyaltyPointsRemoved);
        Assert.Equal(100, (await db.GetCustomerAsync(customer.Id)).LoyaltyPoints);
        Assert.Equal(100m, (await db.Shifts.GetSummaryAsync(shift.Id)).ExpectedCash);
    }

    [Fact]
    public async Task Split_payment_refunds_each_tender_in_proportion_and_reconciles_over_partial_returns()
    {
        var (db, tee, shift, customer) = await SetupTenderAsync(storeCredit: 13m, points: 0);
        await using var _db = db;
        // 3 x 11.00 = 33.00 paid 20.00 cash + 13.00 store credit; only the cash earns points (20).
        var sale = await SellAsync(db, tee, shift, customer, 3,
            new PaymentInput(PaymentMethod.Cash, 20m), new PaymentInput(PaymentMethod.StoreCredit, 13m));
        Assert.Equal(20, sale.LoyaltyPointsEarned);

        var returns = new List<SaleReturn>();
        for (var i = 0; i < 3; i++) returns.Add(await db.Returns.ProcessReturnAsync(Return(db, sale, shift, 1)));

        Assert.All(returns, r => Assert.Equal(r.TotalRefund, r.Refunds.Sum(x => x.Amount)));
        Assert.Equal(6.67m, returns[0].Refunds.Single(r => r.Method == RefundMethod.Cash).Amount);
        Assert.Equal(4.33m, returns[0].Refunds.Single(r => r.Method == RefundMethod.StoreCredit).Amount);
        var all = returns.SelectMany(r => r.Refunds).ToList();
        Assert.Equal(20m, all.Where(r => r.Method == RefundMethod.Cash).Sum(r => r.Amount));
        Assert.Equal(13m, all.Where(r => r.Method == RefundMethod.StoreCredit).Sum(r => r.Amount));

        // Points earned on the cash are taken back exactly once in total, never more.
        Assert.Equal(20, returns.Sum(r => r.LoyaltyPointsRemoved));
        var after = await db.GetCustomerAsync(customer.Id);
        Assert.Equal(13m, after.StoreCredit);
        Assert.Equal(0, after.LoyaltyPoints);
        Assert.Equal(100m, (await db.Shifts.GetSummaryAsync(shift.Id)).ExpectedCash);
    }

    [Fact]
    public async Task Cash_refund_of_a_card_payment_needs_manager_approval()
    {
        var (db, tee, shift, customer) = await SetupTenderAsync();
        await using var _db = db;
        var sale = await SellAsync(db, tee, shift, customer, 2, new PaymentInput(PaymentMethod.Card, 22m));

        var original = await db.Returns.ProcessReturnAsync(Return(db, sale, shift, 1));
        Assert.Equal(RefundMethod.Card, Assert.Single(original.Refunds).Method);

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            db.Returns.ProcessReturnAsync(Return(db, sale, shift, 1, RefundDestination.Cash)));
        Assert.Contains("manager approval", ex.Message);
        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            db.Returns.ProcessReturnAsync(Return(db, sale, shift, 1, RefundDestination.Cash, approver: db.Cashier.Id)));

        var approved = await db.Returns.ProcessReturnAsync(Return(db, sale, shift, 1, RefundDestination.Cash, approver: db.Manager.Id));
        var refund = Assert.Single(approved.Refunds);
        Assert.Equal((PaymentMethod.Card, RefundMethod.Cash, 11m), (refund.Source, refund.Method, refund.Amount));
        Assert.Equal(100m - 11m, (await db.Shifts.GetSummaryAsync(shift.Id)).ExpectedCash);
    }

    [Fact]
    public async Task Card_payment_can_go_to_store_credit_without_approval()
    {
        var (db, tee, shift, customer) = await SetupTenderAsync(storeCredit: 0);
        await using var _db = db;
        var sale = await SellAsync(db, tee, shift, customer, 1, new PaymentInput(PaymentMethod.Card, 11m));

        var ret = await db.Returns.ProcessReturnAsync(Return(db, sale, shift, 1, RefundDestination.StoreCredit));

        var refund = Assert.Single(ret.Refunds);
        Assert.Equal((PaymentMethod.Card, RefundMethod.StoreCredit), (refund.Source, refund.Method));
        Assert.Equal(11m, (await db.GetCustomerAsync(customer.Id)).StoreCredit);
    }

    [Fact]
    public async Task Plan_matches_the_processed_return_and_changes_nothing()
    {
        var (db, tee, shift, customer) = await SetupTenderAsync(storeCredit: 13m);
        await using var _db = db;
        var sale = await SellAsync(db, tee, shift, customer, 3,
            new PaymentInput(PaymentMethod.Card, 20m), new PaymentInput(PaymentMethod.StoreCredit, 13m));
        var lines = new[] { new ReturnLineRequest(sale.Lines.Single().Id, 1) };

        var plan = await db.Returns.PlanAsync(sale.Id, lines, RefundDestination.Cash);

        Assert.Equal(11m, plan.Total);
        Assert.True(plan.NeedsCashOverride);
        Assert.Equal(6.67m, plan.CashOut);
        Assert.Equal(7, (await db.GetVariantAsync(tee.Id)).StockQuantity);
        Assert.Equal(0m, (await db.GetCustomerAsync(customer.Id)).StoreCredit);

        var ret = await db.Returns.ProcessReturnAsync(Return(db, sale, shift, 1, RefundDestination.Cash, approver: db.Manager.Id));
        Assert.Equal(plan.Shares, ret.Refunds.Select(r => new RefundShare(r.Source, r.Method, r.Amount)).ToList());
    }
}
