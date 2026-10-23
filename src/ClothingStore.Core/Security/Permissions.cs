namespace ClothingStore.Core.Security;

public enum Permission
{
    Sell,
    ProcessReturns,
    ManageCustomers,
    ViewProducts,
    ManageProducts,
    ManageInventory,
    ManagePurchasing,
    ViewReports,
    VoidSales,
    AdjustStoreCredit,
    OverrideDiscountLimit,

    /// <summary>Refund card or wallet payments in cash.</summary>
    OverrideRefundMethod,
    /// <summary>Create and edit any account, including managers and admins.</summary>
    ManageUsers,

    /// <summary>Create and edit cashier accounts only.</summary>
    ManageCashiers,
    ManageSettings,
    ViewAllShifts,

    /// <summary>Change the LBP exchange rate.</summary>
    ChangeExchangeRate,

    /// <summary>Change the store logo (the app icon).</summary>
    ManageBranding,

    /// <summary>See how much a customer has spent in total (cashiers only see individual receipts).</summary>
    ViewCustomerSpending,

    /// <summary>Mark delivery-company money as received by bank transfer (no cash in the drawer).</summary>
    SettleByTransfer,
}

public static class Permissions
{
    private static readonly HashSet<Permission> CashierPermissions =
    [
        Permission.Sell,
        Permission.ProcessReturns,
        Permission.ManageCustomers,
        Permission.ViewProducts,
    ];

    private static readonly HashSet<Permission> ManagerPermissions =
    [
        .. CashierPermissions,
        Permission.ManageProducts,
        Permission.ManageInventory,
        Permission.ManagePurchasing,
        Permission.ViewReports,
        Permission.VoidSales,
        Permission.AdjustStoreCredit,
        Permission.OverrideDiscountLimit,
        Permission.OverrideRefundMethod,
        Permission.ViewAllShifts,
        Permission.ChangeExchangeRate,
        Permission.ManageBranding,
        Permission.ManageCashiers,
        Permission.ViewCustomerSpending,
        Permission.SettleByTransfer,
    ];

    public static bool Has(UserRole role, Permission permission) => role switch
    {
        UserRole.Admin => true,
        UserRole.Manager => ManagerPermissions.Contains(permission),
        UserRole.Cashier => CashierPermissions.Contains(permission),
        _ => false,
    };
}
