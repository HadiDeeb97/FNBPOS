using ClothingStore.Core.Localization;

namespace ClothingStore.Core.Pricing;

/// <summary>Part of a refund: money from one tender of the original sale and how it goes back.</summary>
public sealed record RefundShare(PaymentMethod Source, RefundMethod Method, decimal Amount)
{
    /// <summary>Card or wallet money paid out as cash, which needs manager approval.</summary>
    public bool IsCashOverride => Method is RefundMethod.Cash or RefundMethod.CashLbp && Source is PaymentMethod.Card or PaymentMethod.MobileWallet;

    public bool IsCash => Method is RefundMethod.Cash or RefundMethod.CashLbp;
}

/// <summary>
/// Splits a refund across the tenders of the original sale, so money goes back the way it came in.
/// Store credit and loyalty points can never come back as cash or card.
/// </summary>
public static class RefundAllocator
{
    /// <param name="refund">Amount being refunded now.</param>
    /// <param name="remaining">For each tender of the sale, what was paid minus what earlier returns already refunded from it.</param>
    /// <param name="destination">What the cashier chose for the cash, card and wallet parts.</param>
    public static IReadOnlyList<RefundShare> Allocate(
        decimal refund, IReadOnlyDictionary<PaymentMethod, decimal> remaining, RefundDestination destination)
    {
        if (refund <= 0) return [];

        // Proportional to what is left on each tender, so a partial return of a split payment
        // refunds each tender in the same ratio and the last return refunds exactly what remains.
        var tenders = remaining.Where(t => t.Value > 0).OrderBy(t => t.Key).ToList();
        if (refund > tenders.Sum(t => t.Value))
            throw new BusinessRuleException(Loc.T("Err.RefundTooMuch"));

        var amounts = CartCalculator.Allocate(refund, tenders.Select(t => t.Value).ToList());
        return tenders
            .Select((t, i) => new RefundShare(t.Key, MethodFor(t.Key, destination), amounts[i]))
            .Where(s => s.Amount > 0)
            .ToList();
    }

    public static RefundMethod MethodFor(PaymentMethod source, RefundDestination destination) => source switch
    {
        PaymentMethod.StoreCredit => RefundMethod.StoreCredit,
        PaymentMethod.LoyaltyPoints => RefundMethod.LoyaltyPoints,
        _ when destination == RefundDestination.StoreCredit => RefundMethod.StoreCredit,
        PaymentMethod.CashLbp => RefundMethod.CashLbp,
        // Delivery money not received yet is simply owed less; the return service pays cash if it was received.
        PaymentMethod.Delivery => RefundMethod.Delivery,
        _ when destination == RefundDestination.Cash => RefundMethod.Cash,
        PaymentMethod.Card => RefundMethod.Card,
        PaymentMethod.MobileWallet => RefundMethod.MobileWallet,
        _ => RefundMethod.Cash,
    };

    /// <summary>
    /// Pays every cash part in one currency when the cashier asks for it (e.g. a sale paid in pounds refunded in
    /// dollars); null keeps each part in the currency it was paid in.
    /// </summary>
    public static IReadOnlyList<RefundShare> InCurrency(IReadOnlyList<RefundShare> shares, CashCurrency? currency) =>
        currency is null
            ? shares
            : shares.Select(s => s.IsCash ? s with { Method = currency == CashCurrency.Lbp ? RefundMethod.CashLbp : RefundMethod.Cash } : s).ToList();

    /// <summary>
    /// Share of a whole number of points that corresponds to <paramref name="part"/> of <paramref name="whole"/>,
    /// rounded down until the whole amount has been reached, so repeated partial returns add up exactly.
    /// </summary>
    public static int ProportionalPoints(int totalPoints, decimal part, decimal whole) =>
        whole <= 0 || totalPoints <= 0 ? 0
        : part >= whole ? totalPoints
        : (int)Math.Floor(totalPoints * part / whole);
}
