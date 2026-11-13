using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using RixelPOS.Application.Abstractions;
using RixelPOS.Application.Cash;
using RixelPOS.Application.Checkout;
using RixelPOS.Application.Customers;
using RixelPOS.Application.Inventory;
using RixelPOS.Application.Printing;
using RixelPOS.Application.Services;
using RixelPOS.Application.Sessions;
using RixelPOS.Application.Suppliers;
using RixelPOS.Desktop.Localization;
using RixelPOS.Desktop.ViewModels;
using RixelPOS.Desktop.Views.Dialogs;
using RixelPOS.Domain.Common;
using RixelPOS.Domain.Entities;
using RixelPOS.Domain.Enums;
using RixelPOS.Domain.Reporting;

namespace RixelPOS.Desktop.Services;

public sealed class DialogService
{
    private readonly AuthService _auth;
    private readonly CustomerService _customers;
    private readonly InventoryService _inventory;
    private readonly SupplierService _suppliers;
    private readonly ReceiptService _receipt;
    private readonly CashRegisterService _cash;
    private readonly SettingsService _settings;
    private readonly AppSession _session;
    private readonly LocalizationService _loc;
    private SaleCompletedWindow? _saleCompleted;
    private ReceiptPreviewWindow? _preview;

    public DialogService(
        AuthService auth,
        CustomerService customers,
        InventoryService inventory,
        SupplierService suppliers,
        ReceiptService receipt,
        CashRegisterService cash,
        SettingsService settings,
        AppSession session,
        LocalizationService loc)
    {
        _auth = auth;
        _customers = customers;
        _inventory = inventory;
        _suppliers = suppliers;
        _receipt = receipt;
        _cash = cash;
        _settings = settings;
        _session = session;
        _loc = loc;
    }

    private static Window? Owner =>
        (Avalonia.Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;

    private async Task<bool> ShowAsync(DialogWindow window, DialogViewModelBase viewModel)
    {
        window.DataContext = viewModel;
        if (Owner is null)
        {
            return false;
        }

        window.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        return await window.ShowDialog<bool>(Owner);
    }

    public async Task<bool> ConfirmAsync(string title, string message, string? okText = null, bool danger = false)
    {
        var vm = new MessageDialogViewModel(title, message, true, okText ?? _loc.Get("common.confirm"),
            _loc.Get("common.cancel"), danger);
        return await ShowAsync(new MessageDialogWindow(), vm);
    }

    public async Task ShowMessageAsync(string title, string message)
    {
        var vm = new MessageDialogViewModel(title, message, false, _loc.Get("common.close"),
            _loc.Get("common.cancel"), false);
        await ShowAsync(new MessageDialogWindow(), vm);
    }

    public async Task<string?> AskTextAsync(string title, string label, string? initial = null)
    {
        var vm = new ValueDialogViewModel(title, label, initial, numeric: false);
        if (!await ShowAsync(new ValueDialogWindow(), vm))
        {
            return null;
        }

        return vm.TextResult;
    }

    public async Task<decimal?> AskAmountAsync(string title, string label, decimal? initial = null)
    {
        var vm = new ValueDialogViewModel(title, label, initial?.ToString("0.00"), numeric: true);
        return await ShowAsync(new ValueDialogWindow(), vm) ? vm.NumberResult : null;
    }

    public async Task<decimal?> AskQuantityAsync(decimal initial)
    {
        var vm = new ValueDialogViewModel(_loc.Get("dialogs.quantityTitle"), _loc.Get("common.quantity"),
            initial.ToString("0.###"), numeric: true);
        return await ShowAsync(new ValueDialogWindow(), vm) ? vm.NumberResult : null;
    }

    public async Task<Discount?> AskDiscountAsync(long baseCents, string title)
    {
        var vm = new DiscountDialogViewModel(title, baseCents, _settings.Current.CurrencySymbol);
        return await ShowAsync(new DiscountDialogWindow(), vm) ? vm.Result : null;
    }

    public async Task<bool> ApproveAsync(Permission permission, string reason)
    {
        var vm = new ManagerApprovalViewModel(_auth, permission, reason);
        return await ShowAsync(new ManagerApprovalWindow(), vm);
    }

    public async Task<Customer?> PickCustomerAsync()
    {
        var vm = new CustomerPickerViewModel(_customers);
        return await ShowAsync(new CustomerPickerWindow(), vm) ? vm.Result : null;
    }

    public async Task<PaymentResult?> TakePaymentAsync(PaymentRequest request)
    {
        var vm = new PaymentViewModel(_customers, _settings, _loc);
        vm.Initialize(request);
        return await ShowAsync(new PaymentWindow(), vm) ? vm.Result : null;
    }

    /// <summary>
    /// Shows the completed-sale card without blocking the register. It closes itself
    /// when the cashier scans or adds the next product.
    /// </summary>
    public void ShowSaleCompleted(Sale sale, PrintResult? printResult)
    {
        CloseSaleCompleted();

        var vm = new SaleCompletedViewModel(_receipt, _settings, _loc, sale, printResult, this);
        var window = new SaleCompletedWindow { DataContext = vm };
        window.Closed += (_, _) =>
        {
            if (ReferenceEquals(_saleCompleted, window))
            {
                _saleCompleted = null;
            }
        };

        _saleCompleted = window;
        if (Owner is null)
        {
            return;
        }

        window.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        window.Show(Owner);
        Owner.Activate();
    }

    public void CloseSaleCompleted()
    {
        var window = _saleCompleted;
        _saleCompleted = null;
        if (window is null)
        {
            return;
        }

        try
        {
            window.Close();
        }
        catch
        {
            // Already closed.
        }
    }

    /// <summary>Shows the receipt as it will be printed. Returns the print result when printed.</summary>
    public async Task<PrintResult?> ShowReceiptPreviewAsync(Sale sale)
    {
        if (_preview is not null)
        {
            _preview.Activate();
            return null;
        }

        var vm = new ReceiptPreviewViewModel(_receipt, _loc, sale);
        var window = new ReceiptPreviewWindow();
        _preview = window;
        try
        {
            await ShowAsync(window, vm);
            return vm.PrintResult;
        }
        finally
        {
            _preview = null;
        }
    }

    public async Task<HeldSaleItem?> PickHeldSaleAsync(IReadOnlyList<HeldSaleItem> items)
    {
        var vm = new HeldSalesViewModel(items);
        return await ShowAsync(new HeldSalesWindow(), vm) ? vm.Result : null;
    }

    public async Task<bool> OpenRegisterAsync()
    {
        var amount = await AskAmountAsync(_loc.Get("checkout.openRegister"), _loc.Get("checkout.openingAmount"), 0);
        if (amount is null)
        {
            return false;
        }

        try
        {
            var session = _cash.Open(Money.ToCents(amount.Value), _loc.Get("cash.openingBalance"));
            _session.CashSession = session;
            return true;
        }
        catch (Exception ex)
        {
            await ShowMessageAsync(_loc.Get("common.error"), ex.Message);
            return false;
        }
    }

    public async Task<bool> CloseRegisterAsync()
    {
        var session = _cash.Current;
        if (session is null)
        {
            return false;
        }

        var report = _cash.BuildZReport(session);
        var amount = await AskAmountAsync(_loc.Get("cash.closeRegister"), _loc.Get("cash.countedCash"),
            report.ExpectedCashCents / 100m);
        if (amount is null)
        {
            return false;
        }

        try
        {
            var z = _cash.Close(Money.ToCents(amount.Value));
            _session.CashSession = null;
            await ShowZReportAsync(z);
            return true;
        }
        catch (Exception ex)
        {
            await ShowMessageAsync(_loc.Get("common.error"), ex.Message);
            return false;
        }
    }

    public async Task ShowZReportAsync(ZReport report)
    {
        var vm = new ZReportViewModel(report, _settings);
        await ShowAsync(new ZReportWindow(), vm);
    }

    public async Task<bool> CashMovementAsync(CashMovementType type)
    {
        var title = type == CashMovementType.In ? _loc.Get("cash.cashIn") : _loc.Get("cash.cashOut");
        var amount = await AskAmountAsync(title, _loc.Get("cash.movementAmount"));
        if (amount is null or <= 0)
        {
            return false;
        }

        var reason = await AskTextAsync(title, _loc.Get("cash.reason"));
        if (reason is null)
        {
            return false;
        }

        try
        {
            _cash.AddMovement(type, Money.ToCents(amount.Value), reason);
            return true;
        }
        catch (Exception ex)
        {
            await ShowMessageAsync(_loc.Get("common.error"), ex.Message);
            return false;
        }
    }

    public async Task<bool> EditProductAsync(Product? product)
    {
        var vm = new ProductEditorViewModel(_inventory, _settings, this, _loc, product);
        return await ShowAsync(new ProductEditorWindow(), vm);
    }

    public async Task<Product?> CreateProductForReceivingAsync(string barcode)
    {
        var vm = new ProductEditorViewModel(_inventory, _settings, this, _loc, null, barcode, deferSave: true);
        return await ShowAsync(new ProductEditorWindow(), vm) ? vm.SavedProduct : null;
    }

    public async Task<bool> AdjustStockAsync(Product product)
    {
        var vm = new StockAdjustViewModel(_inventory, _settings, product);
        return await ShowAsync(new StockAdjustWindow(), vm);
    }

    public async Task<bool> EditCustomerAsync(Customer? customer)
    {
        var vm = new CustomerEditorViewModel(_customers, _loc, customer);
        return await ShowAsync(new CustomerEditorWindow(), vm);
    }

    public async Task<bool> EditSupplierAsync(Supplier? supplier)
    {
        var vm = new SupplierEditorViewModel(_suppliers, _loc, supplier);
        return await ShowAsync(new SupplierEditorWindow(), vm);
    }

    public async Task<bool> EditUserAsync(User? user)
    {
        var vm = new UserEditorViewModel(_auth, _loc, user);
        return await ShowAsync(new UserEditorWindow(), vm);
    }

    public async Task<bool> CreatePurchaseOrderAsync()
    {
        var vm = new PurchaseEditorViewModel(_suppliers, _inventory, _settings, _loc);
        return await ShowAsync(new PurchaseEditorWindow(), vm);
    }
}
