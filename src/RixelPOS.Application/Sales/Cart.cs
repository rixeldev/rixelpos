using RixelPOS.Domain.Common;
using RixelPOS.Domain.Entities;
using RixelPOS.Domain.Enums;
using RixelPOS.Domain.Services;

namespace RixelPOS.Application.Sales;

/// <summary>The in-progress sale shown on the checkout screen.</summary>
public sealed class Cart
{
    private Sale _sale;

    public Cart()
    {
        _sale = new Sale { Status = SaleStatus.Held, CreatedAt = DateTime.UtcNow };
        Totals = SaleCalculator.Calculate(_sale.Lines, Discount.None);
    }

    public Cart(Sale heldSale)
    {
        _sale = heldSale;
        Totals = SaleCalculator.Calculate(_sale.Lines, _sale.SaleDiscount);
    }

    public Sale Sale => _sale;

    public SaleTotals Totals { get; private set; }

    public IReadOnlyList<SaleLine> Lines => _sale.Lines;

    public bool IsEmpty => _sale.Lines.Count == 0;

    public int DistinctItemCount => _sale.Lines.Count;

    public decimal ItemCount => _sale.Lines.Sum(l => l.Quantity);

    public event EventHandler? Changed;

    public SaleLine AddProduct(Product product, decimal quantity = 1)
    {
        if (quantity <= 0)
        {
            throw new BusinessRuleException("cart.invalid_quantity", "Quantity must be greater than zero.");
        }

        var line = _sale.Lines.FirstOrDefault(l => l.ProductId == product.Id && l.Discount.IsNone);
        if (line is null)
        {
            line = new SaleLine
            {
                SaleId = _sale.Id,
                ProductId = product.Id,
                Sku = product.Sku,
                Name = product.Name,
                Quantity = quantity,
                UnitPriceCents = product.PriceCents,
                CostCents = product.CostCents,
                TaxRateBasisPoints = product.TaxRateBasisPoints,
                TaxInclusive = product.TaxInclusive,
                Discount = Discount.None
            };
            _sale.Lines.Add(line);
        }
        else
        {
            line.Quantity = Math.Round(line.Quantity + quantity, 3, MidpointRounding.AwayFromZero);
        }

        NotifyChanged();
        return line;
    }

    public SaleLine AddCustomLine(string name, long unitPriceCents, int taxRateBasisPoints, bool taxInclusive, decimal quantity = 1)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new BusinessRuleException("cart.name_required", "Product name is required.");
        }

        if (quantity <= 0)
        {
            throw new BusinessRuleException("cart.invalid_quantity", "Quantity must be greater than zero.");
        }

        var line = new SaleLine
        {
            SaleId = _sale.Id,
            ProductId = null,
            Sku = "",
            Name = name.Trim(),
            Quantity = quantity,
            UnitPriceCents = unitPriceCents,
            TaxRateBasisPoints = taxRateBasisPoints,
            TaxInclusive = taxInclusive
        };
        _sale.Lines.Add(line);
        NotifyChanged();
        return line;
    }

    public void SetQuantity(Guid lineId, decimal quantity)
    {
        if (quantity <= 0)
        {
            throw new BusinessRuleException("cart.invalid_quantity", "Quantity must be greater than zero.");
        }

        var line = FindLine(lineId);
        line.Quantity = Math.Round(quantity, 3, MidpointRounding.AwayFromZero);
        NotifyChanged();
    }

    /// <summary>
    /// Adds or subtracts from the last added line. Returns null when the line is removed
    /// because its quantity reached zero.
    /// </summary>
    public SaleLine? AdjustLastQuantity(decimal delta)
    {
        var line = _sale.Lines.LastOrDefault();
        if (line is null)
        {
            return null;
        }

        var next = Math.Round(line.Quantity + delta, 3, MidpointRounding.AwayFromZero);
        if (next <= 0)
        {
            _sale.Lines.Remove(line);
            NotifyChanged();
            return null;
        }

        line.Quantity = next;
        NotifyChanged();
        return line;
    }

    public void IncrementQuantity(Guid lineId, decimal delta)
    {
        var line = FindLine(lineId);
        SetQuantity(lineId, line.Quantity + delta);
    }

    public void RemoveLine(Guid lineId)
    {
        var line = FindLine(lineId);
        _sale.Lines.Remove(line);
        NotifyChanged();
    }

    public void SetLineDiscount(Guid lineId, Discount discount)
    {
        var line = FindLine(lineId);
        line.Discount = discount;
        NotifyChanged();
    }

    public void SetSaleDiscount(Discount discount)
    {
        _sale.SaleDiscount = discount;
        NotifyChanged();
    }

    public void SetCustomer(Customer? customer)
    {
        _sale.CustomerId = customer?.Id;
        _sale.CustomerName = customer?.Name;
        NotifyChanged();
    }

    public void SetNote(string? note)
    {
        _sale.Note = note;
    }

    public void Clear()
    {
        _sale = new Sale { Status = SaleStatus.Held, CreatedAt = DateTime.UtcNow };
        NotifyChanged();
    }

    public Sale Recalculate()
    {
        SaleCalculator.Apply(_sale);
        Totals = SaleCalculator.Calculate(_sale.Lines, _sale.SaleDiscount);
        return _sale;
    }

    private SaleLine FindLine(Guid lineId) =>
        _sale.Lines.FirstOrDefault(l => l.Id == lineId)
        ?? throw new BusinessRuleException("cart.line_missing", "The cart item no longer exists.");

    private void NotifyChanged()
    {
        Recalculate();
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
