using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RixelPOS.Application.Cash;
using RixelPOS.Application.Checkout;
using RixelPOS.Application.Reports;
using RixelPOS.Application.Services;
using RixelPOS.Application.Sessions;
using RixelPOS.Desktop.Localization;
using RixelPOS.Desktop.Services;
using RixelPOS.Domain.Enums;
using RixelPOS.Domain.Reporting;
using RixelPOS.Infrastructure;

namespace RixelPOS.Desktop.ViewModels;

public sealed partial class DashboardViewModel : ViewModelBase, IActivatable
{
    private readonly ReportService _reports;
    private readonly SettingsService _settings;
    private readonly AppSession _session;
    private readonly CashRegisterService _cash;
    private readonly ReceiptService _receipt;
    private readonly DialogService _dialogs;
    private readonly LocalizationService _loc;
    private readonly CheckoutService _checkout;

    [ObservableProperty] private string _todayTotal = "";
    [ObservableProperty] private string _todayCount = "";
    [ObservableProperty] private string _monthTotal = "";
    [ObservableProperty] private string _averageTicket = "";
    [ObservableProperty] private string _lowStockCount = "";
    [ObservableProperty] private string _productCount = "";
    [ObservableProperty] private string _greeting = "";
    [ObservableProperty] private bool _isCashOpen;
    [ObservableProperty] private bool _hasSales;
    [ObservableProperty] private bool _hasLowStock;
    [ObservableProperty] private string _statusMessage = "";
    [ObservableProperty] private bool _statusIsError;

    public DashboardViewModel(
        ReportService reports,
        SettingsService settings,
        AppSession session,
        CashRegisterService cash,
        ReceiptService receipt,
        DialogService dialogs,
        LocalizationService loc,
        CheckoutService checkout)
    {
        _reports = reports;
        _settings = settings;
        _session = session;
        _cash = cash;
        _receipt = receipt;
        _dialogs = dialogs;
        _loc = loc;
        _checkout = checkout;
    }

    public event EventHandler<string>? NavigationRequested;

    public ObservableCollection<SaleHistoryItem> RecentSales { get; } = new();
    public ObservableCollection<TopProductItem> TopProducts { get; } = new();
    public ObservableCollection<LowStockItem> LowStockItems { get; } = new();

    public void Activate()
    {
        try
        {
            var snapshot = _reports.GetDashboard();
            TodayTotal = _settings.FormatMoney(snapshot.TodayTotalCents);
            TodayCount = snapshot.TodaySalesCount.ToString();
            MonthTotal = _settings.FormatMoney(snapshot.MonthTotalCents);
            AverageTicket = _settings.FormatMoney(snapshot.TodaySalesCount == 0
                ? 0
                : snapshot.TodayTotalCents / snapshot.TodaySalesCount);
            LowStockCount = snapshot.LowStockCount.ToString();
            ProductCount = snapshot.ProductCount.ToString();
            Greeting = _loc.Format("dashboard.greeting", _session.CurrentUser?.FullName.Split(' ')[0] ?? "");
            IsCashOpen = snapshot.OpenSession is not null;

            RecentSales.Clear();
            foreach (var sale in snapshot.RecentSales)
            {
                RecentSales.Add(new SaleHistoryItem(sale, _settings.FormatMoney(sale.TotalCents),
                    (sale.CompletedAt ?? sale.CreatedAt).ToLocalTime().ToString("dd/MM HH:mm"),
                    StatusText(sale.Status)));
            }

            TopProducts.Clear();
            foreach (var product in snapshot.TopProducts)
            {
                TopProducts.Add(new TopProductItem(product.Name, product.Quantity, _settings.FormatMoney(product.TotalCents)));
            }

            LowStockItems.Clear();
            foreach (var product in snapshot.LowStockProducts)
            {
                LowStockItems.Add(new LowStockItem(product, _settings.FormatQuantity(product.Stock),
                    _settings.FormatQuantity(product.MinStock)));
            }

            HasSales = RecentSales.Count > 0;
            HasLowStock = LowStockItems.Count > 0;
        }
        catch (Exception ex)
        {
            ReportError(ex);
        }
    }

    [RelayCommand]
    private void NewSale() => NavigationRequested?.Invoke(this, "checkout");

    [RelayCommand]
    private void ReceiveStock() => NavigationRequested?.Invoke(this, "inventory:receive");

    [RelayCommand]
    private void ViewReports() => NavigationRequested?.Invoke(this, "reports");

    [RelayCommand]
    private async Task OpenRegisterAsync()
    {
        if (await _dialogs.OpenRegisterAsync())
        {
            Activate();
        }
    }

    [RelayCommand]
    private void GoToCheckout() => NavigationRequested?.Invoke(this, "checkout");

    [RelayCommand]
    private async Task ReprintAsync(SaleHistoryItem? item)
    {
        if (item is null)
        {
            return;
        }

        var result = await _receipt.PrintAsync(item.Sale);
        StatusMessage = result.Message;
        StatusIsError = !result.Success;
    }

    [RelayCommand]
    private Task ExportPdfAsync(SaleHistoryItem? item)
    {
        if (item is null)
        {
            return Task.CompletedTask;
        }

        try
        {
            Directory.CreateDirectory(AppPaths.DocumentsDirectory);
            var path = _receipt.DefaultPdfPath(item.Sale.Invoice, AppPaths.DocumentsDirectory);
            _receipt.ExportPdf(item.Sale, path);
            StatusMessage = _loc.Format("reports.exported", path);
            StatusIsError = false;
        }
        catch (Exception ex)
        {
            ReportError(ex);
            StatusIsError = true;
        }

        return Task.CompletedTask;
    }

    [RelayCommand]
    private async Task VoidSaleAsync(SaleHistoryItem? item)
    {
        if (item is null || item.IsVoided)
        {
            return;
        }

        var reason = await _dialogs.AskTextAsync(_loc.Get("checkout.voidSale"), _loc.Get("checkout.voidReason"), "");
        if (string.IsNullOrWhiteSpace(reason))
        {
            return;
        }

        var user = _session.CurrentUser;
        if (user is null)
        {
            return;
        }

        if (RixelPOS.Application.Security.DiscountPolicy.VoidRequiresApproval(_settings.Current, user) &&
            !await _dialogs.ApproveAsync(Permission.VoidSale, reason))
        {
            return;
        }

        try
        {
            _checkout.VoidSale(item.Sale.Id, reason, user);
            StatusMessage = _loc.Get("checkout.saleVoided");
            StatusIsError = false;
            Activate();
        }
        catch (Exception ex)
        {
            ReportError(ex);
            StatusIsError = true;
        }
    }

    private string StatusText(SaleStatus status) => status switch
    {
        SaleStatus.Voided => _loc.Get("checkout.saleVoided"),
        _ => _loc.Get("common.success")
    };
}
