using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RixelPOS.Application.Abstractions;
using RixelPOS.Application.Checkout;
using RixelPOS.Application.Reports;
using RixelPOS.Application.Services;
using RixelPOS.Desktop.Localization;
using RixelPOS.Desktop.Services;
using RixelPOS.Domain.Entities;
using RixelPOS.Domain.Enums;
using RixelPOS.Domain.Reporting;
using RixelPOS.Infrastructure;

namespace RixelPOS.Desktop.ViewModels;

public sealed class DailyReportItem
{
    public DailyReportItem(DailySalesPoint point, long max, string totalText)
    {
        DateText = point.Date.ToString("dd/MM");
        TotalText = totalText;
        CountText = point.SalesCount.ToString();
        Percent = max <= 0 ? 0 : Math.Round(point.TotalCents * 100d / max, 1);
    }

    public string DateText { get; }
    public string TotalText { get; }
    public string CountText { get; }
    public double Percent { get; }
}

public sealed class ProductReportItem
{
    public ProductReportItem(ProductSalesRow row, string totalText, string profitText, string quantityText)
    {
        Name = row.Name;
        TotalText = totalText;
        ProfitText = profitText;
        QuantityText = quantityText;
    }

    public string Name { get; }
    public string TotalText { get; }
    public string ProfitText { get; }
    public string QuantityText { get; }
}

public sealed class PaymentReportItem
{
    public PaymentReportItem(string method, int count, string totalText)
    {
        Method = method;
        CountText = count.ToString();
        TotalText = totalText;
    }

    public string Method { get; }
    public string CountText { get; }
    public string TotalText { get; }
}

public sealed class CashierReportItem
{
    public CashierReportItem(string name, int count, string totalText)
    {
        Name = name;
        CountText = count.ToString();
        TotalText = totalText;
    }

    public string Name { get; }
    public string CountText { get; }
    public string TotalText { get; }
}

public sealed class CategoryReportItem
{
    public CategoryReportItem(string name, decimal quantity, string totalText)
    {
        Name = name;
        QuantityText = quantity.ToString("0.###");
        TotalText = totalText;
    }

    public string Name { get; }
    public string QuantityText { get; }
    public string TotalText { get; }
}

public sealed partial class ReportsViewModel : ViewModelBase, IActivatable
{
    private readonly ReportService _reports;
    private readonly SettingsService _settings;
    private readonly DialogService _dialogs;
    private readonly FileDialogService _files;
    private readonly LocalizationService _loc;
    private readonly ReceiptService _receipt;
    private readonly IPosStore _store;
    private SalesReport? _current;
    private Sale? _foundSale;

    [ObservableProperty] private DateTimeOffset? _fromDate;
    [ObservableProperty] private DateTimeOffset? _toDate;
    [ObservableProperty] private string _totalText = "";
    [ObservableProperty] private string _countText = "";
    [ObservableProperty] private string _itemsText = "";
    [ObservableProperty] private string _taxText = "";
    [ObservableProperty] private string _discountText = "";
    [ObservableProperty] private string _costText = "";
    [ObservableProperty] private string _profitText = "";
    [ObservableProperty] private string _averageText = "";
    [ObservableProperty] private string _rangeText = "";
    [ObservableProperty] private bool _hasData;
    [ObservableProperty] private string _statusMessage = "";
    [ObservableProperty] private bool _statusIsError;
    [ObservableProperty] private string _invoiceSearch = "";
    [ObservableProperty] private bool _hasFoundInvoice;
    [ObservableProperty] private string _foundInvoiceNumber = "";
    [ObservableProperty] private string _foundInvoiceTotal = "";
    [ObservableProperty] private string _foundInvoiceDate = "";
    [ObservableProperty] private string _invoiceSearchMessage = "";

    public ReportsViewModel(
        ReportService reports,
        SettingsService settings,
        DialogService dialogs,
        FileDialogService files,
        LocalizationService loc,
        ReceiptService receipt,
        IPosStore store)
    {
        _reports = reports;
        _settings = settings;
        _dialogs = dialogs;
        _files = files;
        _loc = loc;
        _receipt = receipt;
        _store = store;
        SetTodayRange();
    }

    public ObservableCollection<DailyReportItem> Daily { get; } = new();
    public ObservableCollection<ProductReportItem> TopProducts { get; } = new();
    public ObservableCollection<PaymentReportItem> Payments { get; } = new();
    public ObservableCollection<CashierReportItem> Cashiers { get; } = new();
    public ObservableCollection<CategoryReportItem> Categories { get; } = new();

    public void Activate() => Generate();

    [RelayCommand]
    private void Today()
    {
        SetTodayRange();
        Generate();
    }

    [RelayCommand]
    private void LastWeek()
    {
        FromDate = DateTimeOffset.Now.Date.AddDays(-6);
        ToDate = DateTimeOffset.Now.Date;
        Generate();
    }

    [RelayCommand]
    private void ThisMonth()
    {
        var now = DateTime.Now;
        FromDate = new DateTimeOffset(new DateTime(now.Year, now.Month, 1));
        ToDate = DateTimeOffset.Now.Date;
        Generate();
    }

    [RelayCommand]
    private void Generate()
    {
        try
        {
            var from = (FromDate?.Date ?? DateTime.Today).Date;
            var to = (ToDate?.Date ?? DateTime.Today).Date.AddDays(1).AddSeconds(-1);
            if (to < from)
            {
                (from, to) = (to.Date, from.Date.AddDays(1).AddSeconds(-1));
            }

            var report = _reports.Get(from, to);
            _current = report;
            RangeText = $"{from:dd/MM/yyyy} — {to:dd/MM/yyyy}";
            TotalText = _settings.FormatMoney(report.TotalCents);
            CountText = report.SalesCount.ToString();
            ItemsText = report.ItemCount.ToString();
            TaxText = _settings.FormatMoney(report.TaxCents);
            DiscountText = _settings.FormatMoney(report.DiscountCents);
            CostText = _settings.FormatMoney(report.CostCents);
            ProfitText = _settings.FormatMoney(report.ProfitCents);
            AverageText = _settings.FormatMoney(report.AverageTicketCents);
            HasData = report.SalesCount > 0;

            Daily.Clear();
            var max = report.Daily.Count == 0 ? 0 : report.Daily.Max(d => d.TotalCents);
            foreach (var item in report.Daily)
            {
                Daily.Add(new DailyReportItem(item, max, _settings.FormatMoney(item.TotalCents)));
            }

            TopProducts.Clear();
            foreach (var item in report.TopProducts)
            {
                TopProducts.Add(new ProductReportItem(item,
                    _settings.FormatMoney(item.TotalCents),
                    _settings.FormatMoney(item.ProfitCents),
                    item.Quantity.ToString("0.###")));
            }

            Payments.Clear();
            foreach (var item in report.ByPayment.Where(p => p.Count > 0))
            {
                Payments.Add(new PaymentReportItem(PaymentName(item.Method), item.Count, _settings.FormatMoney(item.TotalCents)));
            }

            Cashiers.Clear();
            foreach (var item in report.ByCashier)
            {
                Cashiers.Add(new CashierReportItem(item.CashierName, item.SalesCount, _settings.FormatMoney(item.TotalCents)));
            }

            Categories.Clear();
            foreach (var item in report.ByCategory)
            {
                Categories.Add(new CategoryReportItem(item.CategoryName, item.Quantity, _settings.FormatMoney(item.TotalCents)));
            }

            StatusMessage = HasData ? "" : _loc.Get("reports.noData");
            StatusIsError = false;
        }
        catch (Exception ex)
        {
            ReportError(ex);
            StatusIsError = true;
        }
    }

    [RelayCommand]
    private async Task ExportCsvAsync()
    {
        if (_current is null)
        {
            Generate();
        }

        if (_current is null)
        {
            return;
        }

        var path = await _files.PickSaveAsync(_loc.Get("reports.exportCsv"),
            $"reporte-{DateTime.Now:yyyyMMdd}.csv", "CSV", "csv");
        if (path is null)
        {
            return;
        }

        try
        {
            _reports.ExportCsv(_current, path);
            StatusMessage = _loc.Format("reports.exported", path);
            StatusIsError = false;
        }
        catch (Exception ex)
        {
            ReportError(ex);
            StatusIsError = true;
        }
    }

    [RelayCommand]
    private async Task ExportPdfAsync()
    {
        if (_current is null)
        {
            Generate();
        }

        if (_current is null)
        {
            return;
        }

        var path = await _files.PickSaveAsync(_loc.Get("reports.exportPdf"),
            $"reporte-{DateTime.Now:yyyyMMdd}.pdf", "PDF", "pdf");
        if (path is null)
        {
            return;
        }

        try
        {
            _reports.ExportPdf(_current, path);
            StatusMessage = _loc.Format("reports.exported", path);
            StatusIsError = false;
        }
        catch (Exception ex)
        {
            ReportError(ex);
            StatusIsError = true;
        }
    }

    public string Money(long cents) => _settings.FormatMoney(cents);

    [RelayCommand]
    private void SearchInvoice()
    {
        InvoiceSearchMessage = "";
        _foundSale = null;
        HasFoundInvoice = false;
        var number = InvoiceSearch.Trim();
        if (number.Length == 0)
        {
            return;
        }

        _foundSale = _store.GetSaleByInvoiceNumber(number);
        if (_foundSale is null)
        {
            InvoiceSearchMessage = _loc.Get("checkout.scanNotFound");
            return;
        }

        FoundInvoiceNumber = _foundSale.Invoice?.Number ?? number;
        FoundInvoiceTotal = _settings.FormatMoney(_foundSale.TotalCents);
        FoundInvoiceDate = (_foundSale.CompletedAt ?? _foundSale.CreatedAt).ToLocalTime().ToString("dd/MM/yyyy HH:mm");
        HasFoundInvoice = true;
    }

    [RelayCommand]
    private async Task PreviewInvoiceAsync()
    {
        if (_foundSale is null)
        {
            return;
        }

        var result = await _dialogs.ShowReceiptPreviewAsync(_foundSale);
        if (result is not null)
        {
            InvoiceSearchMessage = result.Message;
        }
    }

    [RelayCommand]
    private async Task ReprintInvoiceAsync()
    {
        if (_foundSale is null)
        {
            return;
        }

        var result = await _receipt.PrintAsync(_foundSale);
        InvoiceSearchMessage = result.Message;
    }

    [RelayCommand]
    private void ExportInvoicePdf()
    {
        if (_foundSale is null)
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(AppPaths.DocumentsDirectory);
            var path = _receipt.DefaultPdfPath(_foundSale.Invoice, AppPaths.DocumentsDirectory);
            _receipt.ExportPdf(_foundSale, path);
            InvoiceSearchMessage = _loc.Format("reports.exported", path);
        }
        catch (Exception ex)
        {
            InvoiceSearchMessage = ex.Message;
        }
    }

    public string PaymentName(PaymentMethod method) => method switch
    {
        PaymentMethod.Cash => _loc.Get("checkout.cash"),
        PaymentMethod.Card => _loc.Get("checkout.card"),
        PaymentMethod.Transfer => _loc.Get("checkout.transfer"),
        PaymentMethod.StoreCredit => _loc.Get("checkout.storeCredit"),
        PaymentMethod.Credit => _loc.Get("checkout.credit"),
        _ => method.ToString()
    };

    private void SetTodayRange()
    {
        FromDate = DateTimeOffset.Now.Date;
        ToDate = DateTimeOffset.Now.Date;
    }
}
