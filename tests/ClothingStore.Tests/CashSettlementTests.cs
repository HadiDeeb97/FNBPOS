using ClothingStore.Core;
using ClothingStore.Core.Pricing;

namespace ClothingStore.Tests;

public class CashSettlementTests
{
    private const decimal Rate = 89_500m;

    private static CashSettlement Settle(decimal due, decimal usd, decimal lbp, ChangeCurrency changeIn = ChangeCurrency.Usd, int rounding = 1_000) =>
        CashSettlement.Calculate(due, new CashTender(usd, lbp, changeIn), Rate, rounding);

    [Fact]
    public void Lbp_amounts_round_up_when_collecting_and_down_when_paying_out()
    {
        Assert.Equal(1_570_000m, Lbp.ToPay(17.50m, Rate, 5_000)); // 1,566,250
        Assert.Equal(1_565_000m, Lbp.ToGive(17.50m, Rate, 5_000));
        Assert.Equal(1_969_000m, Lbp.ToPay(22m, Rate, 1_000));    // exact stays exact
        Assert.Equal(0m, Lbp.ToPay(22m, 0m, 1_000));
    }

    [Fact]
    public void Dollars_only_gives_dollar_change()
    {
        var s = Settle(22m, 50m, 0m);
        Assert.True(s.IsCovered);
        Assert.Equal((28m, 0m), (s.ChangeUsd, s.ChangeLbp));
        Assert.Equal((22m, 0m), (s.AppliedUsd, s.AppliedFromLbp));
    }

    [Fact]
    public void Paying_the_lbp_amount_shown_covers_the_sale_exactly()
    {
        var s = Settle(22m, 0m, Lbp.ToPay(22m, Rate, 1_000));
        Assert.True(s.IsCovered);
        Assert.Equal((0m, 0m), (s.ChangeUsd, s.ChangeLbp));
        Assert.Equal((0m, 22m), (s.AppliedUsd, s.AppliedFromLbp));
    }

    [Fact]
    public void One_thousand_pounds_short_is_reported_in_both_currencies()
    {
        var s = Settle(22m, 0m, 1_968_000m);
        Assert.False(s.IsCovered);
        Assert.Equal(1_000m, s.ShortLbp);
        Assert.Equal(0.01m, s.ShortUsd);
    }

    [Fact]
    public void Split_tender_uses_dollars_first_and_gives_change_in_pounds()
    {
        // 10 * 89,500 + 1,100,000 = 1,995,000 against 1,969,000 due.
        var s = Settle(22m, 10m, 1_100_000m, ChangeCurrency.Lbp);
        Assert.Equal((0m, 26_000m), (s.ChangeUsd, s.ChangeLbp));
        Assert.Equal((10m, 12m), (s.AppliedUsd, s.AppliedFromLbp));
    }

    [Theory]
    [InlineData(ChangeCurrency.Usd, 28, 45_000)]      // no dollar coins: the 0.50 is given in pounds
    [InlineData(ChangeCurrency.Mixed, 28, 45_000)]    // 0.50 = 44,750 to the nearest 1,000
    [InlineData(ChangeCurrency.Lbp, 0, 2_551_000)]    // 28.50 = 2,550,750 to the nearest 1,000
    public void Change_can_be_given_in_dollars_pounds_or_both(ChangeCurrency changeIn, double usd, int lbp)
    {
        var s = Settle(21.50m, 50m, 0m, changeIn);
        Assert.Equal(((decimal)usd, (decimal)lbp), (s.ChangeUsd, s.ChangeLbp));
        Assert.Equal(21.50m, s.AppliedUsd);
    }

    /// <summary>59.99 paid with $60: the cent is 895 LBP, which is given as a 1,000 note instead of disappearing.</summary>
    [Theory]
    [InlineData(ChangeCurrency.Mixed, 0, 1_000)]
    [InlineData(ChangeCurrency.Lbp, 0, 1_000)]
    [InlineData(ChangeCurrency.Usd, 0, 1_000)]
    public void A_one_cent_change_is_given_as_the_nearest_pound_note(ChangeCurrency changeIn, double usd, int lbp)
    {
        var s = Settle(59.99m, 60m, 0m, changeIn);
        Assert.True(s.IsCovered);
        Assert.Equal(((decimal)usd, (decimal)lbp), (s.ChangeUsd, s.ChangeLbp));
    }

    [Fact]
    public void Less_than_half_a_note_of_change_stays_in_the_drawer()
    {
        // 60.00 - 59.995: 447.5 LBP is under half of 1,000.
        var s = Settle(59.995m, 60m, 0m, ChangeCurrency.Mixed);
        Assert.Equal((0m, 0m), (s.ChangeUsd, s.ChangeLbp));
    }

    /// <summary>$50 due, $100 paid, the cashier only has a $20 note: the other $30 goes back in pounds.</summary>
    [Fact]
    public void Cashier_gives_the_dollars_they_have_and_the_rest_in_pounds()
    {
        var s = CashSettlement.Calculate(50m, new CashTender(100m, 0m, ChangeCurrency.Mixed, GiveUsd: 20m), Rate, 1_000);
        Assert.Equal((20m, 2_685_000m), (s.ChangeUsd, s.ChangeLbp));
        Assert.Equal((50m, 0m), (s.AppliedUsd, s.AppliedFromLbp));
    }

    /// <summary>Same sale, the cashier has 1,000,000 LBP: the rest in whole dollars, the leftover cents as a pound note.</summary>
    [Fact]
    public void Cashier_gives_the_pounds_they_have_and_the_rest_in_dollars()
    {
        // 50 x 89,500 = 4,475,000 change; minus 1,000,000 = 3,475,000 = $38 + 74,000.
        var s = CashSettlement.Calculate(50m, new CashTender(100m, 0m, ChangeCurrency.Mixed, GiveLbp: 1_000_000m), Rate, 1_000);
        Assert.Equal((38m, 1_074_000m), (s.ChangeUsd, s.ChangeLbp));
    }

    [Fact]
    public void A_split_can_give_everything_in_one_currency()
    {
        Assert.Equal((50m, 0m), Change(CashSettlement.Calculate(50m, new CashTender(100m, 0m, GiveUsd: 50m), Rate, 1_000)));
        Assert.Equal((0m, 4_475_000m), Change(CashSettlement.Calculate(50m, new CashTender(100m, 0m, GiveUsd: 0m), Rate, 1_000)));
        Assert.Equal((50m, 0m), Change(CashSettlement.Calculate(50m, new CashTender(100m, 0m, GiveLbp: 0m), Rate, 1_000)));

        static (decimal, decimal) Change(CashSettlement s) => (s.ChangeUsd, s.ChangeLbp);
    }

    [Fact]
    public void A_split_larger_than_the_change_or_with_cents_is_refused()
    {
        Assert.Throws<BusinessRuleException>(() => CashSettlement.Calculate(50m, new CashTender(100m, 0m, GiveUsd: 60m), Rate, 1_000));
        Assert.Throws<BusinessRuleException>(() => CashSettlement.Calculate(50m, new CashTender(100m, 0m, GiveUsd: 20.5m), Rate, 1_000));
        Assert.Throws<BusinessRuleException>(() => CashSettlement.Calculate(50m, new CashTender(100m, 0m, GiveLbp: 5_000_000m), Rate, 1_000));
        Assert.Throws<BusinessRuleException>(() => CashSettlement.Calculate(50m, new CashTender(100m, 0m, GiveUsd: 20m), 0m, 1_000)); // LBP off
    }

    [Fact]
    public void Pounds_are_refused_when_lbp_is_off()
    {
        Assert.Throws<BusinessRuleException>(() => CashSettlement.Calculate(10m, new CashTender(0m, 900_000m), 0m, 1_000));
        Assert.Throws<BusinessRuleException>(() => CashSettlement.Calculate(10m, new CashTender(20m, 0m, ChangeCurrency.Lbp), 0m, 1_000));
        Assert.Equal(10m, CashSettlement.Calculate(10m, new CashTender(20m, 0m), 0m, 1_000).ChangeUsd);
        Assert.Equal(0.01m, CashSettlement.Calculate(59.99m, new CashTender(60m, 0m), 0m, 1_000).ChangeUsd); // cents without LBP
    }
}
