using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RixelPOS.Application.Inventory;
using RixelPOS.Application.Services;
using RixelPOS.Application.Suppliers;
using RixelPOS.Desktop.Localization;
using RixelPOS.Desktop.Services;
using RixelPOS.Domain.Common;
using RixelPOS.Domain.Entities;
using RixelPOS.Domain.Enums;

namespace RixelPOS.Desktop.ViewModels;

public sealed partial class SuppliersViewModel : ViewModelBase, IActivatable
{
    private readonly SupplierService _suppliers;
    private readonly SettingsService _settings;
    private readonly DialogService _dialogs;
    private readonly LocalizationService _loc;

    [ObservableProperty] private int _selectedTabIndex;
    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private SupplierListItem? _selectedSupplier;
    [ObservableProperty] private PurchaseOrderItem? _selectedOrder;
    [ObservableProperty] private string _statusMessage = "";
    [ObservableProperty] private bool _statusIsError;
    [ObservableProperty] private bool _hasSuppliers;
    [ObservableProperty] private bool _hasOrders;

    public SuppliersViewModel(
        SupplierService suppliers,
        SettingsService settings,
        DialogService dialogs,
        LocalizationService loc)
    {
        _suppliers = suppliers;
        _settings = settings;
        _dialogs = dialogs;
        _loc = loc;
    }

    public ObservableCollection<SupplierListItem> Suppliers { get; } = new();
    public ObservableCollection<PurchaseOrderItem> Orders { get; } = new();


    public void Activate() => Refresh();

    partial void OnSearchTextChanged(string value) => LoadSuppliers();

    partial void OnSelectedTabIndexChanged(int value) => Refresh();

    [RelayCommand]
    private void Refresh()
    {
        LoadSuppliers();
        LoadOrders();
    }

    [RelayCommand]
    private async Task NewSupplierAsync()
    {
        if (await _dialogs.EditSupplierAsync(null))
        {
            LoadSuppliers();
            StatusMessage = _loc.Get("suppliers.saved");
            StatusIsError = false;
        }
    }

    [RelayCommand]
    private async Task EditSupplierAsync(SupplierListItem? item)
    {
        item ??= SelectedSupplier;
        if (item is null)
        {
            return;
        }

        if (await _dialogs.EditSupplierAsync(item.Supplier))
        {
            LoadSuppliers();
            StatusMessage = _loc.Get("suppliers.saved");
            StatusIsError = false;
        }
    }

    [RelayCommand]
    private async Task NewOrderAsync()
    {
        if (await _dialogs.CreatePurchaseOrderAsync())
        {
            Refresh();
            StatusMessage = _loc.Get("suppliers.orderCreated");
            StatusIsError = false;
        }
    }

    [RelayCommand]
    private async Task ReceiveOrderAsync(PurchaseOrderItem? item)
    {
        item ??= SelectedOrder;
        if (item is null)
        {
            return;
        }

        if (!await _dialogs.ConfirmAsync(_loc.Get("suppliers.receiveOrder"),
                _loc.Get("suppliers.receiveConfirm"), _loc.Get("suppliers.receiveOrder")))
        {
            return;
        }

        try
        {
            _suppliers.ReceivePurchaseOrder(item.Order.Id);
            Refresh();
            StatusMessage = _loc.Get("suppliers.orderReceived");
            StatusIsError = false;
        }
        catch (Exception ex)
        {
            ReportError(ex);
            StatusIsError = true;
        }
    }

    private void LoadSuppliers()
    {
        try
        {
            Suppliers.Clear();
            foreach (var supplier in _suppliers.Search(string.IsNullOrWhiteSpace(SearchText) ? null : SearchText))
            {
                Suppliers.Add(new SupplierListItem(supplier));
            }

            HasSuppliers = Suppliers.Count > 0;
        }
        catch (Exception ex)
        {
            ReportError(ex);
        }
    }

    private void LoadOrders()
    {
        try
        {
            Orders.Clear();
            foreach (var order in _suppliers.GetPurchaseOrders())
            {
                Orders.Add(new PurchaseOrderItem(order,
                    _settings.FormatMoney(order.TotalCents),
                    order.OrderedAt.ToLocalTime().ToString("dd/MM/yyyy HH:mm"),
                    StatusText(order.Status),
                    order.Status is PurchaseOrderStatus.Ordered or PurchaseOrderStatus.Draft));
            }

            HasOrders = Orders.Count > 0;
        }
        catch (Exception ex)
        {
            ReportError(ex);
        }
    }

    private string StatusText(PurchaseOrderStatus status) => status switch
    {
        PurchaseOrderStatus.Draft => _loc.Get("suppliers.draft"),
        PurchaseOrderStatus.Ordered => _loc.Get("suppliers.ordered"),
        PurchaseOrderStatus.Received => _loc.Get("suppliers.received"),
        PurchaseOrderStatus.Cancelled => _loc.Get("suppliers.cancelled"),
        _ => status.ToString()
    };
}

public sealed partial class PurchaseEditorViewModel : DialogViewModelBase
{
    private readonly SupplierService _suppliers;
    private readonly InventoryService _inventory;
    private readonly SettingsService _settings;
    private readonly LocalizationService _loc;

    [ObservableProperty] private Supplier? _selectedSupplier;
    [ObservableProperty] private string _productSearch = "";
    [ObservableProperty] private string _notes = "";
    [ObservableProperty] private string _totalText = "";
    [ObservableProperty] private bool _hasLines;

    public PurchaseEditorViewModel(
        SupplierService suppliers,
        InventoryService inventory,
        SettingsService settings,
        LocalizationService loc)
    {
        _suppliers = suppliers;
        _inventory = inventory;
        _settings = settings;
        _loc = loc;
        LoadSuppliers();
        Recalculate();
    }

    public ObservableCollection<Supplier> Suppliers { get; } = new();
    public ObservableCollection<Product> ProductResults { get; } = new();
    public ObservableCollection<PurchaseLineItem> Lines { get; } = new();

    public bool HasProductSearch => ProductResults.Count > 0;

    partial void OnProductSearchChanged(string value)
    {
        ProductResults.Clear();
        if (string.IsNullOrWhiteSpace(value))
        {
            OnPropertyChanged(nameof(HasProductSearch));
            return;
        }

        foreach (var product in _inventory.Search(value).Take(12))
        {
            ProductResults.Add(product);
        }

        OnPropertyChanged(nameof(HasProductSearch));
    }

    [RelayCommand]
    private void AddProduct(Product? product)
    {
        if (product is null)
        {
            return;
        }

        var existing = Lines.FirstOrDefault(l => l.Product.Id == product.Id);
        if (existing is not null)
        {
            existing.Quantity += 1;
        }
        else
        {
            Lines.Add(new PurchaseLineItem(product, product.CostCents, 1, Recalculate, _settings.FormatMoney));
        }

        ProductSearch = "";
        ProductResults.Clear();
        HasLines = Lines.Count > 0;
        Recalculate();
    }

    [RelayCommand]
    private void RemoveLine(PurchaseLineItem? line)
    {
        if (line is not null)
        {
            Lines.Remove(line);
            HasLines = Lines.Count > 0;
            Recalculate();
        }
    }

    [RelayCommand]
    private void Increment(PurchaseLineItem? line)
    {
        if (line is not null)
        {
            line.Quantity += 1;
        }
    }

    [RelayCommand]
    private void Decrement(PurchaseLineItem? line)
    {
        if (line is null)
        {
            return;
        }

        if (line.Quantity <= 1)
        {
            Lines.Remove(line);
            HasLines = Lines.Count > 0;
        }
        else
        {
            line.Quantity -= 1;
        }
    }

    [RelayCommand]
    private void Confirm()
    {
        ErrorMessage = null;
        if (SelectedSupplier is null)
        {
            ErrorMessage = _loc.Get("suppliers.selectSupplier");
            return;
        }

        if (Lines.Count == 0)
        {
            ErrorMessage = _loc.Get("suppliers.addLine");
            return;
        }

        try
        {
            var lines = Lines.Select(l => new PurchaseOrderLine
            {
                ProductId = l.Product.Id,
                ProductName = l.Product.Name,
                Sku = l.Product.Sku,
                Quantity = l.Quantity,
                UnitCostCents = l.CostCents
            }).ToList();

            _suppliers.CreatePurchaseOrder(SelectedSupplier.Id, SelectedSupplier.Name, lines, Notes);
            Close(true);
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    [RelayCommand]
    private void Cancel() => Close(false);

    private void LoadSuppliers()
    {
        foreach (var supplier in _suppliers.Search())
        {
            Suppliers.Add(supplier);
        }

        SelectedSupplier = Suppliers.FirstOrDefault();
    }

    private void Recalculate()
    {
        var total = Lines.Sum(l => l.TotalCents);
        TotalText = _settings.FormatMoney(total);
    }
}
