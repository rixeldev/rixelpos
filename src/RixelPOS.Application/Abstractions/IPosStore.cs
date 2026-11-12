using RixelPOS.Domain.Entities;
using RixelPOS.Domain.Reporting;
using RixelPOS.Domain.Settings;

namespace RixelPOS.Application.Abstractions;

/// <summary>
/// Persistence boundary. All multi-step writes go through
/// <see cref="ExecuteInTransaction{T}"/> so a power cut can never leave a half sale.
/// </summary>
public interface IPosStore
{
    T ExecuteInTransaction<T>(Func<IPosStore, T> work);
    void ExecuteInTransaction(Action<IPosStore> work);

    // Settings & sequences
    BusinessSettings LoadSettings();
    void SaveSettings(BusinessSettings settings);
    string? GetSetting(string key);
    long NextSequence(string name);

    // Products and catalog
    IReadOnlyList<Product> GetProducts(bool includeInactive = false);
    IReadOnlyList<Product> SearchProducts(string term, int limit = 30);
    Product? GetProduct(Guid id);
    Product? FindProductByCode(string code);
    IReadOnlyList<Product> GetLowStockProducts(int limit = 50);
    IReadOnlyList<Product> GetFavoriteProducts(int limit = 24);
    bool BarcodeExists(string code, Guid? exceptProductId = null);
    bool SkuExists(string sku, Guid? exceptProductId = null);
    void SaveProduct(Product product);
    void ArchiveProduct(Guid id);
    void AdjustStock(Guid productId, decimal delta, Domain.Enums.StockMovementType type, Guid? userId,
        string? userName, string? reference = null, Guid? referenceId = null, string? note = null);
    IReadOnlyList<StockMovement> GetStockMovements(Guid productId, int limit = 50);
    IReadOnlyList<Category> GetCategories();
    void SaveCategory(Category category);
    IReadOnlyList<TaxRate> GetTaxRates();
    void SaveTaxRate(TaxRate rate);

    // Users
    IReadOnlyList<User> GetUsers();
    User? GetUser(Guid id);
    User? FindUserByUsername(string username);
    IReadOnlyList<User> GetUsersWithPin();
    void SaveUser(User user);
    int CountAdmins();

    // Customers
    IReadOnlyList<Customer> GetCustomers(string? search = null, int limit = 200);
    Customer? GetCustomer(Guid id);
    void SaveCustomer(Customer customer);
    void AdjustCustomerBalances(Guid customerId, long balanceDeltaCents, long storeCreditDeltaCents);
    IReadOnlyList<Sale> GetCustomerSales(Guid customerId, int limit = 50);

    // Sales, invoices
    void InsertSale(Sale sale);
    void UpdateSale(Sale sale);
    void DeleteSale(Guid saleId);
    Sale? GetSale(Guid id);
    Sale? GetSaleByInvoiceNumber(string invoiceNumber);
    IReadOnlyList<Sale> GetRecentSales(int limit = 20);
    IReadOnlyList<Sale> GetHeldSales();
    IReadOnlyList<Sale> GetCompletedSales(DateTime from, DateTime to, int limit = 5000);
    void InsertInvoice(Invoice invoice);
    void UpdateInvoiceStatus(Guid saleId, Domain.Enums.InvoiceStatus status);
    Invoice? GetInvoiceForSale(Guid saleId);
    IReadOnlyList<Invoice> GetInvoices(DateTime from, DateTime to, int limit = 200);

    // Cash register
    CashSession? GetOpenCashSession();
    CashSession? GetCashSession(Guid id);
    IReadOnlyList<CashSession> GetCashSessions(int limit = 30);
    void SaveCashSession(CashSession session);
    void AddCashMovement(CashMovement movement);
    IReadOnlyList<CashMovement> GetCashMovements(Guid sessionId);
    long GetSessionCashSales(Guid sessionId);

    // Suppliers and purchases
    IReadOnlyList<Supplier> GetSuppliers(string? search = null);
    Supplier? GetSupplier(Guid id);
    void SaveSupplier(Supplier supplier);
    IReadOnlyList<PurchaseOrder> GetPurchaseOrders(int limit = 100);
    PurchaseOrder? GetPurchaseOrder(Guid id);
    string NextPurchaseOrderNumber();
    void SavePurchaseOrder(PurchaseOrder order);
    void ReceivePurchaseOrder(Guid orderId, Guid userId, string userName);

    // Stock counts
    StockCount CreateStockCount(Guid userId, string userName, string? notes);
    StockCount? GetOpenStockCount();
    void AddStockCountLine(Guid countId, Guid productId, decimal countedQuantity);
    void CloseStockCount(Guid countId, Guid userId, string userName, bool applyAdjustments);
    IReadOnlyList<StockCount> GetStockCounts(int limit = 30);

    // Audit
    void AddAudit(AuditEntry entry);
    IReadOnlyList<AuditEntry> GetAuditEntries(int limit = 200);

    // Reports
    SalesReport BuildSalesReport(DateTime from, DateTime to);
    DashboardSnapshot BuildDashboard(DateTime today, int lowStockLimit = 8);
}
