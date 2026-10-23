using ClothingStore.Core;
using ClothingStore.Core.Pricing;

namespace ClothingStore.Data.Services;

public sealed record CheckoutLine(int VariantId, int Quantity, DiscountType DiscountType = DiscountType.None, decimal DiscountValue = 0m);

public sealed record PaymentInput(PaymentMethod Method, decimal Amount, string? Reference = null);

public sealed record CheckoutRequest
{
    public required int UserId { get; init; }
    public int? ShiftId { get; init; }
    public int? CustomerId { get; init; }
    public required IReadOnlyList<CheckoutLine> Lines { get; init; }
    public DiscountType CartDiscountType { get; init; }
    public decimal CartDiscountValue { get; init; }

    /// <summary>
    /// Tenders. For cash, Amount is what the customer handed over (dollars for <see cref="PaymentMethod.Cash"/>,
    /// pounds for <see cref="PaymentMethod.CashLbp"/>); change is worked out here.
    /// For every other method Amount is the dollar amount charged.
    /// </summary>
    public required IReadOnlyList<PaymentInput> Payments { get; init; }

    /// <summary>How change is handed back.</summary>
    public ChangeCurrency ChangeIn { get; init; } = ChangeCurrency.Usd;

    /// <summary>
    /// The cashier's own split of the change, overriding <see cref="ChangeIn"/>: dollars to give back (whole dollars,
    /// the rest in pounds) or pounds to give back (the rest in dollars). Set at most one; null when not used.
    /// </summary>
    public decimal? GiveChangeUsd { get; init; }
    public decimal? GiveChangeLbp { get; init; }

    /// <summary>
    /// LBP rate the till showed the customer. When pounds are involved and the rate has changed since,
    /// the sale is refused so nobody pays at an old rate.
    /// </summary>
    public decimal ExchangeRate { get; init; }

    /// <summary>Manager who approved a discount above the cashier limit (if any).</summary>
    public int? ApprovedByUserId { get; init; }

    public string? Notes { get; init; }

    /// <summary>In store, or the channel of an online order (WhatsApp, Instagram...).</summary>
    public SalesChannel Channel { get; init; } = SalesChannel.InStore;

    /// <summary>Delivery charge for an online order, added to the total (not taxed). Must be 0 in store.</summary>
    public decimal DeliveryFee { get; init; }

    /// <summary>Delivery company or driver of an online order.</summary>
    public string? Courier { get; init; }

    /// <summary>The delivery company's invoice / tracking number.</summary>
    public string? DeliveryReference { get; init; }
}

public sealed record HeldCartLine(int VariantId, int Quantity, DiscountType DiscountType, decimal DiscountValue);

public sealed record HeldCart(
    IReadOnlyList<HeldCartLine> Lines,
    DiscountType CartDiscountType,
    decimal CartDiscountValue,
    int? CustomerId,
    SalesChannel Channel = SalesChannel.InStore,
    decimal DeliveryFee = 0,
    string? Notes = null,
    string? Courier = null,
    string? DeliveryReference = null);

public sealed record ReturnLineRequest(int SaleLineId, int Quantity, bool Restock = true);

public sealed record ReturnRequest
{
    public required int SaleId { get; init; }
    public required int UserId { get; init; }
    public int? ShiftId { get; init; }
    public RefundDestination RefundTo { get; init; } = RefundDestination.OriginalPayment;
    public required IReadOnlyList<ReturnLineRequest> Lines { get; init; }
    public string? Reason { get; init; }

    /// <summary>Manager who approved a return outside the return window or a cash refund of a card payment (if any).</summary>
    public int? ApprovedByUserId { get; init; }

    /// <summary>Pay every cash part in this currency; null = the currency each part was paid in.</summary>
    public CashCurrency? CashCurrency { get; init; }

    /// <summary>LBP rate the till showed; checked like <see cref="CheckoutRequest.ExchangeRate"/>.</summary>
    public decimal ExchangeRate { get; init; }

    /// <summary>
    /// For an order paid through a delivery company that hasn't paid the store yet: the customer already paid the
    /// courier, so the customer is refunded at the counter and the courier still owes the full amount. False: the
    /// money never left the courier, so it is just owed less.
    /// </summary>
    public bool CustomerPaidCourier { get; init; }
}

/// <summary>What a return would refund and how, before it is processed.</summary>
public sealed record RefundPlan(decimal Total, IReadOnlyList<RefundShare> Shares, decimal Rate = 0, int Rounding = 1)
{
    /// <summary>Dollars paid out of the drawer.</summary>
    public decimal CashOut => Shares.Where(s => s.Method == RefundMethod.Cash).Sum(s => s.Amount);

    /// <summary>Taken off what the delivery company owes (its money never reached the store).</summary>
    public decimal OffDelivery => Shares.Where(s => s.Method == RefundMethod.Delivery).Sum(s => s.Amount);

    /// <summary>Pounds paid out of the drawer.</summary>
    public decimal CashOutLbp => Shares.Where(s => s.Method == RefundMethod.CashLbp).Sum(s => Lbp.ToGive(s.Amount, Rate, Rounding));
    public bool NeedsCashOverride => Shares.Any(s => s.IsCashOverride);
    public bool AddsStoreCredit => Shares.Any(s => s.Method == RefundMethod.StoreCredit);
}

/// <summary>Which sales the history lists.</summary>
public enum SaleListFilter
{
    All,
    Completed,
    WithReturns,
    Voided,
}

/// <summary>
/// The history screen's summary: every sale in the dates (voided ones included), the voided ones, the sales items came
/// back from and what was refunded, and what is left after taking voided sales and refunds off.
/// </summary>
public sealed record SalesSummary(int Count, decimal Total, int VoidedCount, decimal VoidedTotal, int ReturnedCount, decimal Refunded)
{
    public decimal Net => Total - VoidedTotal - Refunded;
}
