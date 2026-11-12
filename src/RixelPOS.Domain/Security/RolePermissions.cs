using RixelPOS.Domain.Enums;

namespace RixelPOS.Domain.Security;

public static class RolePermissions
{
    private static readonly Permission[] CashierPermissions =
    {
        Permission.Sell,
        Permission.HoldSale,
        Permission.ReprintReceipt,
        Permission.ViewInventory,
        Permission.ViewCustomers,
        Permission.ManageCustomers,
        Permission.ManageCash
    };

    private static readonly Permission[] ManagerPermissions = CashierPermissions.Concat(new[]
    {
        Permission.ManageInventory,
        Permission.ReceiveStock,
        Permission.StockCount,
        Permission.ViewSuppliers,
        Permission.ManageSuppliers,
        Permission.ManagePurchases,
        Permission.VoidSale,
        Permission.ApplyDiscount,
        Permission.Refund,
        Permission.ViewReports,
        Permission.ViewAudit
    }).ToArray();

    public static bool Can(UserRole role, Permission permission) => role switch
    {
        UserRole.Admin => true,
        UserRole.Manager => ManagerPermissions.Contains(permission),
        UserRole.Cashier => CashierPermissions.Contains(permission),
        _ => false
    };

    public static bool RequiresApproval(UserRole role, Permission permission) =>
        permission is Permission.VoidSale or Permission.Refund && role == UserRole.Cashier;

    public static string DisplayName(UserRole role) => role switch
    {
        UserRole.Admin => "Admin",
        UserRole.Manager => "Manager",
        _ => "Cashier"
    };
}
