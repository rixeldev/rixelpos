using RixelPOS.Domain.Entities;
using RixelPOS.Domain.Enums;

namespace RixelPOS.Domain.Reporting;

public sealed record DailySalesPoint(DateOnly Date, long TotalCents, int SalesCount);

public sealed record ProductSalesRow(Guid ProductId, string Name, decimal Quantity, long TotalCents, long CostCents, long ProfitCents);

public sealed record PaymentTotalRow(PaymentMethod Method, long TotalCents, int Count);

public sealed record CashierTotalRow(Guid? CashierId, string CashierName, long TotalCents, int SalesCount);

public sealed record CategoryTotalRow(string CategoryName, long TotalCents, decimal Quantity);

public sealed record SalesReport(
    DateTime From,
    DateTime To,
    long TotalCents,
    long TaxCents,
    long DiscountCents,
    long CostCents,
    int SalesCount,
    int ItemCount,
    IReadOnlyList<DailySalesPoint> Daily,
    IReadOnlyList<ProductSalesRow> TopProducts,
    IReadOnlyList<PaymentTotalRow> ByPayment,
    IReadOnlyList<CashierTotalRow> ByCashier,
    IReadOnlyList<CategoryTotalRow> ByCategory)
{
    public long ProfitCents => TotalCents - TaxCents - CostCents;
    public long AverageTicketCents => SalesCount == 0 ? 0 : TotalCents / SalesCount;
}

public sealed record ZReport(
    CashSession Session,
    int SalesCount,
    long GrossSalesCents,
    long CashSalesCents,
    long CardSalesCents,
    long TransferSalesCents,
    long CreditSalesCents,
    long StoreCreditCents,
    long CashInCents,
    long CashOutCents,
    long ExpectedCashCents,
    IReadOnlyList<PaymentTotalRow> Payments,
    IReadOnlyList<CashMovement> Movements,
    IReadOnlyList<Sale> Sales)
{
    public long NetSalesCents => GrossSalesCents;
}

public sealed record DashboardSnapshot(
    long TodayTotalCents,
    int TodaySalesCount,
    long MonthTotalCents,
    int LowStockCount,
    int ProductCount,
    int CustomerCount,
    CashSession? OpenSession,
    IReadOnlyList<Sale> RecentSales,
    IReadOnlyList<ProductSalesRow> TopProducts,
    IReadOnlyList<Product> LowStockProducts);
