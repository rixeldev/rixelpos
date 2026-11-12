using RixelPOS.Domain.Common;

namespace RixelPOS.Domain.Entities;

public sealed class Product
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Sku { get; set; } = "";
    public string Name { get; set; } = "";
    public Guid? CategoryId { get; set; }
    public string? CategoryName { get; set; }
    public long CostCents { get; set; }
    public long PriceCents { get; set; }
    public int TaxRateBasisPoints { get; set; } = Defaults.DefaultTaxRateBasisPoints;

    /// <summary>True when <see cref="PriceCents"/> already includes tax (default for new products).</summary>
    public bool TaxInclusive { get; set; } = true;
    public decimal Stock { get; set; }
    public decimal MinStock { get; set; }
    public string Unit { get; set; } = "unit";
    public bool IsFavorite { get; set; }
    public bool IsActive { get; set; } = true;
    public bool TrackStock { get; set; } = true;
    public string? ImagePath { get; set; }
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public List<ProductBarcode> Barcodes { get; set; } = new();

    public Money Price => Money.FromCents(PriceCents);
    public Money Cost => Money.FromCents(CostCents);
    public bool IsLowStock => TrackStock && Stock <= MinStock;
    public bool IsOutOfStock => TrackStock && Stock <= 0;

    public string? PrimaryBarcode => Barcodes.FirstOrDefault(b => b.IsPrimary)?.Code
                                     ?? Barcodes.FirstOrDefault()?.Code;

    public void AdjustStock(decimal delta)
    {
        if (!TrackStock)
        {
            return;
        }

        Stock = Math.Round(Stock + delta, 3, MidpointRounding.AwayFromZero);
        UpdatedAt = DateTime.UtcNow;
    }

    public bool Matches(string term)
    {
        if (string.IsNullOrWhiteSpace(term))
        {
            return true;
        }

        term = term.Trim();
        return Name.Contains(term, StringComparison.OrdinalIgnoreCase)
               || Sku.Contains(term, StringComparison.OrdinalIgnoreCase)
               || Barcodes.Any(b => b.Code.Contains(term, StringComparison.OrdinalIgnoreCase));
    }
}

public sealed class ProductBarcode
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProductId { get; set; }
    public string Code { get; set; } = "";
    public bool IsPrimary { get; set; }
}
