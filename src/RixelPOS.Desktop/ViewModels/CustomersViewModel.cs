using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RixelPOS.Application.Checkout;
using RixelPOS.Application.Customers;
using RixelPOS.Application.Services;
using RixelPOS.Application.Sessions;
using RixelPOS.Desktop.Localization;
using RixelPOS.Desktop.Services;
using RixelPOS.Domain.Entities;
using RixelPOS.Infrastructure;

namespace RixelPOS.Desktop.ViewModels;

public sealed partial class CustomersViewModel : ViewModelBase, IActivatable
{
    private readonly CustomerService _customers;
    private readonly SettingsService _settings;
    private readonly DialogService _dialogs;
    private readonly ReceiptService _receipt;
    private readonly LocalizationService _loc;

    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private CustomerListItem? _selectedItem;
    [ObservableProperty] private string _statusMessage = "";
    [ObservableProperty] private bool _statusIsError;
    [ObservableProperty] private bool _hasSelection;
    [ObservableProperty] private string _detailName = "";
    [ObservableProperty] private string _detailPhone = "";
    [ObservableProperty] private string _detailTaxId = "";
    [ObservableProperty] private string _detailAddress = "";
    [ObservableProperty] private string _balanceText = "";
    [ObservableProperty] private string _storeCreditText = "";
    [ObservableProperty] private string _creditLimitText = "";
    [ObservableProperty] private string _totalSpentText = "";
    [ObservableProperty] private string _historyTitle = "";
    [ObservableProperty] private bool _hasHistory;

    public CustomersViewModel(
        CustomerService customers,
        SettingsService settings,
        DialogService dialogs,
        ReceiptService receipt,
        LocalizationService loc)
    {
        _customers = customers;
        _settings = settings;
        _dialogs = dialogs;
        _receipt = receipt;
        _loc = loc;
    }

    public ObservableCollection<CustomerListItem> Items { get; } = new();
    public ObservableCollection<SaleHistoryItem> History { get; } = new();


    public void Activate()
    {
        var selectedId = SelectedItem?.Customer.Id;
        Load();
        if (selectedId is not null)
        {
            SelectedItem = Items.FirstOrDefault(i => i.Customer.Id == selectedId);
        }
    }

    partial void OnSearchTextChanged(string value) => Load();

    partial void OnSelectedItemChanged(CustomerListItem? value)
    {
        HasSelection = value is not null;
        if (value is null)
        {
            History.Clear();
            HasHistory = false;
            return;
        }

        var c = value.Customer;
        DetailName = c.Name;
        DetailPhone = string.IsNullOrWhiteSpace(c.Phone) ? "—" : c.Phone;
        DetailTaxId = string.IsNullOrWhiteSpace(c.TaxId) ? "—" : c.TaxId;
        DetailAddress = string.IsNullOrWhiteSpace(c.Address) ? "—" : c.Address;
        BalanceText = _settings.FormatMoney(c.BalanceCents);
        StoreCreditText = _settings.FormatMoney(c.StoreCreditCents);
        CreditLimitText = _settings.FormatMoney(c.CreditLimitCents);
        HistoryTitle = _loc.Format("customers.historyTitle", c.Name);
        LoadHistory(c);
    }

    [RelayCommand]
    private void Refresh() => Load();

    [RelayCommand]
    private async Task NewCustomerAsync()
    {
        if (await _dialogs.EditCustomerAsync(null))
        {
            Load();
            StatusMessage = _loc.Get("customers.saved");
            StatusIsError = false;
        }
    }

    [RelayCommand]
    private async Task EditCustomerAsync(CustomerListItem? item)
    {
        item ??= SelectedItem;
        if (item is null)
        {
            return;
        }

        if (await _dialogs.EditCustomerAsync(item.Customer))
        {
            Load();
            StatusMessage = _loc.Get("customers.saved");
            StatusIsError = false;
        }
    }

    [RelayCommand]
    private async Task AddCreditAsync()
    {
        if (SelectedItem is null)
        {
            return;
        }

        var amount = await _dialogs.AskAmountAsync(_loc.Get("customers.addCredit"), _loc.Get("customers.amount"));
        if (amount is null or <= 0)
        {
            return;
        }

        try
        {
            _customers.AddStoreCredit(SelectedItem.Customer.Id, (long)Math.Round(amount.Value * 100m));
            Load();
            StatusMessage = _loc.Get("toast.saved");
            StatusIsError = false;
        }
        catch (Exception ex)
        {
            ReportError(ex);
            StatusIsError = true;
        }
    }

    [RelayCommand]
    private async Task PayBalanceAsync()
    {
        if (SelectedItem is null)
        {
            return;
        }

        var amount = await _dialogs.AskAmountAsync(_loc.Get("customers.payBalance"), _loc.Get("customers.amount"));
        if (amount is null or <= 0)
        {
            return;
        }

        try
        {
            _customers.PayBalance(SelectedItem.Customer.Id, (long)Math.Round(amount.Value * 100m));
            Load();
            StatusMessage = _loc.Get("toast.saved");
            StatusIsError = false;
        }
        catch (Exception ex)
        {
            ReportError(ex);
            StatusIsError = true;
        }
    }

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
    private void ExportPdf(SaleHistoryItem? item)
    {
        if (item is null)
        {
            return;
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
    }

    private void Load()
    {
        try
        {
            var selectedId = SelectedItem?.Customer.Id;
            Items.Clear();
            foreach (var customer in _customers.Search(string.IsNullOrWhiteSpace(SearchText) ? null : SearchText))
            {
                Items.Add(new CustomerListItem(customer,
                    _settings.FormatMoney(customer.BalanceCents),
                    _settings.FormatMoney(customer.StoreCreditCents),
                    _settings.FormatMoney(customer.CreditLimitCents)));
            }

            if (selectedId is not null)
            {
                SelectedItem = Items.FirstOrDefault(i => i.Customer.Id == selectedId);
            }
        }
        catch (Exception ex)
        {
            ReportError(ex);
        }
    }

    private void LoadHistory(Customer customer)
    {
        History.Clear();
        foreach (var sale in _customers.GetHistory(customer.Id))
        {
            var item = new SaleHistoryItem(sale, _settings.FormatMoney(sale.TotalCents),
                (sale.CompletedAt ?? sale.CreatedAt).ToLocalTime().ToString("dd/MM/yyyy HH:mm"),
                sale.Status.ToString());
            History.Add(item);
        }

        HasHistory = History.Count > 0;
        TotalSpentText = _settings.FormatMoney(History.Sum(h => h.Sale.TotalCents));
    }
}
