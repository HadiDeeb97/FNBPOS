using ClothingStore.Core;
using ClothingStore.Core.Pricing;

namespace ClothingStore.Tests;

public class RefundAllocatorTests
{
    [Theory]
    [InlineData(PaymentMethod.Cash, RefundDestination.OriginalPayment, RefundMethod.Cash)]
    [InlineData(PaymentMethod.Card, RefundDestination.OriginalPayment, RefundMethod.Card)]
    [InlineData(PaymentMethod.MobileWallet, RefundDestination.OriginalPayment, RefundMethod.MobileWallet)]
    [InlineData(PaymentMethod.Card, RefundDestination.StoreCredit, RefundMethod.StoreCredit)]
    [InlineData(PaymentMethod.Card, RefundDestination.Cash, RefundMethod.Cash)]
    [InlineData(PaymentMethod.StoreCredit, RefundDestination.Cash, RefundMethod.StoreCredit)]
    [InlineData(PaymentMethod.StoreCredit, RefundDestination.OriginalPayment, RefundMethod.StoreCredit)]
    [InlineData(PaymentMethod.LoyaltyPoints, RefundDestination.Cash, RefundMethod.LoyaltyPoints)]
    [InlineData(PaymentMethod.LoyaltyPoints, RefundDestination.StoreCredit, RefundMethod.LoyaltyPoints)]
    public void Store_funds_always_go_back_in_kind(PaymentMethod source, RefundDestination to, RefundMethod expected) =>
        Assert.Equal(expected, RefundAllocator.MethodFor(source, to));

    [Fact]
    public void Splits_in_proportion_to_what_is_left_on_each_tender()
    {
        var shares = RefundAllocator.Allocate(10m, new Dictionary<PaymentMethod, decimal>
        {
            [PaymentMethod.Card] = 30m,
            [PaymentMethod.StoreCredit] = 10m,
            [PaymentMethod.Cash] = 0m,
        }, RefundDestination.OriginalPayment);

        Assert.Equal(
            [new RefundShare(PaymentMethod.Card, RefundMethod.Card, 7.50m), new RefundShare(PaymentMethod.StoreCredit, RefundMethod.StoreCredit, 2.50m)],
            shares.OrderBy(s => s.Source).ToList());
    }

    [Fact]
    public void Cannot_refund_more_than_is_left() =>
        Assert.Throws<BusinessRuleException>(() => RefundAllocator.Allocate(
            10.01m, new Dictionary<PaymentMethod, decimal> { [PaymentMethod.Cash] = 10m }, RefundDestination.OriginalPayment));

    [Fact]
    public void Only_card_or_wallet_money_paid_out_as_cash_is_an_override()
    {
        Assert.True(new RefundShare(PaymentMethod.Card, RefundMethod.Cash, 1m).IsCashOverride);
        Assert.True(new RefundShare(PaymentMethod.MobileWallet, RefundMethod.Cash, 1m).IsCashOverride);
        Assert.False(new RefundShare(PaymentMethod.Cash, RefundMethod.Cash, 1m).IsCashOverride);
        Assert.False(new RefundShare(PaymentMethod.Card, RefundMethod.StoreCredit, 1m).IsCashOverride);
    }

    [Theory]
    [InlineData(20, 6.67, 20, 6)]
    [InlineData(20, 20, 20, 20)]
    [InlineData(20, 25, 20, 20)]
    [InlineData(20, 5, 0, 0)]
    [InlineData(0, 5, 10, 0)]
    public void Proportional_points_round_down_until_everything_is_refunded(int total, decimal part, decimal whole, int expected) =>
        Assert.Equal(expected, RefundAllocator.ProportionalPoints(total, part, whole));
}
