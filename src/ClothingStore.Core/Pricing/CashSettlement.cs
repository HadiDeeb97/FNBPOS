using ClothingStore.Core.Localization;

namespace ClothingStore.Core.Pricing;

/// <summary>Lebanese pound amounts. Prices stay in dollars; LBP is a second cash currency at the store's rate.</summary>
public static class Lbp
{
    /// <summary>LBP the customer must hand over for a dollar amount: rounded up so the drawer is never short.</summary>
    public static decimal ToPay(decimal usd, decimal rate, int rounding) =>
        rate <= 0 || usd <= 0 ? 0 : RoundUp(usd * rate, rounding);

    /// <summary>
    /// LBP handed out for a dollar amount (refunds): to the nearest note, like change, so a cent or two isn't lost to
    /// either side. The drawer records exactly what was handed out.
    /// </summary>
    public static decimal ToGive(decimal usd, decimal rate, int rounding) =>
        rate <= 0 || usd <= 0 ? 0 : RoundNearest(usd * rate, rounding);

    public static decimal RoundUp(decimal lbp, int rounding)
    {
        var step = Math.Max(1, rounding);
        return Math.Ceiling(lbp / step) * step;
    }

    public static decimal RoundDown(decimal lbp, int rounding)
    {
        var step = Math.Max(1, rounding);
        return Math.Floor(lbp / step) * step;
    }

    /// <summary>To the nearest step; exactly half way goes up.</summary>
    public static decimal RoundNearest(decimal lbp, int rounding)
    {
        var step = Math.Max(1, rounding);
        return Math.Round(lbp / step, MidpointRounding.AwayFromZero) * step;
    }

    /// <summary>"1,567,000 LBP" with the currency name in the current (or given) language.</summary>
    public static string Format(decimal lbp, string? language = null) =>
        $"{lbp:N0} {(language is null ? Loc.T("Currency.Lbp") : Loc.Get(language, "Currency.Lbp"))}";
}

/// <summary>
/// What the cashier collected in cash, in both currencies. <paramref name="GiveUsd"/> / <paramref name="GiveLbp"/> are
/// the cashier's own split of the change ("I only have $20"): one is fixed and the rest is worked out in the other
/// currency, overriding <paramref name="ChangeIn"/>. Null means no split.
/// </summary>
public sealed record CashTender(decimal Usd, decimal Lbp, ChangeCurrency ChangeIn = ChangeCurrency.Usd, decimal? GiveUsd = null, decimal? GiveLbp = null)
{
    public bool HasSplit => GiveUsd is not null || GiveLbp is not null;
}

/// <summary>
/// Works out whether cash in dollars and pounds covers what is due, and the change in each currency.
/// Comparisons are done in LBP (dollars x rate), which is exact, so a customer paying the LBP amount on the
/// screen is never a fraction short.
/// </summary>
public sealed record CashSettlement
{
    /// <summary>Cash due in dollars (sale total minus card, wallet, credit and points).</summary>
    public decimal DueUsd { get; init; }
    public decimal TenderedUsd { get; init; }
    public decimal TenderedLbp { get; init; }
    public decimal Rate { get; init; }
    public int Rounding { get; init; }
    public ChangeCurrency ChangeIn { get; init; }

    /// <summary>Still to pay, in dollars (0 when covered).</summary>
    public decimal ShortUsd { get; init; }

    /// <summary>Still to pay in LBP, rounded up (0 when covered or when LBP is off).</summary>
    public decimal ShortLbp { get; init; }

    public decimal ChangeUsd { get; init; }
    public decimal ChangeLbp { get; init; }

    /// <summary>Part of the cash due covered by the dollars kept in the drawer.</summary>
    public decimal AppliedUsd { get; init; }

    /// <summary>Part of the cash due (in dollars) covered by pounds.</summary>
    public decimal AppliedFromLbp { get; init; }

    public bool IsCovered => ShortUsd == 0;

    /// <summary>Total change, worth in dollars.</summary>
    public decimal ChangeValueUsd => Rate > 0 ? Money.Round(ChangeUsd + ChangeLbp / Rate) : ChangeUsd;

    public static CashSettlement Calculate(decimal dueUsd, CashTender tender, decimal rate, int rounding)
    {
        if (tender.Usd < 0 || tender.Lbp < 0) throw new BusinessRuleException(Loc.T("Err.PaymentNegative"));

        var lbpOn = rate > 0;
        if (!lbpOn && (tender.Lbp > 0 || tender.ChangeIn != ChangeCurrency.Usd || tender.HasSplit))
            throw new BusinessRuleException(Loc.T("Err.LbpDisabled"));

        dueUsd = Math.Max(0, dueUsd);
        var r = lbpOn ? rate : 1m;
        var step = Math.Max(1, rounding);

        // Everything in "LBP units" (dollars x rate) so the comparison has no rounding error.
        var due = dueUsd * r;
        var tendered = tender.Usd * r + tender.Lbp;

        if (tendered < due)
        {
            var shortUsd = Money.Round((due - tendered) / r);
            if (shortUsd == 0) shortUsd = 0.01m;
            return new CashSettlement
            {
                DueUsd = dueUsd, TenderedUsd = tender.Usd, TenderedLbp = tender.Lbp, Rate = rate, Rounding = step,
                ChangeIn = tender.ChangeIn,
                ShortUsd = shortUsd,
                ShortLbp = lbpOn ? Lbp.RoundUp(due - tendered, step) : 0,
            };
        }

        // Pound change goes to the nearest note: owing 895 LBP (one cent at 89,500) the customer gets 1,000 rather
        // than nothing, and the drawer records the 1,000 that actually left it.
        var extra = tendered - due;
        var (changeUsd, changeLbp) = tender.HasSplit
            ? Split(extra, r, step, tender.GiveUsd, tender.GiveLbp)
            : ByChoice(extra, r, step, lbpOn ? tender.ChangeIn : ChangeCurrency.Usd, lbpOn);

        return Settled(dueUsd, tender, rate, step, changeUsd, changeLbp);
    }

    /// <summary>Change (<paramref name="extra"/> in LBP units) in the currency the cashier picked.</summary>
    private static (decimal Usd, decimal Lbp) ByChoice(decimal extra, decimal r, int step, ChangeCurrency changeIn, bool lbpOn)
    {
        switch (changeIn)
        {
            case ChangeCurrency.Lbp:
                return (0, Lbp.RoundNearest(extra, step));
            case ChangeCurrency.Mixed:
            case ChangeCurrency.Usd when lbpOn:
            {
                // Whole dollars, the rest as the nearest pound note. Also for "dollars" when LBP is on: there are no
                // dollar coins in circulation, so cents can't be handed out.
                var dollars = Math.Floor(extra / r);
                return (dollars, Lbp.RoundNearest(extra - dollars * r, step));
            }
            default:
                // No LBP: cents are kept in the drawer rather than paid out as a fraction.
                return (Math.Floor(extra / r * 100m) / 100m, 0);
        }
    }

    /// <summary>
    /// The cashier's split of the change (<paramref name="extra"/> in LBP units). Dollars fixed: the rest in pounds.
    /// Pounds fixed: the rest in whole dollars, any cents left over added to the pounds as the nearest note.
    /// </summary>
    private static (decimal Usd, decimal Lbp) Split(decimal extra, decimal rate, int step, decimal? giveUsd, decimal? giveLbp)
    {
        if (giveUsd is { } usd)
        {
            if (usd < 0) throw new BusinessRuleException(Loc.T("Err.PaymentNegative"));
            if (usd != Math.Floor(usd)) throw new BusinessRuleException(Loc.T("Err.ChangeWholeDollars"));
            if (usd * rate > extra) throw new BusinessRuleException(Loc.T("Err.ChangeSplitTooMuch"));
            return (usd, Lbp.RoundNearest(extra - usd * rate, step));
        }

        var lbp = Math.Round(giveLbp!.Value, 0, MidpointRounding.AwayFromZero);
        if (lbp < 0) throw new BusinessRuleException(Loc.T("Err.PaymentNegative"));
        if (lbp > Lbp.RoundNearest(extra, step)) throw new BusinessRuleException(Loc.T("Err.ChangeSplitTooMuch"));
        var rest = Math.Max(0, extra - lbp);
        var dollars = Math.Floor(rest / rate);
        return (dollars, lbp + Lbp.RoundNearest(rest - dollars * rate, step));
    }

    private static CashSettlement Settled(decimal dueUsd, CashTender tender, decimal rate, int step, decimal changeUsd, decimal changeLbp)
    {
        // Dollars stay in the drawer first; the rest of the cash due came from pounds.
        var appliedUsd = Math.Clamp(tender.Usd - changeUsd, 0, dueUsd);
        return new CashSettlement
        {
            DueUsd = dueUsd, TenderedUsd = tender.Usd, TenderedLbp = tender.Lbp, Rate = rate, Rounding = step,
            ChangeIn = tender.ChangeIn,
            ChangeUsd = changeUsd,
            ChangeLbp = changeLbp,
            AppliedUsd = appliedUsd,
            AppliedFromLbp = dueUsd - appliedUsd,
        };
    }
}
