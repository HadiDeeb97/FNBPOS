using ClothingStore.Core;
using ClothingStore.Data.Services;
using Microsoft.EntityFrameworkCore;

namespace ClothingStore.Tests;

/// <summary>Cash in Lebanese pounds next to dollars. The tee sells for 22.00 (20 + 10% tax); the rate is 89,500.</summary>
public class LebanesePoundTests
{
    private const decimal Rate = 89_500m;

    [Fact]
    public async Task Sale_paid_in_dollars_and_pounds_records_both_and_the_drawer_counts_each_currency()
    {
        await using var db = await TestDatabase.CreateAsync();
        var tee = await db.CreateTeeAsync();
        var shift = await db.Shifts.OpenShiftAsync(db.Cashier.Id, 100m, 500_000m);

        var sale = await db.Sales.CompleteSaleAsync(new CheckoutRequest
        {
            UserId = db.Cashier.Id, ShiftId = shift.Id,
            Lines = [new CheckoutLine(tee.Variants[0].Id, 1)],
            Payments = [new PaymentInput(PaymentMethod.Cash, 10m), new PaymentInput(PaymentMethod.CashLbp, 1_100_000m)],
            ChangeIn = ChangeCurrency.Lbp, ExchangeRate = Rate,
        });

        Assert.Equal((10m, 0m, 1_100_000m, 26_000m), (sale.CashTendered, sale.ChangeGiven, sale.CashTenderedLbp, sale.ChangeGivenLbp));
        Assert.Equal(Rate, sale.ExchangeRate);
        Assert.Equal(10m, sale.Payments.Single(p => p.Method == PaymentMethod.Cash).Amount);
        Assert.Equal(12m, sale.Payments.Single(p => p.Method == PaymentMethod.CashLbp).Amount);

        // A dollar sale with change in pounds takes pounds out of the drawer.
        await db.Sales.CompleteSaleAsync(new CheckoutRequest
        {
            UserId = db.Cashier.Id, ShiftId = shift.Id,
            Lines = [new CheckoutLine(tee.Variants[1].Id, 1)],
            Payments = [new PaymentInput(PaymentMethod.Cash, 50m)],
            ChangeIn = ChangeCurrency.Mixed, ExchangeRate = Rate,
        }); // change 28 USD

        await db.Shifts.AddCashMovementAsync(shift.Id, CashMovementType.PayOut, 200_000m, "Delivery", db.Cashier.Id, CashCurrency.Lbp);

        var summary = await db.Shifts.GetSummaryAsync(shift.Id);
        Assert.Equal(100m + 10m + 22m, summary.ExpectedCash);
        Assert.Equal(500_000m + 1_074_000m - 200_000m, summary.ExpectedCashLbp);
        Assert.True(summary.ShowLbp);

        var closed = await db.Shifts.CloseShiftAsync(shift.Id, 132m, null, 1_370_000m);
        Assert.Equal(0m, closed.Variance);
        Assert.Equal(-4_000m, closed.VarianceLbp);
    }

    [Fact]
    public async Task A_sale_at_an_old_rate_is_refused_after_the_rate_changes()
    {
        await using var db = await TestDatabase.CreateAsync();
        var tee = await db.CreateTeeAsync();

        await Assert.ThrowsAsync<BusinessRuleException>(() => db.Settings.SetExchangeRateAsync(90_000m, db.Cashier.Id));
        await db.Settings.SetExchangeRateAsync(90_000m, db.Manager.Id);

        var request = new CheckoutRequest
        {
            UserId = db.Cashier.Id,
            Lines = [new CheckoutLine(tee.Variants[0].Id, 1)],
            Payments = [new PaymentInput(PaymentMethod.CashLbp, 1_980_000m)],
            ExchangeRate = Rate,
        };
        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() => db.Sales.CompleteSaleAsync(request));
        Assert.Contains("90,000", ex.Message);

        var sale = await db.Sales.CompleteSaleAsync(request with { ExchangeRate = 90_000m });
        Assert.Equal(22m, sale.Payments.Single().Amount);

        // Dollar-only sales don't care about the rate the till showed.
        await db.Sales.CompleteSaleAsync(request with { Payments = [new PaymentInput(PaymentMethod.Card, 22m)] });

        var history = await db.Settings.GetRateHistoryAsync();
        var change = Assert.Single(history);
        Assert.Equal((Rate, 90_000m, db.Manager.Id), (change.OldRate, change.NewRate, change.UserId));
    }

    [Fact]
    public async Task Saving_settings_never_puts_back_an_old_rate()
    {
        await using var db = await TestDatabase.CreateAsync();
        var screen = await db.Settings.GetAsync();
        await db.Settings.SetExchangeRateAsync(91_000m, db.Admin.Id);

        screen.LbpRate = Rate; // the settings screen was opened before the change
        screen.StoreName = "Renamed";
        await db.Settings.SaveAsync(screen);

        var saved = await db.Settings.GetAsync();
        Assert.Equal(("Renamed", 91_000m), (saved.StoreName, saved.LbpRate));
    }

    [Fact]
    public async Task Pounds_are_refunded_in_pounds_at_todays_rate_unless_the_cashier_picks_dollars()
    {
        await using var db = await TestDatabase.CreateAsync();
        var tee = await db.CreateTeeAsync();
        var shift = await db.OpenShiftAsync();
        var sale = await db.Sales.CompleteSaleAsync(new CheckoutRequest
        {
            UserId = db.Cashier.Id, ShiftId = shift.Id,
            Lines = [new CheckoutLine(tee.Variants[0].Id, 2)],
            Payments = [new PaymentInput(PaymentMethod.CashLbp, 3_938_000m)],
            ExchangeRate = Rate,
        });
        await db.Settings.SetExchangeRateAsync(90_000m, db.Manager.Id);

        var plan = await db.Returns.PlanAsync(sale.Id, [new ReturnLineRequest(sale.Lines[0].Id, 1)], RefundDestination.OriginalPayment);
        Assert.Equal(1_980_000m, plan.CashOutLbp);

        var inLbp = await db.Returns.ProcessReturnAsync(new ReturnRequest
        {
            SaleId = sale.Id, UserId = db.Cashier.Id, ShiftId = shift.Id,
            Lines = [new ReturnLineRequest(sale.Lines[0].Id, 1)], ExchangeRate = 90_000m,
        });
        var refund = Assert.Single(inLbp.Refunds);
        Assert.Equal((PaymentMethod.CashLbp, RefundMethod.CashLbp, 22m, 1_980_000m), (refund.Source, refund.Method, refund.Amount, refund.AmountLbp));

        var inUsd = await db.Returns.ProcessReturnAsync(new ReturnRequest
        {
            SaleId = sale.Id, UserId = db.Cashier.Id, ShiftId = shift.Id,
            Lines = [new ReturnLineRequest(sale.Lines[0].Id, 1)], CashCurrency = CashCurrency.Usd,
        });
        Assert.Equal(RefundMethod.Cash, Assert.Single(inUsd.Refunds).Method);

        var summary = await db.Shifts.GetSummaryAsync(shift.Id);
        Assert.Equal(100m - 22m, summary.ExpectedCash);
        Assert.Equal(3_938_000m - 1_980_000m, summary.ExpectedCashLbp);
    }

    [Fact]
    public async Task Stores_start_with_lbp_switched_on()
    {
        await using var db = await TestDatabase.CreateAsync();
        await using var ctx = await db.Factory.CreateDbContextAsync();
        var settings = await ctx.Settings.AsNoTracking().SingleAsync();
        Assert.Equal((true, Rate, 1_000), (settings.LbpEnabled, settings.LbpRate, settings.LbpRounding));
    }

    /// <summary>$22 paid with $50 and the cashier only has a $20 note: $20 and the other $8 in pounds go back.</summary>
    [Fact]
    public async Task Cashiers_own_change_split_is_recorded_and_counted_in_the_drawer()
    {
        await using var db = await TestDatabase.CreateAsync();
        var tee = await db.CreateTeeAsync();
        var shift = await db.Shifts.OpenShiftAsync(db.Cashier.Id, 100m, 1_000_000m);

        var sale = await db.Sales.CompleteSaleAsync(new CheckoutRequest
        {
            UserId = db.Cashier.Id, ShiftId = shift.Id,
            Lines = [new CheckoutLine(tee.Variants[0].Id, 1)],
            Payments = [new PaymentInput(PaymentMethod.Cash, 50m)],
            ChangeIn = ChangeCurrency.Mixed, GiveChangeUsd = 20m, ExchangeRate = Rate,
        });
        Assert.Equal((50m, 20m, 0m, 716_000m), (sale.CashTendered, sale.ChangeGiven, sale.CashTenderedLbp, sale.ChangeGivenLbp));

        var summary = await db.Shifts.GetSummaryAsync(shift.Id);
        Assert.Equal(100m + 50m - 20m, summary.ExpectedCash);
        Assert.Equal(1_000_000m - 716_000m, summary.ExpectedCashLbp);

        await Assert.ThrowsAsync<BusinessRuleException>(() => db.Sales.CompleteSaleAsync(new CheckoutRequest
        {
            UserId = db.Cashier.Id, ShiftId = shift.Id,
            Lines = [new CheckoutLine(tee.Variants[1].Id, 1)],
            Payments = [new PaymentInput(PaymentMethod.Cash, 50m)],
            GiveChangeUsd = 30m, ExchangeRate = Rate, // more than the $28 change
        }));
    }
}
