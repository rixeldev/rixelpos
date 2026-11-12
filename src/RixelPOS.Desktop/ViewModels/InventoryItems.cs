using CommunityToolkit.Mvvm.ComponentModel;
using RixelPOS.Desktop.Localization;
using RixelPOS.Domain.Entities;
using RixelPOS.Domain.Enums;

namespace RixelPOS.Desktop.ViewModels;

public sealed class ProductListItem
{
    public ProductListItem(Product product, string priceText, string costText, string stockText, string minText)
    {
        Product = product;
        Name = product.Name;
        Sku = product.Sku;
        Barcode = product.PrimaryBarcode ?? "";
        Category = product.CategoryName ?? "";
        PriceText = priceText;
        CostText = costText;
        StockText = stockText;
        MinText = minText;
        IsLowStock = product.IsLowStock;
        IsOutOfStock = product.IsOutOfStock;
        IsFavorite = product.IsFavorite;
        Status = product.IsOutOfStock
            ? LocalizationService.Instance.Get("inventory.statusOut")
            : product.IsLowStock
                ? LocalizationService.Instance.Get("inventory.statusLow")
                : LocalizationService.Instance.Get("inventory.statusOk");
    }

    public Product Product { get; }
    public string Name { get; }
    public string Sku { get; }
    public string Barcode { get; }
    public string Category { get; }
    public string PriceText { get; }
    public string CostText { get; }
    public string StockText { get; }
    public string MinText { get; }
    public bool IsLowStock { get; }
    public bool IsOutOfStock { get; }
    public bool IsFavorite { get; }
    public string Status { get; }
}

public sealed partial class ReceivingLineItem : ObservableObject
{
    public ReceivingLineItem(Product? product, Product? newProduct, string code, string name, string stockText)
    {
        Product = product;
        NewProduct = newProduct;
        Code = code;
        Name = name;
        StockText = stockText;
        IsNew = newProduct is not null;
    }

    public Product? Product { get; }
    public Product? NewProduct { get; }
    public string Code { get; }
    public string Name { get; }
    public string StockText { get; }
    public bool IsNew { get; }
    public string KindText => IsNew ? "Nuevo" : "Existente";

    [ObservableProperty] private decimal _quantity = 1;

    partial void OnQuantityChanged(decimal value) => OnPropertyChanged(nameof(QuantityText));

    public string QuantityText => Quantity.ToString("0.###");
}

public sealed class StockCountLineItem
{
    public StockCountLineItem(StockCountLine line)
    {
        Line = line;
        Name = line.ProductName;
        Sku = line.Sku;
        SystemText = line.SystemQuantity.ToString("0.###");
        CountedText = line.CountedQuantity.ToString("0.###");
        DifferenceText = line.Difference > 0 ? "+" + line.Difference.ToString("0.###") : line.Difference.ToString("0.###");
        IsPositive = line.Difference > 0;
        IsNegative = line.Difference < 0;
        HasDifference = line.Difference != 0;
    }

    public StockCountLine Line { get; }
    public string Name { get; }
    public string Sku { get; }
    public string SystemText { get; }
    public string CountedText { get; }
    public string DifferenceText { get; }
    public bool IsPositive { get; }
    public bool IsNegative { get; }
    public bool HasDifference { get; }
}

public sealed class SupplierListItem
{
    public SupplierListItem(Supplier supplier)
    {
        Supplier = supplier;
        Name = supplier.Name;
        ContactName = supplier.ContactName ?? "";
        Phone = supplier.Phone ?? "";
        Email = supplier.Email ?? "";
        TaxId = supplier.TaxId ?? "";
    }

    public Supplier Supplier { get; }
    public string Name { get; }
    public string ContactName { get; }
    public string Phone { get; }
    public string Email { get; }
    public string TaxId { get; }
}

public sealed class PurchaseOrderItem
{
    public PurchaseOrderItem(PurchaseOrder order, string totalText, string dateText, string statusText, bool canReceive)
    {
        Order = order;
        Number = order.Number;
        SupplierName = order.SupplierName;
        TotalText = totalText;
        DateText = dateText;
        StatusText = statusText;
        CanReceive = canReceive;
        ReceivedText = order.ReceivedAt?.ToLocalTime().ToString("dd/MM/yyyy HH:mm") ?? "—";
    }

    public PurchaseOrder Order { get; }
    public string Number { get; }
    public string SupplierName { get; }
    public string TotalText { get; }
    public string DateText { get; }
    public string StatusText { get; }
    public bool CanReceive { get; }
    public string ReceivedText { get; }
}

public sealed class PurchaseLineItem : ObservableObject
{
    private readonly Action _changed;
    private readonly Func<long, string> _formatMoney;
    private decimal _quantity;
    private string _costText;

    public PurchaseLineItem(Product product, long costCents, decimal quantity, Action changed,
        Func<long, string> formatMoney)
    {
        Product = product;
        Name = product.Name;
        Sku = product.Sku;
        Quantity = quantity;
        _costText = (costCents / 100m).ToString("0.00");
        _changed = changed;
        _formatMoney = formatMoney;
    }

    public Product Product { get; }
    public string Name { get; }
    public string Sku { get; }

    public decimal Quantity
    {
        get => _quantity;
        set
        {
            var clamped = Math.Max(0, value);
            if (SetProperty(ref _quantity, clamped))
            {
                OnPropertyChanged(nameof(TotalText));
                _changed();
            }
        }
    }

    public string CostText
    {
        get => _costText;
        set
        {
            if (SetProperty(ref _costText, value))
            {
                OnPropertyChanged(nameof(TotalText));
                _changed();
            }
        }
    }

    public long CostCents => long.TryParse(CostText.Replace(",", "."), System.Globalization.NumberStyles.Number,
        System.Globalization.CultureInfo.InvariantCulture, out var value)
        ? (long)Math.Round(value * 100m, MidpointRounding.AwayFromZero)
        : 0;

    public long TotalCents => (long)Math.Round(Quantity * CostCents, MidpointRounding.AwayFromZero);

    public string TotalText => _formatMoney(TotalCents);
}
