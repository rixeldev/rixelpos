using Dapper;
using RixelPOS.Domain.Enums;
using RixelPOS.Domain.Reporting;

namespace RixelPOS.Infrastructure.Data;

public sealed partial class SqliteStore
{
    public SalesReport BuildSalesReport(DateTime from, DateTime to)
    {
        var utcFrom = from.Kind == DateTimeKind.Utc ? from : from.ToUniversalTime();
        var utcTo = to.Kind == DateTimeKind.Utc ? to : to.ToUniversalTime();
        var sales = GetCompletedSales(utcFrom, utcTo);

        var allLines = sales.SelectMany(s => s.Lines).ToList();
        var total = sales.Sum(s => s.TotalCents);
        var tax = sales.Sum(s => s.TaxCents);
        var discount = sales.Sum(s => s.DiscountCents);
        var cost = allLines.Sum(l => (long)Math.Round(l.Quantity * l.CostCents, MidpointRounding.AwayFromZero));
        var itemCount = (int)Math.Round(allLines.Sum(l => l.Quantity), MidpointRounding.AwayFromZero);

        var daily = new List<DailySalesPoint>();
        var startDay = DateOnly.FromDateTime(from);
        var endDay = DateOnly.FromDateTime(to);
        if (endDay < startDay)
        {
            endDay = startDay;
        }

        if (endDay.DayNumber - startDay.DayNumber <= 370)
        {
            var byDay = sales
                .GroupBy(s => DateOnly.FromDateTime((s.CompletedAt ?? s.CreatedAt).ToLocalTime()))
                .ToDictionary(g => g.Key, g => (Total: g.Sum(s => s.TotalCents), Count: g.Count()));
            for (var day = startDay; day <= endDay; day = day.AddDays(1))
            {
                var entry = byDay.TryGetValue(day, out var value) ? value : (Total: 0L, Count: 0);
                daily.Add(new DailySalesPoint(day, entry.Total, entry.Count));
            }
        }

        var topProducts = allLines
            .GroupBy(l => new { l.ProductId, l.Name })
            .Select(g =>
            {
                var lineTotal = g.Sum(l => l.TotalCents);
                var lineTax = g.Sum(l => l.TaxCents);
                var lineCost = g.Sum(l => (long)Math.Round(l.Quantity * l.CostCents, MidpointRounding.AwayFromZero));
                return new ProductSalesRow(
                    g.Key.ProductId ?? Guid.Empty,
                    g.Key.Name,
                    Math.Round(g.Sum(l => l.Quantity), 3),
                    lineTotal,
                    lineCost,
                    lineTotal - lineTax - lineCost);
            })
            .OrderByDescending(r => r.TotalCents)
            .Take(30)
            .ToList();

        var payments = sales.SelectMany(s => s.Payments)
            .GroupBy(p => p.Method)
            .ToDictionary(g => g.Key, g => (Total: g.Sum(p => p.AmountCents), Count: g.Count()));
        var byPayment = Enum.GetValues<PaymentMethod>()
            .Select(m => new PaymentTotalRow(
                m,
                payments.TryGetValue(m, out var value) ? value.Total : 0,
                payments.TryGetValue(m, out var count) ? count.Count : 0))
            .ToList();

        var byCashier = sales
            .GroupBy(s => new { s.CashierId, s.CashierName })
            .Select(g => new CashierTotalRow(g.Key.CashierId, string.IsNullOrWhiteSpace(g.Key.CashierName) ? "—" : g.Key.CashierName,
                g.Sum(s => s.TotalCents), g.Count()))
            .OrderByDescending(r => r.TotalCents)
            .ToList();

        var categoryMap = WithConnection(c => c.Query<ProductCategoryRow>("""
            SELECT p.id AS ProductId, COALESCE(c.name, 'General') AS CategoryName
            FROM products p LEFT JOIN categories c ON c.id = p.category_id
            """).ToDictionary(r => r.ProductId, r => r.CategoryName, StringComparer.OrdinalIgnoreCase));

        var byCategory = allLines
            .GroupBy(l => l.ProductId is not null && categoryMap.TryGetValue(l.ProductId.Value.ToString(), out var name)
                ? name
                : "Sin categoría")
            .Select(g => new CategoryTotalRow(
                g.Key,
                g.Sum(l => l.TotalCents),
                Math.Round(g.Sum(l => l.Quantity), 3)))
            .OrderByDescending(r => r.TotalCents)
            .ToList();

        return new SalesReport(
            from, to, total, tax, discount, cost, sales.Count, itemCount,
            daily, topProducts, byPayment, byCashier, byCategory);
    }

    public DashboardSnapshot BuildDashboard(DateTime today, int lowStockLimit = 8)
    {
        var localToday = today.Kind == DateTimeKind.Utc ? today.ToLocalTime() : today;
        var startLocal = localToday.Date;
        var endLocal = startLocal.AddDays(1);
        var now = DateTime.Now;
        var utcStart = startLocal.ToUniversalTime();
        var utcNow = now.ToUniversalTime();
        var monthStart = new DateTime(startLocal.Year, startLocal.Month, 1).ToUniversalTime();

        var todaySales = GetCompletedSales(utcStart, utcNow);
        var todayTotal = todaySales.Sum(s => s.TotalCents);

        var monthTotal = WithConnection(c => c.ExecuteScalar<long>("""
            SELECT COALESCE(SUM(total_cents), 0) FROM sales
            WHERE status = 'Completed' AND completed_at >= @from AND completed_at <= @to
            """, new { from = monthStart, to = utcNow }));

        var lowStockCount = WithConnection(c => c.ExecuteScalar<int>("""
            SELECT COUNT(1) FROM products
            WHERE is_active = 1 AND track_stock = 1 AND stock_milli <= min_stock_milli
            """));
        var productCount = WithConnection(c => c.ExecuteScalar<int>(
            "SELECT COUNT(1) FROM products WHERE is_active = 1"));
        var customerCount = WithConnection(c => c.ExecuteScalar<int>(
            "SELECT COUNT(1) FROM customers WHERE is_active = 1"));

        var topProducts = todaySales.SelectMany(s => s.Lines)
            .GroupBy(l => new { l.ProductId, l.Name })
            .Select(g => new ProductSalesRow(
                g.Key.ProductId ?? Guid.Empty,
                g.Key.Name,
                Math.Round(g.Sum(l => l.Quantity), 3),
                g.Sum(l => l.TotalCents),
                g.Sum(l => (long)Math.Round(l.Quantity * l.CostCents, MidpointRounding.AwayFromZero)),
                g.Sum(l => l.TotalCents - l.TaxCents - (long)Math.Round(l.Quantity * l.CostCents, MidpointRounding.AwayFromZero))))
            .OrderByDescending(r => r.TotalCents)
            .Take(5)
            .ToList();

        return new DashboardSnapshot(
            todayTotal,
            todaySales.Count,
            monthTotal,
            lowStockCount,
            productCount,
            customerCount,
            GetOpenCashSession(),
            GetRecentSales(8),
            topProducts,
            GetLowStockProducts(lowStockLimit));
    }

    private sealed class ProductCategoryRow
    {
        public string ProductId { get; set; } = "";
        public string CategoryName { get; set; } = "";
    }
}
