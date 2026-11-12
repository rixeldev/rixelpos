using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RixelPOS.Application.Cash;
using RixelPOS.Application.Checkout;
using RixelPOS.Application.Customers;
using RixelPOS.Application.Inventory;
using RixelPOS.Application.Printing;
using RixelPOS.Application.Security;
using RixelPOS.Application.Services;
using RixelPOS.Application.Sales;
using RixelPOS.Application.Sessions;
using RixelPOS.Desktop.Localization;
using RixelPOS.Desktop.Services;
using RixelPOS.Domain.Common;
using RixelPOS.Domain.Entities;
using RixelPOS.Domain.Enums;

namespace RixelPOS.Desktop.ViewModels;

public sealed partial class CheckoutViewModel : ViewModelBase, IActivatable, IScanTarget
{
    private readonly CheckoutService _checkout;
    private readonly InventoryService _inventory;
    private readonly CustomerService _customers;
    private readonly SettingsService _settings;
    private readonly AppSession _session;
    private readonly CashRegisterService _cash;
    private readonly ReceiptService _receipt;
    private readonly DialogService _dialogs;
    private readonly SoundService _sound;
    private readonly LocalizationService _loc;
    private readonly DispatcherTimer _autoAddTimer;
    private Cart _cart = new();
    private Customer? _customer;

    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private bool _isSearchOpen;
    [ObservableProperty] private string _statusMessage = "";
    [ObservableProperty] private bool _statusIsError;
    [ObservableProperty] private string _subtotalText = "";
    [ObservableProperty] private string _discountText = "";
    [ObservableProperty] private string _taxText = "";
    [ObservableProperty] private string _totalText = "";
    [ObservableProperty] private string _changeHint = "";
    [ObservableProperty] private string _itemCountText = "";
    [ObservableProperty] private string _customerName = "";
    [ObservableProperty] private string _cashStatus = "";
    [ObservableProperty] private bool _isCashOpen;
    [ObservableProperty] private bool _hasItems;
    [ObservableProperty] private bool _hasDiscount;
    [ObservableProperty] private bool _hasCustomer;
    [ObservableProperty] private string _taxLabel = "ITBIS";

    public CheckoutViewModel(
        CheckoutService checkout,
        InventoryService inventory,
        CustomerService customers,
        SettingsService settings,
        AppSession session,
        CashRegisterService cash,
        ReceiptService receipt,
        DialogService dialogs,
        SoundService sound,
        LocalizationService loc)
    {
        _checkout = checkout;
        _inventory = inventory;
        _customers = customers;
        _settings = settings;
        _session = session;
        _cash = cash;
        _receipt = receipt;
        _dialogs = dialogs;
        _sound = sound;
        _loc = loc;
        _cart.Changed += OnCartChanged;
        CustomerName = _loc.Get("checkout.walkIn");

        // Keyboard-wedge scanners usually send Enter, but some are configured without a
        // suffix. If the typed text exactly matches a barcode/SKU, add it automatically
        // after the configured idle delay so no button press is required.
        _autoAddTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(Math.Max(150, _settings.Current.ScannerDebounceMs))
        };
        _autoAddTimer.Tick += (_, _) => TryAutoAddScannedCode();

        ErrorMessage = null;
    }

    public event EventHandler? FocusSearchRequested;

    public ObservableCollection<ProductTileItem> SearchResults { get; } = new();
    public ObservableCollection<ProductTileItem> Favorites { get; } = new();
    public ObservableCollection<CartLineItem> CartLines { get; } = new();

    public void Activate()
    {
        RefreshCash();
        LoadFavorites();
        RefreshCart();
        FocusSearchRequested?.Invoke(this, EventArgs.Empty);
    }

    public void RequestFocus() => FocusSearchRequested?.Invoke(this, EventArgs.Empty);

    public bool HandleScan(string code)
    {
        AddByCode(code);
        return true;
    }

    partial void OnSearchTextChanged(string value)
    {
        try
        {
            SearchResults.Clear();
            if (string.IsNullOrWhiteSpace(value))
            {
                IsSearchOpen = false;
                return;
            }

            foreach (var product in _inventory.Search(value).Take(24))
            {
                SearchResults.Add(ToTile(product));
            }

            IsSearchOpen = SearchResults.Count > 0;
            ScheduleAutoAdd(value);
        }
        catch (Exception ex)
        {
            ReportError(ex);
        }
    }

    private void ScheduleAutoAdd(string value)
    {
        _autoAddTimer.Stop();
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        _autoAddTimer.Interval = TimeSpan.FromMilliseconds(Math.Max(150, _settings.Current.ScannerDebounceMs));
        _autoAddTimer.Start();
    }

    private void TryAutoAddScannedCode()
    {
        _autoAddTimer.Stop();
        var code = SearchText.Trim();
        if (code.Length < 2)
        {
            return;
        }

        try
        {
            // Only auto-add an exact code, never a partial name search.
            var product = _inventory.FindByCode(code);
            if (product is not null)
            {
                AddToCart(product);
            }
        }
        catch (Exception ex)
        {
            ReportError(ex);
        }
    }

    [RelayCommand]
    private void CommitSearch()
    {
        var text = SearchText.Trim();
        if (text.Length == 0)
        {
            return;
        }

        AddByCode(text);
    }

    [RelayCommand]
    private void SelectSearchResult(ProductTileItem? item)
    {
        if (item is not null)
        {
            AddToCart(item.Product);
        }
    }

    [RelayCommand]
    private void AddProduct(ProductTileItem? item)
    {
        if (item is not null)
        {
            AddToCart(item.Product);
        }
    }

    [RelayCommand]
    private void Increment(CartLineItem? item)
    {
        if (item is null)
        {
            return;
        }

        _cart.SetQuantity(item.Id, item.Quantity + 1);
    }

    [RelayCommand]
    private void Decrement(CartLineItem? item)
    {
        if (item is null)
        {
            return;
        }

        if (item.Quantity <= 1)
        {
            _cart.RemoveLine(item.Id);
        }
        else
        {
            _cart.SetQuantity(item.Id, item.Quantity - 1);
        }
    }

    [RelayCommand]
    private void RemoveLine(CartLineItem? item)
    {
        if (item is not null)
        {
            _cart.RemoveLine(item.Id);
        }
    }

    [RelayCommand]
    private async Task EditQuantityAsync(CartLineItem? item)
    {
        if (item is null)
        {
            return;
        }

        var quantity = await _dialogs.AskQuantityAsync(item.Quantity);
        if (quantity is > 0)
        {
            _cart.SetQuantity(item.Id, quantity.Value);
        }
    }

    [RelayCommand]
    private async Task ApplyLineDiscountAsync(CartLineItem? item)
    {
        if (item is null)
        {
            return;
        }

        var baseCents = item.Line.NetCents + item.Line.TaxCents;
        var discount = await _dialogs.AskDiscountAsync(baseCents, _loc.Get("checkout.lineDiscount"));
        if (discount is null)
        {
            return;
        }

        var user = _session.CurrentUser;
        if (user is not null && DiscountPolicy.RequiresApproval(_settings.Current, user, discount.Value, baseCents))
        {
            if (!await _dialogs.ApproveAsync(Permission.ApplyDiscount, _loc.Get("checkout.lineDiscount")))
            {
                return;
            }
        }

        _cart.SetLineDiscount(item.Id, discount.Value);
    }

    [RelayCommand]
    private async Task ApplySaleDiscountAsync()
    {
        var baseCents = _cart.Totals.TotalCents;
        if (baseCents <= 0)
        {
            return;
        }

        var discount = await _dialogs.AskDiscountAsync(baseCents, _loc.Get("checkout.discountSale"));
        if (discount is null)
        {
            return;
        }

        var user = _session.CurrentUser;
        if (user is not null && DiscountPolicy.RequiresApproval(_settings.Current, user, discount.Value, baseCents))
        {
            if (!await _dialogs.ApproveAsync(Permission.ApplyDiscount, _loc.Get("checkout.discountSale")))
            {
                return;
            }
        }

        _cart.SetSaleDiscount(discount.Value);
    }

    [RelayCommand]
    private async Task PickCustomerAsync()
    {
        var customer = await _dialogs.PickCustomerAsync();
        if (customer is null)
        {
            return;
        }

        _customer = customer;
        _cart.SetCustomer(customer);
        CustomerName = customer.Name;
        HasCustomer = true;
    }

    [RelayCommand]
    private void ClearCustomer()
    {
        _customer = null;
        _cart.SetCustomer(null);
        CustomerName = _loc.Get("checkout.walkIn");
        HasCustomer = false;
    }

    [RelayCommand]
    private async Task HoldAsync()
    {
        if (!HasItems)
        {
            return;
        }

        try
        {
            var label = await _dialogs.AskTextAsync(_loc.Get("checkout.hold"), _loc.Get("checkout.holdLabel"), "");
            if (label is null)
            {
                return;
            }

            _checkout.HoldSale(_cart, label);
            LoadCart(new Cart());
            StatusMessage = _loc.Get("checkout.saleHeld");
            StatusIsError = false;
            _sound.Play(ScanSound.Held);
            FocusSearchRequested?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            ReportError(ex);
            StatusIsError = true;
        }
    }

    [RelayCommand]
    private async Task ResumeHeldAsync()
    {
        var held = _checkout.GetHeldSales()
            .Select(s => new HeldSaleItem(s, _settings.FormatMoney(s.TotalCents),
                s.CreatedAt.ToLocalTime().ToString("dd/MM HH:mm")))
            .ToList();
        var selected = await _dialogs.PickHeldSaleAsync(held);
        if (selected is null)
        {
            return;
        }

        try
        {
            var cart = _checkout.ResumeSale(selected.Sale.Id);
            LoadCart(cart);
            StatusMessage = _loc.Get("checkout.saleResumed");
            StatusIsError = false;
            FocusSearchRequested?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            ReportError(ex);
            StatusIsError = true;
        }
    }

    [RelayCommand]
    private async Task ClearCartAsync()
    {
        if (!HasItems)
        {
            return;
        }

        if (await _dialogs.ConfirmAsync(_loc.Get("checkout.clear"), _loc.Get("dialogs.areYouSure"), _loc.Get("checkout.clear")))
        {
            LoadCart(new Cart());
            FocusSearchRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    [RelayCommand]
    private async Task ChargeAsync()
    {
        if (!HasItems)
        {
            return;
        }

        RefreshCash();
        if (!IsCashOpen)
        {
            if (!await _dialogs.OpenRegisterAsync())
            {
                return;
            }

            RefreshCash();
        }

        var user = _session.CurrentUser;
        if (user is not null && !_cart.Sale.SaleDiscount.IsNone &&
            DiscountPolicy.RequiresApproval(_settings.Current, user, _cart.Sale.SaleDiscount, _cart.Totals.TotalCents))
        {
            if (!await _dialogs.ApproveAsync(Permission.ApplyDiscount, _loc.Get("checkout.discountSale")))
            {
                return;
            }
        }

        var request = new PaymentRequest(
            _cart.Totals.TotalCents,
            _customer,
            _settings.Current.StoreCreditEnabled,
            _settings.Current.CreditSalesEnabled);

        var result = await _dialogs.TakePaymentAsync(request);
        if (result is null)
        {
            return;
        }

        try
        {
            var sale = _checkout.CompleteSale(_cart, result.Payments, result.Customer, null);
            _sound.Play(ScanSound.Success);
            _session.CashSession = _cash.Current;
            _session.NotifyChanged();

            PrintResult? printResult = null;
            if (_settings.Current.AutoPrintReceipt)
            {
                printResult = _settings.Current.PreviewReceiptBeforePrint
                    ? await _dialogs.ShowReceiptPreviewAsync(sale)
                    : await _receipt.PrintAsync(sale);
            }

            LoadCart(new Cart());
            await _dialogs.ShowSaleCompletedAsync(sale, printResult);
            RefreshCash();
            FocusSearchRequested?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            ReportError(ex);
            StatusIsError = true;
            await _dialogs.ShowMessageAsync(_loc.Get("common.error"), ErrorMessage ?? ex.Message);
        }
    }

    [RelayCommand]
    private async Task OpenRegisterAsync()
    {
        if (await _dialogs.OpenRegisterAsync())
        {
            RefreshCash();
        }
    }

    [RelayCommand]
    private void CloseSearch()
    {
        IsSearchOpen = false;
        SearchText = "";
        FocusSearchRequested?.Invoke(this, EventArgs.Empty);
    }

    private void AddByCode(string code)
    {
        try
        {
            code = code.Trim();
            if (code.Length == 0)
            {
                return;
            }

            var product = _inventory.FindByCode(code);
            if (product is null)
            {
                var results = _inventory.Search(code);
                product = results.Count == 1 ? results[0] : null;
            }

            if (product is null)
            {
                StatusMessage = _loc.Get("checkout.scanNotFound");
                StatusIsError = true;
                _sound.Play(ScanSound.Error);
                return;
            }

            AddToCart(product);
        }
        catch (Exception ex)
        {
            ReportError(ex);
            StatusIsError = true;
        }
    }

    private void AddToCart(Product product)
    {
        try
        {
            _cart.AddProduct(product);
            StatusMessage = $"{product.Name} · {_loc.Get("checkout.addedToCart")}";
            StatusIsError = false;
            SearchText = "";
            SearchResults.Clear();
            IsSearchOpen = false;
            _sound.Play(ScanSound.Found);
            FocusSearchRequested?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            ReportError(ex);
            StatusIsError = true;
            _sound.Play(ScanSound.Error);
        }
    }

    private void LoadCart(Cart cart)
    {
        _cart.Changed -= OnCartChanged;
        _cart = cart;
        _cart.Changed += OnCartChanged;
        _customer = null;
        if (cart.Sale.CustomerId is not null)
        {
            _customer = _customers.Get(cart.Sale.CustomerId.Value);
        }

        HasCustomer = _customer is not null;
        CustomerName = _customer?.Name ?? _loc.Get("checkout.walkIn");
        RefreshCart();
    }

    private void OnCartChanged(object? sender, EventArgs e) => RefreshCart();

    private void RefreshCart()
    {
        CartLines.Clear();
        foreach (var line in _cart.Lines)
        {
            CartLines.Add(new CartLineItem(line,
                _settings.FormatMoney(line.UnitPriceCents),
                _settings.FormatMoney(line.TotalCents),
                line.DiscountCents > 0 ? "-" + _settings.FormatMoney(line.DiscountCents) : ""));
        }

        var totals = _cart.Totals;
        SubtotalText = _settings.FormatMoney(totals.SubtotalCents);
        DiscountText = "-" + _settings.FormatMoney(totals.DiscountCents);
        TaxText = _settings.FormatMoney(totals.TaxCents);
        TotalText = _settings.FormatMoney(totals.TotalCents);
        TaxLabel = _settings.Current.TaxName;
        HasItems = _cart.Lines.Count > 0;
        HasDiscount = totals.DiscountCents > 0;
        ItemCountText = $"{_cart.ItemCount:0.###} {_loc.Get("checkout.items")}";
        if (string.IsNullOrWhiteSpace(CustomerName))
        {
            CustomerName = _loc.Get("checkout.walkIn");
        }

        ChangeHint = _customer is not null ? _customer.Name : _loc.Get("checkout.walkIn");
    }

    private void RefreshCash()
    {
        IsCashOpen = _cash.Current is not null;
        CashStatus = IsCashOpen ? _loc.Get("cash.registerOpen") : _loc.Get("cash.registerClosed");
    }

    private void LoadFavorites()
    {
        Favorites.Clear();
        foreach (var product in _inventory.Search("").Where(p => p.IsFavorite).Take(24))
        {
            Favorites.Add(ToTile(product));
        }

        if (Favorites.Count == 0)
        {
            foreach (var product in _inventory.Search("").Take(18))
            {
                Favorites.Add(ToTile(product));
            }
        }
    }

    private ProductTileItem ToTile(Product product) =>
        new(product, _settings.FormatMoney(product.PriceCents),
            $"{_settings.FormatQuantity(product.Stock)} {product.Unit}", product.CategoryName);
}
