using RixelPOS.Domain.Enums;

namespace RixelPOS.Domain.Entities;

public sealed class Supplier
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string? ContactName { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? Address { get; set; }
    public string? TaxId { get; set; }
    public string? Notes { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public sealed class PurchaseOrder
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Number { get; set; } = "";
    public Guid? SupplierId { get; set; }
    public string SupplierName { get; set; } = "";
    public PurchaseOrderStatus Status { get; set; } = PurchaseOrderStatus.Draft;
    public DateTime OrderedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ReceivedAt { get; set; }
    public long TotalCents { get; set; }
    public string? Notes { get; set; }
    public List<PurchaseOrderLine> Lines { get; set; } = new();
}

public sealed class PurchaseOrderLine
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PurchaseOrderId { get; set; }
    public Guid ProductId { get; set; }
    public string ProductName { get; set; } = "";
    public string Sku { get; set; } = "";
    public decimal Quantity { get; set; }
    public long UnitCostCents { get; set; }
    public decimal ReceivedQuantity { get; set; }
    public long LineTotalCents => (long)Math.Round(Quantity * UnitCostCents, MidpointRounding.AwayFromZero);
}
