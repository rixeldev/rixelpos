using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RixelPOS.Application.Customers;
using RixelPOS.Application.Inventory;
using RixelPOS.Application.Printing;
using RixelPOS.Application.Services;
using RixelPOS.Application.Suppliers;
using RixelPOS.Desktop.Localization;
using RixelPOS.Desktop.Services;
using RixelPOS.Domain.Common;
using RixelPOS.Domain.Entities;
using RixelPOS.Domain.Enums;

namespace RixelPOS.Desktop.ViewModels;

public sealed partial class CustomerEditorViewModel : DialogViewModelBase
{
    private readonly CustomerService _service;
    private readonly Customer? _existing;
    private readonly bool _isNew;

    [ObservableProperty] private string _title = "";
    [ObservableProperty] private string _name = "";
    [ObservableProperty] private string _phone = "";
    [ObservableProperty] private string _taxId = "";
    [ObservableProperty] private string _address = "";
    [ObservableProperty] private string _email = "";
    [ObservableProperty] private string _creditLimitText = "";
    [ObservableProperty] private string _notes = "";
    [ObservableProperty] private bool _isActive = true;

    public CustomerEditorViewModel(CustomerService service, LocalizationService loc, Customer? customer = null)
    {
        _service = service;
        _existing = customer;
        _isNew = customer is null;
        Title = loc.Get(customer is null ? "customers.newCustomer" : "customers.editCustomer");
        if (customer is not null)
        {
            Name = customer.Name;
            Phone = customer.Phone ?? "";
            TaxId = customer.TaxId ?? "";
            Address = customer.Address ?? "";
            Email = customer.Email ?? "";
            CreditLimitText = (customer.CreditLimitCents / 100m).ToString("0.00", CultureInfo.InvariantCulture);
            Notes = customer.Notes ?? "";
            IsActive = customer.IsActive;
        }
    }

    [RelayCommand]
    private void Save()
    {
        ErrorMessage = null;
        if (string.IsNullOrWhiteSpace(Name))
        {
            ErrorMessage = LocalizationService.Instance.Get("dialogs.required");
            return;
        }

        var credit = 0m;
        if (!string.IsNullOrWhiteSpace(CreditLimitText) &&
            (!decimal.TryParse(CreditLimitText.Replace(',', '.'), NumberStyles.Number, CultureInfo.InvariantCulture, out credit) || credit < 0))
        {
            ErrorMessage = LocalizationService.Instance.Get("dialogs.invalidAmount");
            return;
        }

        try
        {
            var customer = _existing ?? new Customer();
            customer.Name = Name.Trim();
            customer.Phone = string.IsNullOrWhiteSpace(Phone) ? null : Phone.Trim();
            customer.TaxId = string.IsNullOrWhiteSpace(TaxId) ? null : TaxId.Trim();
            customer.Address = string.IsNullOrWhiteSpace(Address) ? null : Address.Trim();
            customer.Email = string.IsNullOrWhiteSpace(Email) ? null : Email.Trim();
            customer.CreditLimitCents = Money.ToCents(credit);
            customer.Notes = string.IsNullOrWhiteSpace(Notes) ? null : Notes.Trim();
            customer.IsActive = IsActive;
            _service.Save(customer, _isNew);
            Close(true);
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    [RelayCommand]
    private void Cancel() => Close(false);
}

public sealed partial class SupplierEditorViewModel : DialogViewModelBase
{
    private readonly SupplierService _service;
    private readonly Supplier? _existing;
    private readonly bool _isNew;

    [ObservableProperty] private string _title = "";
    [ObservableProperty] private string _name = "";
    [ObservableProperty] private string _contactName = "";
    [ObservableProperty] private string _phone = "";
    [ObservableProperty] private string _email = "";
    [ObservableProperty] private string _address = "";
    [ObservableProperty] private string _taxId = "";
    [ObservableProperty] private string _notes = "";
    [ObservableProperty] private bool _isActive = true;

    public SupplierEditorViewModel(SupplierService service, LocalizationService loc, Supplier? supplier = null)
    {
        _service = service;
        _existing = supplier;
        _isNew = supplier is null;
        Title = loc.Get(supplier is null ? "suppliers.newSupplier" : "suppliers.editSupplier");
        if (supplier is not null)
        {
            Name = supplier.Name;
            ContactName = supplier.ContactName ?? "";
            Phone = supplier.Phone ?? "";
            Email = supplier.Email ?? "";
            Address = supplier.Address ?? "";
            TaxId = supplier.TaxId ?? "";
            Notes = supplier.Notes ?? "";
            IsActive = supplier.IsActive;
        }
    }

    [RelayCommand]
    private void Save()
    {
        ErrorMessage = null;
        if (string.IsNullOrWhiteSpace(Name))
        {
            ErrorMessage = LocalizationService.Instance.Get("dialogs.required");
            return;
        }

        try
        {
            var supplier = _existing ?? new Supplier();
            supplier.Name = Name.Trim();
            supplier.ContactName = string.IsNullOrWhiteSpace(ContactName) ? null : ContactName.Trim();
            supplier.Phone = string.IsNullOrWhiteSpace(Phone) ? null : Phone.Trim();
            supplier.Email = string.IsNullOrWhiteSpace(Email) ? null : Email.Trim();
            supplier.Address = string.IsNullOrWhiteSpace(Address) ? null : Address.Trim();
            supplier.TaxId = string.IsNullOrWhiteSpace(TaxId) ? null : TaxId.Trim();
            supplier.Notes = string.IsNullOrWhiteSpace(Notes) ? null : Notes.Trim();
            supplier.IsActive = IsActive;
            _service.Save(supplier, _isNew);
            Close(true);
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    [RelayCommand]
    private void Cancel() => Close(false);
}

public sealed partial class UserEditorViewModel : DialogViewModelBase
{
    private readonly AuthService _auth;
    private readonly User? _existing;

    [ObservableProperty] private string _title = "";
    [ObservableProperty] private string _fullName = "";
    [ObservableProperty] private string _username = "";
    [ObservableProperty] private int _roleIndex;
    [ObservableProperty] private string _pin = "";
    [ObservableProperty] private string _password = "";
    [ObservableProperty] private bool _isActive = true;
    [ObservableProperty] private bool _isNew = true;

    public UserEditorViewModel(AuthService auth, LocalizationService loc, User? user = null)
    {
        _auth = auth;
        _existing = user;
        IsNew = user is null;
        Title = loc.Get(user is null ? "users.newUser" : "users.editUser");
        if (user is not null)
        {
            FullName = user.FullName;
            Username = user.Username;
            RoleIndex = (int)user.Role;
            IsActive = user.IsActive;
        }
    }

    public string[] Roles => new[]
    {
        LocalizationService.Instance.Get("users.cashier"),
        LocalizationService.Instance.Get("users.manager"),
        LocalizationService.Instance.Get("users.admin")
    };

    [RelayCommand]
    private void Save()
    {
        ErrorMessage = null;
        try
        {
            var role = (UserRole)Math.Clamp(RoleIndex, 0, 2);
            if (IsNew)
            {
                _auth.CreateUser(Username, FullName, role,
                    string.IsNullOrWhiteSpace(Pin) ? null : Pin,
                    string.IsNullOrWhiteSpace(Password) ? null : Password);
            }
            else if (_existing is not null)
            {
                _existing.FullName = FullName.Trim();
                _existing.Username = Username.Trim().ToLowerInvariant();
                _existing.Role = role;
                _auth.UpdateUser(_existing,
                    string.IsNullOrWhiteSpace(Pin) ? null : Pin,
                    string.IsNullOrWhiteSpace(Password) ? null : Password);
                if (_existing.IsActive != IsActive)
                {
                    _auth.SetActive(_existing.Id, IsActive);
                }
            }

            Close(true);
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    [RelayCommand]
    private void Cancel() => Close(false);
}

public sealed partial class ProductEditorViewModel : DialogViewModelBase
{
    private readonly InventoryService _inventory;
    private readonly SettingsService _settings;
    private readonly DialogService _dialogs;
    private readonly LocalizationService _loc;
    private readonly Product? _existing;
    private readonly bool _deferSave;
    private readonly string? _initialBarcode;

    [ObservableProperty] private string _title = "";
    [ObservableProperty] private bool _isNew = true;
    [ObservableProperty] private bool _isDeferred;
    [ObservableProperty] private string _name = "";
    [ObservableProperty] private string _sku = "";
    [ObservableProperty] private string _barcode = "";
    [ObservableProperty] private string _newBarcode = "";
    [ObservableProperty] private string _priceText = "";
    [ObservableProperty] private string _costText = "";
    [ObservableProperty] private string _taxPercentText = "";
    [ObservableProperty] private bool _taxInclusive = true;
    [ObservableProperty] private string _stockText = "";
    [ObservableProperty] private string _minStockText = "";
    [ObservableProperty] private string _unit = "";
    [ObservableProperty] private bool _isFavorite;
    [ObservableProperty] private bool _trackStock = true;
    [ObservableProperty] private string _notes = "";
    [ObservableProperty] private Category? _selectedCategory;
    [ObservableProperty] private string _newCategoryName = "";
    [ObservableProperty] private string _statusMessage = "";

    public ProductEditorViewModel(
        InventoryService inventory,
        SettingsService settings,
        DialogService dialogs,
        LocalizationService loc,
        Product? product = null,
        string? barcode = null,
        bool deferSave = false)
    {
        _inventory = inventory;
        _settings = settings;
        _dialogs = dialogs;
        _loc = loc;
        _existing = product;
        _deferSave = deferSave;
        _initialBarcode = barcode;
        IsNew = product is null;
        IsDeferred = deferSave;
        Title = loc.Get(product is null ? "inventory.newProduct" : "inventory.editProduct");

        Categories.Clear();
        foreach (var category in inventory.GetCategories())
        {
            Categories.Add(category);
        }

        if (product is not null)
        {
            Name = product.Name;
            Sku = product.Sku;
            PriceText = (product.PriceCents / 100m).ToString("0.00", CultureInfo.InvariantCulture);
            CostText = (product.CostCents / 100m).ToString("0.00", CultureInfo.InvariantCulture);
            TaxPercentText = (product.TaxRateBasisPoints / 100m).ToString("0.##", CultureInfo.InvariantCulture);
            TaxInclusive = product.TaxInclusive;
            StockText = product.Stock.ToString("0.###", CultureInfo.InvariantCulture);
            MinStockText = product.MinStock.ToString("0.###", CultureInfo.InvariantCulture);
            Unit = product.Unit;
            IsFavorite = product.IsFavorite;
            TrackStock = product.TrackStock;
            Notes = product.Notes ?? "";
            SelectedCategory = Categories.FirstOrDefault(c => c.Id == product.CategoryId);
            foreach (var code in product.Barcodes)
            {
                Barcodes.Add(new BarcodeEntry(code.Code, code.IsPrimary));
            }
        }
        else
        {
            TaxPercentText = "";
            TaxInclusive = true;
            if (!string.IsNullOrWhiteSpace(barcode))
            {
                Barcodes.Add(new BarcodeEntry(barcode, true));
            }
        }
    }

    public ObservableCollection<Category> Categories { get; } = new();
    public ObservableCollection<BarcodeEntry> Barcodes { get; } = new();

    public Product? SavedProduct { get; private set; }

    [RelayCommand]
    private void AddBarcode()
    {
        var code = NewBarcode.Trim();
        if (code.Length == 0 || Barcodes.Any(b => b.Code == code))
        {
            return;
        }

        Barcodes.Add(new BarcodeEntry(code, Barcodes.Count == 0));
        NewBarcode = "";
    }

    [RelayCommand]
    private void AddBarcodeFromField()
    {
        Barcode = Barcode.Trim();
        if (Barcode.Length == 0)
        {
            return;
        }

        if (!Barcodes.Any(b => b.Code == Barcode))
        {
            Barcodes.Add(new BarcodeEntry(Barcode, Barcodes.Count == 0));
        }

        Barcode = "";
    }

    [RelayCommand]
    private void RemoveBarcode(BarcodeEntry? entry)
    {
        if (entry is not null)
        {
            Barcodes.Remove(entry);
        }
    }

    [RelayCommand]
    private async Task AddCategoryAsync()
    {
        if (string.IsNullOrWhiteSpace(NewCategoryName))
        {
            return;
        }

        try
        {
            _inventory.CreateCategory(NewCategoryName.Trim());
            Categories.Clear();
            foreach (var category in _inventory.GetCategories())
            {
                Categories.Add(category);
            }

            SelectedCategory = Categories.FirstOrDefault(c => c.Name.Equals(NewCategoryName.Trim(), StringComparison.OrdinalIgnoreCase));
            NewCategoryName = "";
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    [RelayCommand]
    private void GenerateBarcode()
    {
        try
        {
            if (_existing is not null)
            {
                var code = _inventory.CreateInternalBarcode(_existing.Id);
                if (!Barcodes.Any(b => b.Code == code))
                {
                    Barcodes.Add(new BarcodeEntry(code, Barcodes.Count == 0));
                }
            }
            else
            {
                var code = Application.Inventory.BarcodeGenerator.BuildEan13("200", DateTime.UtcNow.Ticks % 1_000_000_000);
                Barcodes.Add(new BarcodeEntry(code, Barcodes.Count == 0));
                StatusMessage = _loc.Get("inventory.barcodeGenerated");
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    [RelayCommand]
    private void Save()
    {
        ErrorMessage = null;
        if (string.IsNullOrWhiteSpace(Name))
        {
            ErrorMessage = _loc.Get("inventory.nameRequired");
            return;
        }

        if (!TryMoney(PriceText, out var price) || !TryMoney(CostText, out var cost))
        {
            ErrorMessage = _loc.Get("inventory.priceRequired");
            return;
        }

        if (!TryDecimal(TaxPercentText, out var taxPercent) || taxPercent is < 0 or > 100)
        {
            ErrorMessage = _loc.Get("dialogs.taxRange");
            return;
        }

        if (!TryDecimal(StockText, out var stock) || !TryDecimal(MinStockText, out var minStock))
        {
            ErrorMessage = _loc.Get("inventory.checkStock");
            return;
        }

        if (stock < 0 || minStock < 0)
        {
            ErrorMessage = _loc.Get("inventory.stockNegative");
            return;
        }

        try
        {
            var product = _existing ?? new Product();
            product.Name = Name.Trim();
            product.Sku = string.IsNullOrWhiteSpace(Sku)
                ? _inventory.GenerateSku(product.Name, Barcodes.FirstOrDefault()?.Code, _existing?.Id)
                : Sku.Trim();
            product.PriceCents = price;
            product.CostCents = cost;
            product.TaxRateBasisPoints = (int)Math.Round(taxPercent * 100, MidpointRounding.AwayFromZero);
            product.TaxInclusive = true;
            product.Unit = string.IsNullOrWhiteSpace(Unit) ? "unit" : Unit.Trim();
            product.MinStock = minStock;
            product.IsFavorite = IsFavorite;
            product.TrackStock = TrackStock;
            product.Notes = string.IsNullOrWhiteSpace(Notes) ? null : Notes.Trim();
            product.CategoryId = SelectedCategory?.Id;
            product.Barcodes.Clear();
            foreach (var barcode in Barcodes)
            {
                product.Barcodes.Add(new ProductBarcode
                {
                    ProductId = product.Id,
                    Code = barcode.Code,
                    IsPrimary = barcode.IsPrimary
                });
            }

            if (_deferSave)
            {
                product.Stock = 0;
                SavedProduct = product;
                Close(true);
                return;
            }

            product.Stock = stock;
            _inventory.SaveProduct(product, IsNew);
            SavedProduct = _inventory.GetProduct(product.Id);
            Close(true);
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    [RelayCommand]
    private void Cancel() => Close(false);

    private static bool TryMoney(string text, out long cents)
    {
        cents = 0;
        if (string.IsNullOrWhiteSpace(text))
        {
            return true;
        }

        if (!decimal.TryParse(text.Replace(',', '.'), NumberStyles.Number, CultureInfo.InvariantCulture, out var value) || value < 0)
        {
            return false;
        }

        cents = Money.ToCents(value);
        return true;
    }

    private static bool TryDecimal(string text, out decimal value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(text))
        {
            return true;
        }

        return decimal.TryParse(text.Replace(',', '.'), NumberStyles.Number, CultureInfo.InvariantCulture, out value);
    }
}

public sealed partial class BarcodeEntry : ObservableObject
{
    public BarcodeEntry(string code, bool isPrimary)
    {
        Code = code;
        IsPrimary = isPrimary;
    }

    public string Code { get; }
    public bool IsPrimary { get; }
    public string PrimaryText => IsPrimary ? "Principal" : "";
}

public sealed partial class StockAdjustViewModel : DialogViewModelBase
{
    private readonly InventoryService _inventory;
    private readonly Product _product;

    [ObservableProperty] private string _productName = "";
    [ObservableProperty] private string _currentStockText = "";
    [ObservableProperty] private string _newStockText = "";
    [ObservableProperty] private string _reason = "";
    [ObservableProperty] private string _differenceText = "";

    public StockAdjustViewModel(InventoryService inventory, SettingsService settings, Product product)
    {
        _inventory = inventory;
        _product = product;
        ProductName = product.Name;
        CurrentStockText = settings.FormatQuantity(product.Stock);
        NewStockText = product.Stock.ToString("0.###", CultureInfo.InvariantCulture);
        DifferenceText = "0";
    }

    partial void OnNewStockTextChanged(string value)
    {
        if (decimal.TryParse(value.Replace(',', '.'), NumberStyles.Number, CultureInfo.InvariantCulture, out var newStock))
        {
            var delta = newStock - _product.Stock;
            DifferenceText = (delta > 0 ? "+" : "") + delta.ToString("0.###");
        }
    }

    [RelayCommand]
    private void Save()
    {
        ErrorMessage = null;
        if (!decimal.TryParse(NewStockText.Replace(',', '.'), NumberStyles.Number, CultureInfo.InvariantCulture, out var newStock) || newStock < 0)
        {
            ErrorMessage = LocalizationService.Instance.Get("dialogs.invalidAmount");
            return;
        }

        if (string.IsNullOrWhiteSpace(Reason))
        {
            ErrorMessage = LocalizationService.Instance.Get("inventory.reason");
            return;
        }

        try
        {
            var delta = Math.Round(newStock - _product.Stock, 3);
            if (delta == 0)
            {
                Close(false);
                return;
            }

            _inventory.AdjustStock(_product.Id, delta, Reason.Trim());
            Close(true);
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    [RelayCommand]
    private void Cancel() => Close(false);
}
