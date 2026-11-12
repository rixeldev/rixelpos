using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RixelPOS.Application.Checkout;
using RixelPOS.Application.Customers;
using RixelPOS.Application.Printing;
using RixelPOS.Application.Services;
using RixelPOS.Desktop.Localization;
using RixelPOS.Domain.Entities;
using RixelPOS.Domain.Enums;
using RixelPOS.Infrastructure;

namespace RixelPOS.Desktop.ViewModels;

public sealed record PaymentRequest(long TotalCents, Customer? Customer, bool StoreCreditEnabled, bool CreditEnabled);

public sealed record PaymentResult(IReadOnlyList<Payment> Payments, Customer? Customer);

public sealed class PaymentLineItem
{
    public PaymentLineItem(Payment payment, string methodName, string amountText, string? detail = null)
    {
        Payment = payment;
        MethodName = methodName;
        AmountText = amountText;
        Detail = detail;
    }

    public Payment Payment { get; }
    public string MethodName { get; }
    public string AmountText { get; }
    public string? Detail { get; }
}

public sealed partial class PaymentViewModel : DialogViewModelBase
{
    private readonly CustomerService _customers;
    private readonly SettingsService _settings;
    private readonly LocalizationService _loc;
    private PaymentRequest _request = new(0, null, false, false);
    private bool _initialized;

    [ObservableProperty] private string _totalText = "";
    [ObservableProperty] private string _remainingText = "";
    [ObservableProperty] private string _changeText = "";
    [ObservableProperty] private string _amountText = "";
    [ObservableProperty] private string _selectedMethodName = "";
    [ObservableProperty] private string _methodHint = "";
    [ObservableProperty] private PaymentMethod _selectedMethod = PaymentMethod.Cash;
    [ObservableProperty] private Customer? _selectedCustomer;
    [ObservableProperty] private string _customerSearch = "";
    [ObservableProperty] private bool _canConfirm;
    [ObservableProperty] private bool _canUseStoreCredit;
    [ObservableProperty] private bool _canUseCredit;
    [ObservableProperty] private string _creditInfo = "";
    [ObservableProperty] private long _remainingCents;
    [ObservableProperty] private long _changeCents;

    public PaymentViewModel(CustomerService customers, SettingsService settings, LocalizationService loc)
    {
        _customers = customers;
        _settings = settings;
        _loc = loc;
        SelectMethod("Cash");
        UpdateMethodLabels();
    }

    public ObservableCollection<Customer> Customers { get; } = new();
    public ObservableCollection<PaymentLineItem> Payments { get; } = new();

    public PaymentResult? Result { get; private set; }

    public void Initialize(PaymentRequest request)
    {
        _request = request;
        TotalText = _settings.FormatMoney(request.TotalCents);
        RemainingCents = request.TotalCents;
        CanUseStoreCredit = request.StoreCreditEnabled;
        CanUseCredit = request.CreditEnabled;
        SelectedCustomer = request.Customer;
        LoadCustomers();
        SelectMethod("Cash");
        UpdateMethodLabels();
        Recalculate();
        _initialized = true;
    }

    partial void OnSelectedCustomerChanged(Customer? value)
    {
        UpdateCreditInfo();
    }

    partial void OnCustomerSearchChanged(string value) => LoadCustomers();

    partial void OnAmountTextChanged(string value) => ErrorMessage = null;

    partial void OnSelectedMethodChanged(PaymentMethod value) => UpdateMethodLabels();

    [RelayCommand]
    private void SelectMethod(string? method)
    {
        if (System.Enum.TryParse<PaymentMethod>(method, true, out var parsed))
        {
            SelectedMethod = parsed;
        }
    }

    [RelayCommand]
    private void AddPayment()
    {
        ErrorMessage = null;
        if (!TryParseAmount(out var rawAmount))
        {
            ErrorMessage = _loc.Get("dialogs.invalidAmount");
            return;
        }

        if (rawAmount <= 0 && RemainingCents > 0)
        {
            rawAmount = RemainingCents;
        }

        rawAmount = Math.Max(0, rawAmount);
        if (rawAmount <= 0)
        {
            ErrorMessage = _loc.Get("dialogs.invalidAmount");
            return;
        }

        var remaining = RemainingCents;
        var applied = Math.Min(rawAmount, remaining);
        Payment? payment = null;

        switch (SelectedMethod)
        {
            case PaymentMethod.Cash:
                if (applied <= 0)
                {
                    ErrorMessage = _loc.Get("checkout.insufficientPayment");
                    return;
                }

                payment = new Payment
                {
                    Method = PaymentMethod.Cash,
                    AmountCents = applied,
                    TenderedCents = rawAmount
                };
                break;

            case PaymentMethod.Card:
            case PaymentMethod.Transfer:
                if (applied <= 0)
                {
                    ErrorMessage = _loc.Get("checkout.insufficientPayment");
                    return;
                }

                payment = new Payment
                {
                    Method = SelectedMethod,
                    AmountCents = applied,
                    TenderedCents = applied
                };
                break;

            case PaymentMethod.StoreCredit:
                if (!CanUseStoreCredit)
                {
                    ErrorMessage = _loc.Get("store_credit.disabled");
                    return;
                }

                if (SelectedCustomer is null)
                {
                    ErrorMessage = _loc.Get("checkout.customerRequiredCredit");
                    return;
                }

                var availableStore = SelectedCustomer.StoreCreditCents - Payments
                    .Where(p => p.Payment.Method == PaymentMethod.StoreCredit).Sum(p => p.Payment.AmountCents);
                applied = Math.Min(applied, Math.Max(0, availableStore));
                if (applied <= 0)
                {
                    ErrorMessage = _loc.Get("checkout.storeCreditInsufficient");
                    return;
                }

                payment = new Payment { Method = PaymentMethod.StoreCredit, AmountCents = applied, TenderedCents = applied };
                break;

            case PaymentMethod.Credit:
                if (!CanUseCredit)
                {
                    ErrorMessage = _loc.Get("credit.disabled");
                    return;
                }

                if (SelectedCustomer is null)
                {
                    ErrorMessage = _loc.Get("checkout.customerRequiredCredit");
                    return;
                }

                var creditLeft = SelectedCustomer.CreditLimitCents - SelectedCustomer.BalanceCents -
                    Payments.Where(p => p.Payment.Method == PaymentMethod.Credit).Sum(p => p.Payment.AmountCents);
                applied = Math.Min(applied, Math.Max(0, creditLeft));
                if (applied <= 0)
                {
                    ErrorMessage = _loc.Get("checkout.creditLimit");
                    return;
                }

                payment = new Payment { Method = PaymentMethod.Credit, AmountCents = applied, TenderedCents = applied };
                break;
        }

        if (payment is null)
        {
            return;
        }

        Payments.Add(new PaymentLineItem(payment, MethodName(payment.Method),
            _settings.FormatMoney(payment.AmountCents),
            payment.Method == PaymentMethod.Cash && payment.TenderedCents > payment.AmountCents
                ? $"{_loc.Get("receipt.received")}: {_settings.FormatMoney(payment.TenderedCents)}"
                : null));
        AmountText = "";
        Recalculate();
    }

    [RelayCommand]
    private void RemoveLast()
    {
        if (Payments.Count == 0)
        {
            return;
        }

        Payments.RemoveAt(Payments.Count - 1);
        Recalculate();
    }

    [RelayCommand]
    private void QuickAmount(string? value)
    {
        AmountText = value switch
        {
            "exact" => (RemainingCents / 100m).ToString("0.00", CultureInfo.InvariantCulture),
            "100" => "100",
            "500" => "500",
            "1000" => "1000",
            "2000" => "2000",
            _ => AmountText
        };
    }

    [RelayCommand]
    private void Keypad(string? digit)
    {
        if (string.IsNullOrEmpty(digit))
        {
            return;
        }

        if (digit == ".")
        {
            if (!AmountText.Contains('.'))
            {
                AmountText = string.IsNullOrEmpty(AmountText) ? "0." : AmountText + ".";
            }

            return;
        }

        if (AmountText.Length >= 12)
        {
            return;
        }

        AmountText += digit;
    }

    [RelayCommand]
    private void Backspace()
    {
        if (AmountText.Length > 0)
        {
            AmountText = AmountText[..^1];
        }
    }

    [RelayCommand]
    private void ClearAmount() => AmountText = "";

    [RelayCommand]
    private void ClearCustomer()
    {
        SelectedCustomer = null;
        UpdateCreditInfo();
    }

    [RelayCommand]
    private void Confirm()
    {
        if (RemainingCents > 0)
        {
            ErrorMessage = _loc.Get("checkout.insufficientPayment");
            return;
        }

        if (Payments.Count == 0)
        {
            ErrorMessage = _loc.Get("checkout.paymentRequired");
            return;
        }

        Result = new PaymentResult(Payments.Select(p => p.Payment).ToList(), SelectedCustomer);
        Close(true);
    }

    [RelayCommand]
    private void Cancel() => Close(false);

    private void LoadCustomers()
    {
        if (!_initialized && Customers.Count > 0)
        {
            return;
        }

        var selectedId = SelectedCustomer?.Id;
        Customers.Clear();
        foreach (var customer in _customers.Search(string.IsNullOrWhiteSpace(CustomerSearch) ? null : CustomerSearch).Take(100))
        {
            Customers.Add(customer);
        }

        if (selectedId is not null)
        {
            SelectedCustomer = Customers.FirstOrDefault(c => c.Id == selectedId) ?? SelectedCustomer;
        }
    }

    private void UpdateCreditInfo()
    {
        if (SelectedCustomer is null)
        {
            CreditInfo = "";
            OnPropertyChanged(nameof(HasCreditInfo));
            return;
        }

        var available = Math.Max(0, SelectedCustomer.CreditLimitCents - SelectedCustomer.BalanceCents);
        CreditInfo = $"{_loc.Get("checkout.availableCredit")}: {_settings.FormatMoney(available)} · " +
                     $"{_loc.Get("customers.storeCredit")}: {_settings.FormatMoney(SelectedCustomer.StoreCreditCents)}";
        OnPropertyChanged(nameof(HasCreditInfo));
    }

    public bool HasCreditInfo => !string.IsNullOrWhiteSpace(CreditInfo);

    private void UpdateMethodLabels()
    {
        SelectedMethodName = MethodName(SelectedMethod);
        MethodHint = SelectedMethod switch
        {
            PaymentMethod.Cash => _loc.Get("checkout.hintCash"),
            PaymentMethod.Card => _loc.Get("checkout.hintCard"),
            PaymentMethod.Transfer => _loc.Get("checkout.hintTransfer"),
            PaymentMethod.StoreCredit => _loc.Get("checkout.hintStoreCredit"),
            PaymentMethod.Credit => _loc.Get("checkout.hintCredit"),
            _ => ""
        };
    }

    private void Recalculate()
    {
        var paid = Payments.Sum(p => p.Payment.AmountCents);
        RemainingCents = Math.Max(0, _request.TotalCents - paid);
        ChangeCents = Payments.Where(p => p.Payment.Method == PaymentMethod.Cash)
            .Sum(p => Math.Max(0, p.Payment.TenderedCents - p.Payment.AmountCents));
        TotalText = _settings.FormatMoney(_request.TotalCents);
        RemainingText = _settings.FormatMoney(RemainingCents);
        ChangeText = _settings.FormatMoney(ChangeCents);
        CanConfirm = RemainingCents == 0 && Payments.Count > 0;
        UpdateCreditInfo();
    }

    private bool TryParseAmount(out long cents)
    {
        cents = 0;
        if (string.IsNullOrWhiteSpace(AmountText))
        {
            return true;
        }

        var text = AmountText.Replace(",", ".").Replace("RD$", "").Replace("$", "").Trim();
        if (!decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var amount))
        {
            return false;
        }

        cents = (long)Math.Round(amount * 100m, MidpointRounding.AwayFromZero);
        return true;
    }

    private string MethodName(PaymentMethod method) => method switch
    {
        PaymentMethod.Cash => _loc.Get("checkout.cash"),
        PaymentMethod.Card => _loc.Get("checkout.card"),
        PaymentMethod.Transfer => _loc.Get("checkout.transfer"),
        PaymentMethod.StoreCredit => _loc.Get("checkout.storeCredit"),
        PaymentMethod.Credit => _loc.Get("checkout.credit"),
        _ => method.ToString()
    };
}

public sealed partial class SaleCompletedViewModel : DialogViewModelBase
{
    private readonly ReceiptService _receipt;
    private readonly SettingsService _settings;
    private readonly LocalizationService _loc;
    private readonly Sale _sale;
    private readonly PrintResult? _initialPrint;

    [ObservableProperty] private string _invoiceNumber = "";
    [ObservableProperty] private string _totalText = "";
    [ObservableProperty] private string _changeText = "";
    [ObservableProperty] private string _statusMessage = "";
    [ObservableProperty] private bool _showChange;
    [ObservableProperty] private bool _statusIsError;

    public SaleCompletedViewModel(ReceiptService receipt, SettingsService settings, LocalizationService loc, Sale sale, PrintResult? initialPrint = null)
    {
        _receipt = receipt;
        _settings = settings;
        _loc = loc;
        _sale = sale;
        _initialPrint = initialPrint;
        InvoiceNumber = sale.Invoice?.Number ?? $"#{sale.Number}";
        TotalText = settings.FormatMoney(sale.TotalCents);
        ChangeText = settings.FormatMoney(sale.ChangeCents);
        ShowChange = sale.ChangeCents > 0;
        if (initialPrint is not null)
        {
            StatusMessage = initialPrint.Message;
            StatusIsError = !initialPrint.Success;
        }
    }

    [RelayCommand]
    private async Task PrintAsync()
    {
        var result = await _receipt.PrintAsync(_sale);
        StatusMessage = result.Message;
        StatusIsError = !result.Success;
    }

    [RelayCommand]
    private void ExportPdf()
    {
        try
        {
            Directory.CreateDirectory(AppPaths.DocumentsDirectory);
            var path = _receipt.DefaultPdfPath(_sale.Invoice, AppPaths.DocumentsDirectory);
            _receipt.ExportPdf(_sale, path);
            StatusMessage = _loc.Format("reports.exported", path);
            StatusIsError = false;
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
            StatusIsError = true;
        }
    }

    [RelayCommand]
    private void Done() => Close(true);
}
