using RixelPOS.Domain.Common;
using RixelPOS.Domain.Enums;

namespace RixelPOS.Domain.Entities;

public sealed class Sale
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public long Number { get; set; }
    public SaleStatus Status { get; set; } = SaleStatus.Held;
    public Guid? CashierId { get; set; }
    public string CashierName { get; set; } = "";
    public Guid? CustomerId { get; set; }
    public string? CustomerName { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }
    public long SubtotalCents { get; set; }
    public long DiscountCents { get; set; }
    public long TaxCents { get; set; }
    public long TotalCents { get; set; }
    public long PaidCents { get; set; }
    public long ChangeCents { get; set; }
    public Common.Discount SaleDiscount { get; set; } = Common.Discount.None;
    public string? Note { get; set; }
    public string? HeldLabel { get; set; }
    public Guid? CashSessionId { get; set; }
    public bool IsCredit { get; set; }
    public DateTime? VoidedAt { get; set; }
    public Guid? VoidedBy { get; set; }
    public string? VoidReason { get; set; }
    public List<SaleLine> Lines { get; set; } = new();
    public List<Payment> Payments { get; set; } = new();
    public Invoice? Invoice { get; set; }

    public Money Total => Money.FromCents(TotalCents);
    public Money Subtotal => Money.FromCents(SubtotalCents);
    public Money Tax => Money.FromCents(TaxCents);
    public Money Discount => Money.FromCents(DiscountCents);
    public Money Paid => Money.FromCents(PaidCents);
    public Money Change => Money.FromCents(ChangeCents);

    public decimal ItemCount => Lines.Sum(l => l.Quantity);
}

public sealed class SaleLine
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SaleId { get; set; }
    public Guid? ProductId { get; set; }
    public string Sku { get; set; } = "";
    public string Name { get; set; } = "";
    public decimal Quantity { get; set; } = 1;
    public long UnitPriceCents { get; set; }
    public long CostCents { get; set; }
    public Common.Discount Discount { get; set; } = Common.Discount.None;
    public int TaxRateBasisPoints { get; set; }
    public bool TaxInclusive { get; set; }
    public long DiscountCents { get; set; }
    public long NetCents { get; set; }
    public long TaxCents { get; set; }
    public long TotalCents { get; set; }

    public Money UnitPrice => Money.FromCents(UnitPriceCents);
    public Money Total => Money.FromCents(TotalCents);
    public Money Tax => Money.FromCents(TaxCents);

    public SaleLine Clone() => (SaleLine)MemberwiseClone();
}

public sealed class Payment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SaleId { get; set; }
    public PaymentMethod Method { get; set; }
    public long AmountCents { get; set; }
    public long TenderedCents { get; set; }
    public string? Reference { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public Money Amount => Money.FromCents(AmountCents);
}

public sealed class Invoice
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SaleId { get; set; }
    public string Number { get; set; } = "";
    public long Sequence { get; set; }
    public string Prefix { get; set; } = "";
    public DateTime IssuedAt { get; set; } = DateTime.UtcNow;
    public string? CustomerName { get; set; }
    public string? CustomerTaxId { get; set; }
    public long TotalCents { get; set; }
    public string? FiscalData { get; set; }
    public InvoiceStatus Status { get; set; } = InvoiceStatus.Issued;

    public string? Ncf => FiscalData is null ? null : TryReadFiscal("ncf", FiscalData);

    private static string? TryReadFiscal(string key, string json)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty(key, out var value) ? value.GetString() : null;
        }
        catch
        {
            return null;
        }
    }
}
