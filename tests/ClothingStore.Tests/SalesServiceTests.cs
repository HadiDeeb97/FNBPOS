using ClothingStore.Core;
using ClothingStore.Core.Entities;
using ClothingStore.Data.Services;

namespace ClothingStore.Tests;

public class SalesServiceTests
{
    [Fact]
    public async Task Cash_sale_decrements_stock_gives_change_and_numbers_receipts()
    {
        await using var db = await TestDatabase.CreateAsync();
        var tee = await db.CreateTeeAsync(stockEach: 5);
        var shift = await db.OpenShiftAsync();
        var medium = tee.Variants.Single(v => v.Size == "M");

        var sale = await db.Sales.CompleteSaleAsync(new CheckoutRequest
        {
            UserId = db.Cashier.Id,
            ShiftId = shift.Id,
            Lines = [new CheckoutLine(medium.Id, 2)],
            Payments = [new PaymentInput(PaymentMethod.Cash, 50m)],
        });

        Assert.Equal(40m, sale.Subtotal);
        Assert.Equal(4m, sale.TaxTotal);
        Assert.Equal(44m, sale.Total);
        Assert.Equal(6m, sale.ChangeGiven);
        Assert.Equal(44m, sale.Payments.Single().Amount);
        Assert.Matches(@"^R\d{8}-0001$", sale.ReceiptNumber);
        Assert.Equal(3, (await db.GetVariantAsync(medium.Id)).StockQuantity);

        var second = await db.Sales.CompleteSaleAsync(new CheckoutRequest
        {
            UserId = db.Cashier.Id,
            ShiftId = shift.Id,
            Lines = [new CheckoutLine(medium.Id, 1)],
            Payments = [new PaymentInput(PaymentMethod.Card, 22m, "AUTH123")],
        });
        Assert.EndsWith("-0002", second.ReceiptNumber);
        Assert.Equal(0m, second.ChangeGiven);
    }

    [Fact]
    public async Task Cannot_oversell_unless_allowed()
    {
        await using var db = await TestDatabase.CreateAsync();
        var tee = await db.CreateTeeAsync(stockEach: 1);
        var v = tee.Variants[0];

        var request = new CheckoutRequest
        {
            UserId = db.Cashier.Id,
            Lines = [new CheckoutLine(v.Id, 2)],
            Payments = [new PaymentInput(PaymentMethod.Cash, 100m)],
        };
        await Assert.ThrowsAsync<BusinessRuleException>(() => db.Sales.CompleteSaleAsync(request));
        Assert.Equal(1, (await db.GetVariantAsync(v.Id)).StockQuantity);

        var settings = await db.Settings.GetAsync();
        settings.AllowNegativeStock = true;
        await db.Settings.SaveAsync(settings);

        await db.Sales.CompleteSaleAsync(request);
        Assert.Equal(-1, (await db.GetVariantAsync(v.Id)).StockQuantity);
    }

    [Fact]
    public async Task Short_payment_and_overpaid_card_are_rejected()
    {
        await using var db = await TestDatabase.CreateAsync();
        var tee = await db.CreateTeeAsync();
        var lines = new[] { new CheckoutLine(tee.Variants[0].Id, 1) }; // total 22.00

        await Assert.ThrowsAsync<BusinessRuleException>(() => db.Sales.CompleteSaleAsync(new CheckoutRequest
        {
            UserId = db.Cashier.Id, Lines = lines, Payments = [new PaymentInput(PaymentMethod.Cash, 20m)],
        }));
        await Assert.ThrowsAsync<BusinessRuleException>(() => db.Sales.CompleteSaleAsync(new CheckoutRequest
        {
            UserId = db.Cashier.Id, Lines = lines, Payments = [new PaymentInput(PaymentMethod.Card, 30m)],
        }));
    }

    [Fact]
    public async Task Split_tender_with_store_credit_and_loyalty_updates_customer()
    {
        await using var db = await TestDatabase.CreateAsync();
        var tee = await db.CreateTeeAsync();
        var customer = await db.Customers.SaveAsync(new Customer { FirstName = "Ana", Phone = "1" });
        await db.Customers.AdjustStoreCreditAsync(customer.Id, 10m, db.Manager.Id);
        await using (var ctx = await db.Factory.CreateDbContextAsync())
        {
            var c = await ctx.Customers.FindAsync(customer.Id);
            c!.LoyaltyPoints = 50; // worth 5.00 at 0.10/pt
            await ctx.SaveChangesAsync();
        }

        // 2 tees = 44.00 incl. 10% tax.
        var sale = await db.Sales.CompleteSaleAsync(new CheckoutRequest
        {
            UserId = db.Cashier.Id,
            CustomerId = customer.Id,
            Lines = [new CheckoutLine(tee.Variants[0].Id, 2)],
            Payments =
            [
                new PaymentInput(PaymentMethod.StoreCredit, 10m),
                new PaymentInput(PaymentMethod.LoyaltyPoints, 4m),
                new PaymentInput(PaymentMethod.Card, 30m),
            ],
        });

        Assert.Equal(44m, sale.Payments.Sum(p => p.Amount));
        Assert.Equal(40, sale.LoyaltyPointsRedeemed);
        Assert.Equal(30, sale.LoyaltyPointsEarned); // only card spend earns points

        var after = await db.GetCustomerAsync(customer.Id);
        Assert.Equal(0m, after.StoreCredit);
        Assert.Equal(50 - 40 + 30, after.LoyaltyPoints);
    }

    [Fact]
    public async Task Cashier_discount_limit_requires_manager_approval()
    {
        await using var db = await TestDatabase.CreateAsync();
        var tee = await db.CreateTeeAsync();
        CheckoutRequest Request(int? approver) => new()
        {
            UserId = db.Cashier.Id,
            Lines = [new CheckoutLine(tee.Variants[0].Id, 1, DiscountType.Percent, 25m)],
            Payments = [new PaymentInput(PaymentMethod.Cash, 100m)],
            ExchangeRate = 89_500m,
            ApprovedByUserId = approver,
        };

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() => db.Sales.CompleteSaleAsync(Request(null)));
        Assert.Contains("manager approval", ex.Message);
        await Assert.ThrowsAsync<BusinessRuleException>(() => db.Sales.CompleteSaleAsync(Request(db.Cashier.Id)));

        var sale = await db.Sales.CompleteSaleAsync(Request(db.Manager.Id));
        Assert.Equal(5m, sale.DiscountTotal);
    }

    [Fact]
    public async Task Sales_require_an_open_shift_when_one_is_given()
    {
        await using var db = await TestDatabase.CreateAsync();
        var tee = await db.CreateTeeAsync();
        var shift = await db.OpenShiftAsync();
        await db.Shifts.CloseShiftAsync(shift.Id, 100m, null);

        await Assert.ThrowsAsync<BusinessRuleException>(() => db.Sales.CompleteSaleAsync(new CheckoutRequest
        {
            UserId = db.Cashier.Id,
            ShiftId = shift.Id,
            Lines = [new CheckoutLine(tee.Variants[0].Id, 1)],
            Payments = [new PaymentInput(PaymentMethod.Cash, 100m)],
        }));
    }

    [Fact]
    public async Task Void_restocks_and_reverses_customer_balances()
    {
        await using var db = await TestDatabase.CreateAsync();
        var tee = await db.CreateTeeAsync(stockEach: 5);
        var customer = await db.Customers.SaveAsync(new Customer { FirstName = "Ben" });
        await db.Customers.AdjustStoreCreditAsync(customer.Id, 5m, db.Manager.Id);
        var shift = await db.OpenShiftAsync();

        var sale = await db.Sales.CompleteSaleAsync(new CheckoutRequest
        {
            UserId = db.Cashier.Id,
            ShiftId = shift.Id,
            CustomerId = customer.Id,
            Lines = [new CheckoutLine(tee.Variants[0].Id, 1)],
            Payments = [new PaymentInput(PaymentMethod.StoreCredit, 5m), new PaymentInput(PaymentMethod.Cash, 20m)],
        });

        await Assert.ThrowsAsync<BusinessRuleException>(() => db.Sales.VoidSaleAsync(sale.Id, db.Cashier.Id, "mistake"));
        await db.Sales.VoidSaleAsync(sale.Id, db.Manager.Id, "Rang up wrong item");

        Assert.Equal(5, (await db.GetVariantAsync(tee.Variants[0].Id)).StockQuantity);
        var c = await db.GetCustomerAsync(customer.Id);
        Assert.Equal(5m, c.StoreCredit);
        Assert.Equal(0, c.LoyaltyPoints);
        Assert.Equal(SaleStatus.Voided, (await db.Sales.GetAsync(sale.Id))!.Status);

        var summary = await db.Shifts.GetSummaryAsync(shift.Id);
        Assert.Equal(100m, summary.ExpectedCash); // voided cash excluded
        Assert.Equal(1, summary.VoidedCount);
    }

    [Fact]
    public async Task Held_cart_round_trip()
    {
        await using var db = await TestDatabase.CreateAsync();
        var cart = new HeldCart([new HeldCartLine(1, 2, DiscountType.Amount, 3m)], DiscountType.Percent, 5m, null);
        var held = await db.Sales.HoldAsync("Fitting room 2", db.Cashier.Id, cart);

        Assert.Single(await db.Sales.GetHeldAsync());
        var resumed = await db.Sales.ResumeAsync(held.Id);
        Assert.Equal(cart.Lines, resumed.Lines);
        Assert.Equal(5m, resumed.CartDiscountValue);
        Assert.Empty(await db.Sales.GetHeldAsync());
    }
}
