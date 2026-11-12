using Dapper;
using Microsoft.Data.Sqlite;
using RixelPOS.Domain.Entities;
using RixelPOS.Domain.Enums;

namespace RixelPOS.Infrastructure.Data;

public sealed partial class SqliteStore
{
    public IReadOnlyList<Supplier> GetSuppliers(string? search = null) =>
        WithConnection(c =>
        {
            if (string.IsNullOrWhiteSpace(search))
            {
                return c.Query<Supplier>("""
                    SELECT * FROM suppliers WHERE is_active = 1 ORDER BY name COLLATE NOCASE
                    """).ToList();
            }

            var like = $"%{search.Trim()}%";
            return c.Query<Supplier>("""
                SELECT * FROM suppliers
                WHERE is_active = 1 AND (name LIKE @like COLLATE NOCASE OR phone LIKE @like OR contact_name LIKE @like)
                ORDER BY name COLLATE NOCASE
                """, new { like }).ToList();
        });

    public Supplier? GetSupplier(Guid id) =>
        WithConnection(c => c.QuerySingleOrDefault<Supplier>(
            "SELECT * FROM suppliers WHERE id = @id", new { id = id.ToString() }));

    public void SaveSupplier(Supplier supplier) =>
        WithConnection((c, tx) => c.Execute("""
            INSERT INTO suppliers(id, name, contact_name, phone, email, address, tax_id, notes, is_active, created_at)
            VALUES(@id, @name, @contactName, @phone, @email, @address, @taxId, @notes, @isActive, @createdAt)
            ON CONFLICT(id) DO UPDATE SET
                name = excluded.name, contact_name = excluded.contact_name, phone = excluded.phone,
                email = excluded.email, address = excluded.address, tax_id = excluded.tax_id,
                notes = excluded.notes, is_active = excluded.is_active
            """,
            new
            {
                id = supplier.Id.ToString(),
                supplier.Name,
                supplier.ContactName,
                supplier.Phone,
                supplier.Email,
                supplier.Address,
                supplier.TaxId,
                supplier.Notes,
                supplier.IsActive,
                supplier.CreatedAt
            }, tx));

    public IReadOnlyList<PurchaseOrder> GetPurchaseOrders(int limit = 100)
    {
        var orders = WithConnection(c => c.Query<PurchaseOrder>("""
            SELECT * FROM purchase_orders ORDER BY ordered_at DESC LIMIT @limit
            """, new { limit }).ToList());
        AttachPurchaseLines(orders);
        return orders;
    }

    public PurchaseOrder? GetPurchaseOrder(Guid id)
    {
        var order = WithConnection(c => c.QuerySingleOrDefault<PurchaseOrder>(
            "SELECT * FROM purchase_orders WHERE id = @id", new { id = id.ToString() }));
        if (order is null)
        {
            return null;
        }

        AttachPurchaseLines(new[] { order });
        return order;
    }

    public string NextPurchaseOrderNumber()
    {
        var sequence = NextSequence("purchase");
        return $"OC-{sequence:D6}";
    }

    public void SavePurchaseOrder(PurchaseOrder order) =>
        InTransaction((connection, transaction) =>
        {
            connection.Execute("""
                INSERT INTO purchase_orders(id, number, supplier_id, supplier_name, status, ordered_at, received_at, total_cents, notes)
                VALUES(@id, @number, @supplierId, @supplierName, @status, @orderedAt, @receivedAt, @total, @notes)
                ON CONFLICT(id) DO UPDATE SET
                    status = excluded.status, received_at = excluded.received_at,
                    total_cents = excluded.total_cents, notes = excluded.notes
                """,
                new
                {
                    id = order.Id.ToString(),
                    order.Number,
                    supplierId = order.SupplierId?.ToString(),
                    order.SupplierName,
                    status = order.Status.ToString(),
                    order.OrderedAt,
                    order.ReceivedAt,
                    total = order.TotalCents,
                    order.Notes
                }, transaction);

            connection.Execute("DELETE FROM purchase_order_lines WHERE purchase_order_id = @id",
                new { id = order.Id.ToString() }, transaction);

            foreach (var line in order.Lines)
            {
                connection.Execute("""
                    INSERT INTO purchase_order_lines
                        (id, purchase_order_id, product_id, product_name, sku, quantity_milli, unit_cost_cents, received_quantity_milli)
                    VALUES
                        (@id, @orderId, @productId, @productName, @sku, @quantityMilli, @unitCost, @receivedMilli)
                    """,
                    new
                    {
                        id = line.Id.ToString(),
                        orderId = order.Id.ToString(),
                        productId = line.ProductId.ToString(),
                        line.ProductName,
                        line.Sku,
                        quantityMilli = DbConvert.ToMilli(line.Quantity),
                        unitCost = line.UnitCostCents,
                        receivedMilli = DbConvert.ToMilli(line.ReceivedQuantity)
                    }, transaction);
            }

            return true;
        });

    public void ReceivePurchaseOrder(Guid orderId, Guid userId, string userName) =>
        InTransaction((connection, transaction) =>
        {
            var order = connection.QuerySingleOrDefault<PurchaseOrder>(
                "SELECT * FROM purchase_orders WHERE id = @id", new { id = orderId.ToString() }, transaction)
                ?? throw new Domain.Common.BusinessRuleException("purchase.not_found", "Purchase order not found.");
            if (order.Status is PurchaseOrderStatus.Received or PurchaseOrderStatus.Cancelled)
            {
                throw new Domain.Common.BusinessRuleException("purchase.invalid_status", "The purchase order cannot be received.");
            }

            var lines = connection.Query<PurchaseOrderLineRow>("""
                SELECT * FROM purchase_order_lines WHERE purchase_order_id = @id
                """, new { id = orderId.ToString() }, transaction).ToList();

            foreach (var row in lines)
            {
                var line = row.ToDomain();
                var remaining = Math.Round(line.Quantity - line.ReceivedQuantity, 3);
                if (remaining <= 0)
                {
                    continue;
                }

                AdjustStock(line.ProductId, remaining, StockMovementType.Purchase, userId, userName,
                    order.Number, order.Id, null);

                connection.Execute(
                    "UPDATE purchase_order_lines SET received_quantity_milli = quantity_milli WHERE id = @id",
                    new { id = line.Id.ToString() }, transaction);

                if (line.UnitCostCents > 0)
                {
                    connection.Execute(
                        "UPDATE products SET cost_cents = @cost, updated_at = @now WHERE id = @productId",
                        new { cost = line.UnitCostCents, now = DateTime.UtcNow, productId = line.ProductId.ToString() },
                        transaction);
                }
            }

            connection.Execute("""
                UPDATE purchase_orders SET status = 'Received', received_at = @now WHERE id = @id
                """, new { now = DateTime.UtcNow, id = orderId.ToString() }, transaction);

            return true;
        });

    private void AttachPurchaseLines(IReadOnlyList<PurchaseOrder> orders)
    {
        if (orders.Count == 0)
        {
            return;
        }

        var ids = orders.Select(o => o.Id.ToString()).ToList();
        var lines = WithConnection(c => c.Query<PurchaseOrderLineRow>(
            "SELECT * FROM purchase_order_lines WHERE purchase_order_id IN @ids", new { ids }).ToList());
        var map = lines.GroupBy(l => l.PurchaseOrderId).ToDictionary(g => g.Key, g => g.Select(l => l.ToDomain()).ToList());

        foreach (var order in orders)
        {
            if (map.TryGetValue(order.Id.ToString(), out var orderLines))
            {
                order.Lines = orderLines;
            }
        }
    }

    internal sealed class PurchaseOrderLineRow
    {
        public string Id { get; set; } = "";
        public string PurchaseOrderId { get; set; } = "";
        public string ProductId { get; set; } = "";
        public string ProductName { get; set; } = "";
        public string Sku { get; set; } = "";
        public long QuantityMilli { get; set; }
        public long UnitCostCents { get; set; }
        public long ReceivedQuantityMilli { get; set; }

        public PurchaseOrderLine ToDomain() => new()
        {
            Id = Guid.Parse(Id),
            PurchaseOrderId = Guid.Parse(PurchaseOrderId),
            ProductId = Guid.Parse(ProductId),
            ProductName = ProductName,
            Sku = Sku,
            Quantity = DbConvert.FromMilli(QuantityMilli),
            UnitCostCents = UnitCostCents,
            ReceivedQuantity = DbConvert.FromMilli(ReceivedQuantityMilli)
        };
    }
}
