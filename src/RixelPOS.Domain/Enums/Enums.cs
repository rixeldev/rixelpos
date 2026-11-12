namespace RixelPOS.Domain.Enums;

public enum UserRole
{
    Cashier = 0,
    Manager = 1,
    Admin = 2
}

public enum SaleStatus
{
    Held = 0,
    Completed = 1,
    Voided = 2
}

public enum PaymentMethod
{
    Cash = 0,
    Card = 1,
    Transfer = 2,
    StoreCredit = 3,
    Credit = 4
}

public enum CashMovementType
{
    In = 0,
    Out = 1
}

public enum StockMovementType
{
    Sale = 0,
    Adjustment = 1,
    Purchase = 2,
    Return = 3,
    Initial = 4,
    Count = 5,
    Void = 6
}

public enum PurchaseOrderStatus
{
    Draft = 0,
    Ordered = 1,
    Received = 2,
    Cancelled = 3
}

public enum StockCountStatus
{
    Open = 0,
    Closed = 1
}

public enum InvoiceStatus
{
    Issued = 0,
    Voided = 1
}

public enum AppLanguage
{
    Spanish = 0,
    English = 1
}

public enum AppTheme
{
    Light = 0,
    Dark = 1,
    System = 2
}

public enum ReceiptWidth
{
    Mm58 = 58,
    Mm80 = 80
}

public enum PrinterConnectionKind
{
    None = 0,
    Network = 1,
    Serial = 2,
    UsbWindows = 3,
    CupsLinux = 4,
    File = 5
}

public enum FiscalMode
{
    None = 0,
    DominicanNcf = 1
}

public enum Permission
{
    Sell = 1,
    HoldSale = 2,
    ReprintReceipt = 3,
    ViewInventory = 10,
    ManageInventory = 11,
    ReceiveStock = 12,
    StockCount = 13,
    ViewCustomers = 20,
    ManageCustomers = 21,
    ViewSuppliers = 30,
    ManageSuppliers = 31,
    ManagePurchases = 32,
    ManageCash = 40,
    VoidSale = 50,
    ApplyDiscount = 51,
    Refund = 52,
    ViewReports = 60,
    ManageSettings = 70,
    ManageUsers = 71,
    BackupRestore = 72,
    ViewAudit = 73
}
