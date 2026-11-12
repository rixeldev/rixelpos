using RixelPOS.Domain.Common;
using RixelPOS.Domain.Enums;

namespace RixelPOS.Domain.Entities;

public sealed class CashSession
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTime OpenedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ClosedAt { get; set; }
    public long OpeningCents { get; set; }
    public long ExpectedCents { get; set; }
    public long? ClosingCents { get; set; }
    public long? DifferenceCents { get; set; }
    public Guid? OpenedBy { get; set; }
    public string OpenedByName { get; set; } = "";
    public Guid? ClosedBy { get; set; }
    public string? ClosedByName { get; set; }
    public string? Notes { get; set; }
    public bool IsOpen => ClosedAt is null;

    public Money Opening => Money.FromCents(OpeningCents);
    public Money Expected => Money.FromCents(ExpectedCents);
    public Money? Closing => ClosingCents is null ? null : Money.FromCents(ClosingCents.Value);
    public Money? Difference => DifferenceCents is null ? null : Money.FromCents(DifferenceCents.Value);
}

public sealed class CashMovement
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SessionId { get; set; }
    public CashMovementType Type { get; set; }
    public long AmountCents { get; set; }
    public string Reason { get; set; } = "";
    public Guid? UserId { get; set; }
    public string UserName { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public Money Amount => Money.FromCents(AmountCents);
}

public sealed class StockMovement
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProductId { get; set; }
    public string ProductName { get; set; } = "";
    public StockMovementType Type { get; set; }
    public decimal Quantity { get; set; }
    public string? ReferenceId { get; set; }
    public string? Reference { get; set; }
    public Guid? UserId { get; set; }
    public string? Note { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public sealed class StockCount
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public long Number { get; set; }
    public StockCountStatus Status { get; set; } = StockCountStatus.Open;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ClosedAt { get; set; }
    public Guid? UserId { get; set; }
    public string UserName { get; set; } = "";
    public string? Notes { get; set; }
    public List<StockCountLine> Lines { get; set; } = new();
}

public sealed class StockCountLine
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid StockCountId { get; set; }
    public Guid ProductId { get; set; }
    public string ProductName { get; set; } = "";
    public string Sku { get; set; } = "";
    public decimal SystemQuantity { get; set; }
    public decimal CountedQuantity { get; set; }
    public decimal Difference => Math.Round(CountedQuantity - SystemQuantity, 3);
}

public sealed class AuditEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? UserId { get; set; }
    public string UserName { get; set; } = "";
    public string Action { get; set; } = "";
    public string EntityType { get; set; } = "";
    public string? EntityId { get; set; }
    public string? Details { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
