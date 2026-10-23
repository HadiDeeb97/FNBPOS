namespace ClothingStore.Core;

public enum UserRole
{
    Cashier = 0,
    Manager = 1,
    Admin = 2,
}

public enum Gender
{
    Unisex = 0,
    Men = 1,
    Women = 2,
    Kids = 3,
}

public enum SaleStatus
{
    Completed = 0,
    Voided = 1,
}

public enum PaymentMethod
{
    Cash = 0,
    Card = 1,
    MobileWallet = 2,
    StoreCredit = 3,
    LoyaltyPoints = 4,

    /// <summary>Cash in Lebanese pounds. The payment amount is the dollar value applied to the sale.</summary>
    CashLbp = 5,

    /// <summary>
    /// Collected by the delivery company, which pays the store later (online orders only). Not cash in the drawer:
    /// it is owed until recorded as received on the Deliveries page.
    /// </summary>
    Delivery = 6,
}

/// <summary>Where one part of a refund was paid out.</summary>
public enum RefundMethod
{
    Cash = 0,
    Card = 1,
    StoreCredit = 2,
    MobileWallet = 3,
    LoyaltyPoints = 4,

    /// <summary>Paid out in Lebanese pounds at the day's rate. The refund amount is in dollars.</summary>
    CashLbp = 5,

    /// <summary>Taken off what the delivery company still owes for the sale (its money never reached the store).</summary>
    Delivery = 6,
}

/// <summary>
/// What the cashier chose for the cash, card and wallet parts of a refund. Parts paid with store credit or
/// loyalty points always go back as store credit or points.
/// </summary>
public enum RefundDestination
{
    /// <summary>Each part goes back the way it was paid: cash to cash, card to card, wallet to wallet.</summary>
    OriginalPayment = 0,

    /// <summary>Everything becomes store credit (e.g. for an exchange).</summary>
    StoreCredit = 1,

    /// <summary>Card and wallet parts are paid out in cash too. Needs manager approval.</summary>
    Cash = 2,
}

public enum DiscountType
{
    None = 0,
    Percent = 1,
    Amount = 2,
}

public enum StockMovementType
{
    InitialStock = 0,
    Sale = 1,
    Return = 2,
    PurchaseReceipt = 3,
    Adjustment = 4,
    Damaged = 5,
    Void = 6,
    StockCount = 7,

    // 8 and 9 were used by the old online-orders screen; kept so their history still reads correctly.
    /// <summary>Held for an order on the old online-orders screen.</summary>
    OnlineOrder = 8,

    /// <summary>Put back when an order on the old online-orders screen was cancelled or removed.</summary>
    OnlineOrderCancelled = 9,
}

public enum PurchaseOrderStatus
{
    Draft = 0,
    Ordered = 1,
    PartiallyReceived = 2,
    Received = 3,
    Cancelled = 4,
}

public enum ShiftStatus
{
    Open = 0,
    Closed = 1,
}

public enum BackupKind
{
    Manual = 0,
    Scheduled = 1,
    ShiftClose = 2,

    /// <summary>Taken when the app starts, before the database is upgraded.</summary>
    Startup = 3,
}

public enum CashMovementType
{
    PayIn = 0,
    PayOut = 1,
}

/// <summary>A physical currency in the cash drawer.</summary>
public enum CashCurrency
{
    Usd = 0,
    Lbp = 1,
}

/// <summary>How change is handed back when the customer paid more than the cash due.</summary>
public enum ChangeCurrency
{
    Usd = 0,
    Lbp = 1,

    /// <summary>Whole dollars in USD and the rest in LBP (there are no dollar coins).</summary>
    Mixed = 2,
}

/// <summary>Where a sale came from.</summary>
public enum SalesChannel
{
    InStore = 0,
    WhatsApp = 1,
    Instagram = 2,
    Facebook = 3,
    Phone = 4,
    Website = 5,
    Other = 6,
}


/// <summary>How a delivery company's payment reached the store.</summary>
public enum SettlementMethod
{
    /// <summary>Dollars, into the cash drawer.</summary>
    Cash = 0,

    /// <summary>Lebanese pounds, into the cash drawer.</summary>
    CashLbp = 1,

    /// <summary>Bank or wallet transfer (Whish, OMT...): not in the drawer.</summary>
    Transfer = 2,
}

/// <summary>Whether a delivery partner is a company or a single driver.</summary>
public enum DeliveryPartnerKind
{
    Company = 0,
    Driver = 1,
}
