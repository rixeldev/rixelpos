using RixelPOS.Domain.Entities;
using RixelPOS.Domain.Enums;

namespace RixelPOS.Desktop.ViewModels;

public sealed class ProductTileItem
{
    public ProductTileItem(Product product, string priceText, string stockText, string? category)
    {
        Product = product;
        Name = product.Name;
        PriceText = priceText;
        StockText = stockText;
        Category = category ?? "";
        Barcode = product.PrimaryBarcode ?? product.Sku;
        IsLowStock = product.IsLowStock;
        IsOutOfStock = product.IsOutOfStock;
        IsFavorite = product.IsFavorite;
        SpokenCode = product.PrimaryBarcode ?? product.Sku;
    }

    public Product Product { get; }
    public string Name { get; }
    public string PriceText { get; }
    public string StockText { get; }
    public string Category { get; }
    public string Barcode { get; }
    public string SpokenCode { get; }
    public bool IsLowStock { get; }
    public bool IsOutOfStock { get; }
    public bool IsFavorite { get; }
}

public sealed class CartLineItem
{
    public CartLineItem(SaleLine line, string unitPriceText, string totalText, string discountText)
    {
        Line = line;
        Id = line.Id;
        Name = line.Name;
        Sku = line.Sku;
        Quantity = line.Quantity;
        QuantityText = line.Quantity == Math.Truncate(line.Quantity)
            ? line.Quantity.ToString("0")
            : line.Quantity.ToString("0.###");
        UnitPriceText = unitPriceText;
        TotalText = totalText;
        DiscountText = discountText;
        HasDiscount = line.DiscountCents > 0;
    }

    public SaleLine Line { get; }
    public Guid Id { get; }
    public string Name { get; }
    public string Sku { get; }
    public decimal Quantity { get; }
    public string QuantityText { get; }
    public string UnitPriceText { get; }
    public string TotalText { get; }
    public string DiscountText { get; }
    public bool HasDiscount { get; }
}

public sealed class SaleHistoryItem
{
    public SaleHistoryItem(Sale sale, string totalText, string dateText, string statusText)
    {
        Sale = sale;
        InvoiceNumber = sale.Invoice?.Number ?? (sale.Number > 0 ? $"#{sale.Number}" : "—");
        TotalText = totalText;
        DateText = dateText;
        CustomerName = string.IsNullOrWhiteSpace(sale.CustomerName) ? "Consumidor final" : sale.CustomerName;
        CashierName = sale.CashierName;
        StatusText = statusText;
        IsVoided = sale.Status == SaleStatus.Voided;
    }

    public Sale Sale { get; }
    public string InvoiceNumber { get; }
    public string TotalText { get; }
    public string DateText { get; }
    public string CustomerName { get; }
    public string CashierName { get; }
    public string StatusText { get; }
    public bool IsVoided { get; }
}

public sealed class TopProductItem
{
    public TopProductItem(string name, decimal quantity, string totalText)
    {
        Name = name;
        Quantity = quantity;
        QuantityText = $"{quantity:0.###}";
        TotalText = totalText;
    }

    public string Name { get; }
    public decimal Quantity { get; }
    public string QuantityText { get; }
    public string TotalText { get; }
}

public sealed class LowStockItem
{
    public LowStockItem(Product product, string stockText, string minText)
    {
        Product = product;
        Name = product.Name;
        StockText = stockText;
        MinText = minText;
        IsOut = product.IsOutOfStock;
    }

    public Product Product { get; }
    public string Name { get; }
    public string StockText { get; }
    public string MinText { get; }
    public bool IsOut { get; }
}

public sealed class CustomerListItem
{
    public CustomerListItem(Customer customer, string balanceText, string creditText, string limitText)
    {
        Customer = customer;
        Name = customer.Name;
        Phone = customer.Phone ?? "";
        TaxId = customer.TaxId ?? "";
        BalanceText = balanceText;
        StoreCreditText = creditText;
        CreditLimitText = limitText;
        HasDebt = customer.BalanceCents > 0;
    }

    public Customer Customer { get; }
    public string Name { get; }
    public string Phone { get; }
    public string TaxId { get; }
    public string BalanceText { get; }
    public string StoreCreditText { get; }
    public string CreditLimitText { get; }
    public bool HasDebt { get; }
}

public sealed class HeldSaleItem
{
    public HeldSaleItem(Sale sale, string totalText, string dateText)
    {
        Sale = sale;
        Label = string.IsNullOrWhiteSpace(sale.HeldLabel) ? $"#{sale.Number}" : sale.HeldLabel;
        TotalText = totalText;
        DateText = dateText;
        CustomerName = string.IsNullOrWhiteSpace(sale.CustomerName) ? "Consumidor final" : sale.CustomerName;
        ItemCount = sale.Lines.Sum(l => l.Quantity).ToString("0.###");
    }

    public Sale Sale { get; }
    public string Label { get; }
    public string TotalText { get; }
    public string DateText { get; }
    public string CustomerName { get; }
    public string ItemCount { get; }
}

public sealed class MovementItem
{
    public MovementItem(CashMovement movement, string amountText)
    {
        Movement = movement;
        Type = movement.Type == CashMovementType.In ? "Entrada" : "Salida";
        AmountText = amountText;
        Reason = movement.Reason;
        TimeText = movement.CreatedAt.ToLocalTime().ToString("dd/MM HH:mm");
        UserName = movement.UserName;
        IsIn = movement.Type == CashMovementType.In;
    }

    public CashMovement Movement { get; }
    public string Type { get; }
    public string AmountText { get; }
    public string Reason { get; }
    public string TimeText { get; }
    public string UserName { get; }
    public bool IsIn { get; }
}

public sealed class SessionHistoryItem
{
    public SessionHistoryItem(CashSession session, string openingText, string closingText, string differenceText)
    {
        Session = session;
        DateText = session.OpenedAt.ToLocalTime().ToString("dd/MM/yyyy HH:mm");
        ClosingDateText = session.ClosedAt?.ToLocalTime().ToString("dd/MM HH:mm") ?? "—";
        OpenedBy = session.OpenedByName;
        ClosingText = closingText;
        DifferenceText = differenceText;
        IsOpen = session.IsOpen;
        HasDifference = session.DifferenceCents is not null and not 0;
    }

    public CashSession Session { get; }
    public string DateText { get; }
    public string ClosingDateText { get; }
    public string OpenedBy { get; }
    public string ClosingText { get; }
    public string DifferenceText { get; }
    public bool IsOpen { get; }
    public bool HasDifference { get; }
}
