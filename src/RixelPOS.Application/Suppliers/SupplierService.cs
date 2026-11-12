using RixelPOS.Application.Abstractions;
using RixelPOS.Application.Services;
using RixelPOS.Application.Sessions;
using RixelPOS.Domain.Common;
using RixelPOS.Domain.Entities;
using RixelPOS.Domain.Enums;

namespace RixelPOS.Application.Suppliers;

public sealed class SupplierService
{
    private readonly IPosStore _store;
    private readonly AppSession _session;
    private readonly AuditService _audit;
    private readonly IClock _clock;

    public SupplierService(IPosStore store, AppSession session, AuditService audit, IClock clock)
    {
        _store = store;
        _session = session;
        _audit = audit;
        _clock = clock;
    }

    public IReadOnlyList<Supplier> Search(string? term = null) => _store.GetSuppliers(term);

    public Supplier? Get(Guid id) => _store.GetSupplier(id);

    public Supplier Save(Supplier supplier, bool isNew)
    {
        var user = RequireUser();
        if (string.IsNullOrWhiteSpace(supplier.Name))
        {
            throw new BusinessRuleException("supplier.name_required", "Supplier name is required.");
        }

        supplier.Name = supplier.Name.Trim();
        _store.SaveSupplier(supplier);
        _audit.Record(user, isNew ? "supplier.create" : "supplier.update", "supplier", supplier.Id.ToString(), supplier.Name);
        return supplier;
    }

    public IReadOnlyList<PurchaseOrder> GetPurchaseOrders() => _store.GetPurchaseOrders();

    public PurchaseOrder? GetPurchaseOrder(Guid id) => _store.GetPurchaseOrder(id);

    public PurchaseOrder CreatePurchaseOrder(Guid? supplierId, string supplierName, IReadOnlyList<PurchaseOrderLine> lines, string? notes)
    {
        var user = RequireUser();
        if (lines.Count == 0)
        {
            throw new BusinessRuleException("purchase.empty", "Add at least one product to the purchase order.");
        }

        foreach (var line in lines)
        {
            if (line.Quantity <= 0 || line.UnitCostCents < 0)
            {
                throw new BusinessRuleException("purchase.invalid_line", "Quantities must be positive and costs cannot be negative.");
            }
        }

        var order = new PurchaseOrder
        {
            SupplierId = supplierId,
            SupplierName = supplierName,
            Status = PurchaseOrderStatus.Ordered,
            OrderedAt = _clock.UtcNow,
            Notes = notes,
            Lines = lines.ToList()
        };
        order.TotalCents = order.Lines.Sum(l => (long)Math.Round(l.Quantity * l.UnitCostCents, MidpointRounding.AwayFromZero));
        foreach (var line in order.Lines)
        {
            line.PurchaseOrderId = order.Id;
        }

        _store.ExecuteInTransaction(store =>
        {
            var sequence = store.NextSequence("purchase");
            order.Number = $"OC-{sequence:D6}";
            store.SavePurchaseOrder(order);
            _audit.Record(user, "purchase.create", "purchase_order", order.Id.ToString(), order.Number);
        });

        return order;
    }

    public void ReceivePurchaseOrder(Guid orderId)
    {
        var user = RequireUser();
        var order = _store.GetPurchaseOrder(orderId)
            ?? throw new BusinessRuleException("purchase.not_found", "Purchase order not found.");
        if (order.Status is PurchaseOrderStatus.Received or PurchaseOrderStatus.Cancelled)
        {
            throw new BusinessRuleException("purchase.invalid_status", "That purchase order cannot be received.");
        }

        _store.ReceivePurchaseOrder(orderId, user.Id, user.FullName);
        _audit.Record(user, "purchase.receive", "purchase_order", orderId.ToString(), order.Number);
    }

    private User RequireUser() =>
        _session.CurrentUser ?? throw new BusinessRuleException("auth.required", "Please sign in first.");
}
