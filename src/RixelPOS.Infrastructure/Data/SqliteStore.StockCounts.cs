using Dapper;
using RixelPOS.Domain.Common;
using RixelPOS.Domain.Entities;
using RixelPOS.Domain.Enums;

namespace RixelPOS.Infrastructure.Data;

public sealed partial class SqliteStore
{
    public StockCount CreateStockCount(Guid userId, string userName, string? notes) =>
        InTransaction((connection, transaction) =>
        {
            var sequence = connection.ExecuteScalar<long>("""
                INSERT INTO sequences(name, value) VALUES('stock_count', 1)
                ON CONFLICT(name) DO UPDATE SET value = value + 1
                RETURNING value;
                """, transaction: transaction);

            var count = new StockCount
            {
                Number = sequence,
                Status = StockCountStatus.Open,
                CreatedAt = DateTime.UtcNow,
                UserId = userId,
                UserName = userName,
                Notes = notes
            };

            connection.Execute("""
                INSERT INTO stock_counts(id, number, status, created_at, user_id, user_name, notes)
                VALUES(@id, @number, @status, @createdAt, @userId, @userName, @notes)
                """,
                new
                {
                    id = count.Id.ToString(),
                    count.Number,
                    status = count.Status.ToString(),
                    count.CreatedAt,
                    userId = userId.ToString(),
                    userName,
                    notes
                }, transaction);

            return count;
        });

    public StockCount? GetOpenStockCount()
    {
        var count = WithConnection(c => c.QueryFirstOrDefault<StockCount>("""
            SELECT * FROM stock_counts WHERE status = 'Open' ORDER BY created_at DESC LIMIT 1
            """));
        if (count is null)
        {
            return null;
        }

        AttachStockCountLines(new[] { count });
        return count;
    }

    public void AddStockCountLine(Guid countId, Guid productId, decimal countedQuantity)
    {
        if (countedQuantity <= 0)
        {
            throw new BusinessRuleException("stock_count.invalid_quantity", "The counted quantity must be positive.");
        }

        WithConnection((connection, transaction) =>
        {
            var affected = connection.Execute("""
                INSERT INTO stock_count_lines
                    (id, stock_count_id, product_id, product_name, sku, system_quantity_milli, counted_quantity_milli)
                SELECT @id, @countId, p.id, p.name, p.sku, p.stock_milli, @counted
                FROM products p WHERE p.id = @productId
                ON CONFLICT(stock_count_id, product_id) DO UPDATE SET
                    counted_quantity_milli = counted_quantity_milli + excluded.counted_quantity_milli
                """,
                new
                {
                    id = Guid.NewGuid().ToString(),
                    countId = countId.ToString(),
                    productId = productId.ToString(),
                    counted = DbConvert.ToMilli(countedQuantity)
                }, transaction);

            if (affected == 0)
            {
                throw new BusinessRuleException("product.not_found", "Product not found.");
            }

            return true;
        });
    }

    public void CloseStockCount(Guid countId, Guid userId, string userName, bool applyAdjustments)
    {
        InTransaction((connection, transaction) =>
        {
            var count = connection.QuerySingleOrDefault<StockCount>(
                "SELECT * FROM stock_counts WHERE id = @id AND status = 'Open'",
                new { id = countId.ToString() }, transaction)
                ?? throw new BusinessRuleException("stock_count.not_open", "The stock count is not open.");

            var lines = connection.Query<StockCountLineRow>(
                "SELECT * FROM stock_count_lines WHERE stock_count_id = @id",
                new { id = countId.ToString() }, transaction).Select(l => l.ToDomain()).ToList();

            if (applyAdjustments)
            {
                foreach (var line in lines.Where(l => l.Difference != 0))
                {
                    AdjustStock(line.ProductId, line.Difference, StockMovementType.Count, userId, userName,
                        $"Conteo #{count.Number}", count.Id, "Ajuste por conteo físico");
                }
            }

            connection.Execute("""
                UPDATE stock_counts SET status = 'Closed', closed_at = @now WHERE id = @id
                """, new { now = DateTime.UtcNow, id = countId.ToString() }, transaction);

            return true;
        });
    }

    public IReadOnlyList<StockCount> GetStockCounts(int limit = 30)
    {
        var counts = WithConnection(c => c.Query<StockCount>(
            "SELECT * FROM stock_counts ORDER BY created_at DESC LIMIT @limit", new { limit }).ToList());
        AttachStockCountLines(counts);
        return counts;
    }

    private void AttachStockCountLines(IReadOnlyList<StockCount> counts)
    {
        if (counts.Count == 0)
        {
            return;
        }

        var ids = counts.Select(c => c.Id.ToString()).ToList();
        var lines = WithConnection(c => c.Query<StockCountLineRow>(
            "SELECT * FROM stock_count_lines WHERE stock_count_id IN @ids", new { ids }).ToList());
        var map = lines.GroupBy(l => l.StockCountId).ToDictionary(g => g.Key, g => g.Select(l => l.ToDomain()).ToList());

        foreach (var count in counts)
        {
            if (map.TryGetValue(count.Id.ToString(), out var countLines))
            {
                count.Lines = countLines;
            }
        }
    }

    public void AddAudit(AuditEntry entry) =>
        WithConnection((c, tx) => c.Execute("""
            INSERT INTO audit_log(id, user_id, user_name, action, entity_type, entity_id, details, created_at)
            VALUES(@id, @userId, @userName, @action, @entityType, @entityId, @details, @createdAt)
            """,
            new
            {
                id = entry.Id.ToString(),
                userId = entry.UserId?.ToString(),
                entry.UserName,
                entry.Action,
                entry.EntityType,
                entry.EntityId,
                entry.Details,
                entry.CreatedAt
            }, tx));

    public IReadOnlyList<AuditEntry> GetAuditEntries(int limit = 200) =>
        WithConnection(c => c.Query<AuditEntry>(
            "SELECT * FROM audit_log ORDER BY created_at DESC LIMIT @limit", new { limit }).ToList());

    internal sealed class StockCountLineRow
    {
        public string Id { get; set; } = "";
        public string StockCountId { get; set; } = "";
        public string ProductId { get; set; } = "";
        public string ProductName { get; set; } = "";
        public string Sku { get; set; } = "";
        public long SystemQuantityMilli { get; set; }
        public long CountedQuantityMilli { get; set; }

        public StockCountLine ToDomain() => new()
        {
            Id = Guid.Parse(Id),
            StockCountId = Guid.Parse(StockCountId),
            ProductId = Guid.Parse(ProductId),
            ProductName = ProductName,
            Sku = Sku,
            SystemQuantity = DbConvert.FromMilli(SystemQuantityMilli),
            CountedQuantity = DbConvert.FromMilli(CountedQuantityMilli)
        };
    }
}
