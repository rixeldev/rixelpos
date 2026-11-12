using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RixelPOS.Application.Customers;
using RixelPOS.Application.Services;
using RixelPOS.Desktop.Localization;
using RixelPOS.Domain.Common;
using RixelPOS.Domain.Entities;
using RixelPOS.Domain.Enums;
using RixelPOS.Domain.Reporting;

namespace RixelPOS.Desktop.ViewModels;

public sealed partial class MessageDialogViewModel : DialogViewModelBase
{
    public MessageDialogViewModel(string title, string message, bool isConfirm, string okText, string cancelText, bool danger)
    {
        Title = title;
        Message = message;
        IsConfirm = isConfirm;
        OkText = okText;
        CancelText = cancelText;
        IsDanger = danger;
    }

    public string Title { get; }
    public string Message { get; }
    public bool IsConfirm { get; }
    public string OkText { get; }
    public string CancelText { get; }
    public bool IsDanger { get; }

    [RelayCommand]
    private void Ok() => Close(true);

    [RelayCommand]
    private void Cancel() => Close(false);
}

public sealed partial class ValueDialogViewModel : DialogViewModelBase
{
    [ObservableProperty] private string _title = "";
    [ObservableProperty] private string _label = "";
    [ObservableProperty] private string _valueText = "";
    [ObservableProperty] private string _hint = "";
    [ObservableProperty] private bool _isNumeric = true;
    [ObservableProperty] private bool _isInteger;

    public ValueDialogViewModel(string title, string label, string? initial, bool numeric, string? hint = null, bool integer = false)
    {
        Title = title;
        Label = label;
        ValueText = initial ?? "";
        IsNumeric = numeric;
        Hint = hint ?? "";
        IsInteger = integer;
    }

    public decimal? NumberResult { get; private set; }
    public string? TextResult { get; private set; }

    [RelayCommand]
    private void Confirm()
    {
        ErrorMessage = null;
        if (!IsNumeric)
        {
            TextResult = ValueText.Trim();
            Close(true);
            return;
        }

        var text = ValueText.Replace(',', '.').Trim();
        if (!decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var value) || value < 0)
        {
            ErrorMessage = LocalizationService.Instance.Get("dialogs.invalidValue");
            return;
        }

        NumberResult = IsInteger ? Math.Round(value, 0) : value;
        Close(true);
    }

    [RelayCommand]
    private void Cancel() => Close(false);
}

public sealed partial class DiscountDialogViewModel : DialogViewModelBase
{
    private readonly long _baseCents;
    private readonly string _currencySymbol;

    [ObservableProperty] private string _title = "";
    [ObservableProperty] private int _typeIndex;
    [ObservableProperty] private string _valueText = "";
    [ObservableProperty] private string _previewText = "";
    [ObservableProperty] private string _baseText = "";

    public DiscountDialogViewModel(string title, long baseCents, string currencySymbol)
    {
        _baseCents = baseCents;
        _currencySymbol = currencySymbol;
        Title = title;
        BaseText = $"{currencySymbol} {baseCents / 100m:N2}";
    }

    public Discount? Result { get; private set; }

    public bool IsPercent => TypeIndex == 0;

    partial void OnTypeIndexChanged(int value)
    {
        OnPropertyChanged(nameof(IsPercent));
        ValueText = "";
        PreviewText = "";
    }

    partial void OnValueTextChanged(string value) => UpdatePreview();

    [RelayCommand]
    private void Confirm()
    {
        ErrorMessage = null;
        var text = ValueText.Replace(',', '.').Trim();
        if (!decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var value) || value < 0)
        {
            ErrorMessage = LocalizationService.Instance.Get("dialogs.invalidDiscount");
            return;
        }

        if (IsPercent)
        {
            if (value > 100)
            {
                ErrorMessage = LocalizationService.Instance.Get("dialogs.discountMaxPercent");
                return;
            }

            Result = Discount.Percent(value);
        }
        else
        {
            var cents = (long)Math.Round(value * 100m, MidpointRounding.AwayFromZero);
            if (cents > _baseCents)
            {
                ErrorMessage = LocalizationService.Instance.Get("dialogs.discountMaxTotal");
                return;
            }

            Result = Discount.Fixed(Money.FromCents(cents));
        }

        Close(true);
    }

    [RelayCommand]
    private void Cancel() => Close(false);

    [RelayCommand]
    private void Clear()
    {
        Result = Discount.None;
        Close(true);
    }

    private void UpdatePreview()
    {
        if (!decimal.TryParse(ValueText.Replace(',', '.'), NumberStyles.Number, CultureInfo.InvariantCulture, out var value) || value < 0)
        {
            PreviewText = "";
            return;
        }

        var discount = IsPercent
            ? Discount.Percent(value)
            : Discount.Fixed(Money.FromAmount(value));
        var cents = discount.ComputeCents(_baseCents);
        PreviewText = $"- {_currencySymbol} {cents / 100m:N2}";
    }
}

public sealed partial class ManagerApprovalViewModel : DialogViewModelBase
{
    private readonly AuthService _auth;
    private readonly Permission _permission;

    [ObservableProperty] private string _reason = "";
    [ObservableProperty] private string _pinEntry = "";
    [ObservableProperty] private string _pinDots = "";

    public ManagerApprovalViewModel(AuthService auth, Permission permission, string reason)
    {
        _auth = auth;
        _permission = permission;
        Reason = reason;
    }

    partial void OnPinEntryChanged(string value)
    {
        if (value.Length > 8)
        {
            PinEntry = value[..8];
            return;
        }

        PinDots = string.Join(" ", Enumerable.Repeat("●", PinEntry.Length));
        ErrorMessage = null;
    }

    [RelayCommand]
    private void AddDigit(string? digit)
    {
        if (!string.IsNullOrEmpty(digit) && PinEntry.Length < 8)
        {
            PinEntry += digit;
        }
    }

    [RelayCommand]
    private void Backspace()
    {
        if (PinEntry.Length > 0)
        {
            PinEntry = PinEntry[..^1];
        }
    }

    [RelayCommand]
    private void Approve()
    {
        if (PinEntry.Length < 4)
        {
            ErrorMessage = LocalizationService.Instance.Get("dialogs.invalidPin");
            return;
        }

        if (_auth.ApproveManager(PinEntry, _permission))
        {
            Close(true);
        }
        else
        {
            ErrorMessage = LocalizationService.Instance.Get("dialogs.invalidPin");
            PinEntry = "";
        }
    }

    [RelayCommand]
    private void Cancel() => Close(false);
}

public sealed partial class CustomerPickerViewModel : DialogViewModelBase
{
    private readonly CustomerService _customers;

    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private Customer? _selectedCustomer;

    public CustomerPickerViewModel(CustomerService customers)
    {
        _customers = customers;
        Load();
    }

    public ObservableCollection<Customer> Customers { get; } = new();

    partial void OnSearchTextChanged(string value) => Load();

    public Customer? Result { get; private set; }

    [RelayCommand]
    private void Confirm()
    {
        Result = SelectedCustomer;
        Close(SelectedCustomer is not null);
    }

    [RelayCommand]
    private void Cancel() => Close(false);

    [RelayCommand]
    private void WalkIn()
    {
        Result = null;
        Close(false);
    }

    private void Load()
    {
        Customers.Clear();
        foreach (var customer in _customers.Search(string.IsNullOrWhiteSpace(SearchText) ? null : SearchText).Take(200))
        {
            Customers.Add(customer);
        }
    }
}

public sealed partial class HeldSalesViewModel : DialogViewModelBase
{
    [ObservableProperty] private HeldSaleItem? _selectedItem;

    public HeldSalesViewModel(IEnumerable<HeldSaleItem> items)
    {
        foreach (var item in items)
        {
            Items.Add(item);
        }

        SelectedItem = Items.FirstOrDefault();
    }

    public ObservableCollection<HeldSaleItem> Items { get; } = new();

    public bool HasItems => Items.Count > 0;

    public HeldSaleItem? Result { get; private set; }

    [RelayCommand]
    private void Resume()
    {
        if (SelectedItem is null)
        {
            return;
        }

        Result = SelectedItem;
        Close(true);
    }

    [RelayCommand]
    private void Cancel() => Close(false);
}

public sealed partial class ZReportViewModel : DialogViewModelBase
{
    public ZReportViewModel(ZReport report, SettingsService settings)
    {
        Report = report;
        OpenedText = report.Session.OpenedAt.ToLocalTime().ToString("dd/MM/yyyy HH:mm");
        ClosedText = report.Session.ClosedAt?.ToLocalTime().ToString("dd/MM/yyyy HH:mm") ?? "—";
        OpeningText = settings.FormatMoney(report.Session.OpeningCents);
        ExpectedText = settings.FormatMoney(report.ExpectedCashCents);
        CountedText = settings.FormatMoney(report.Session.ClosingCents ?? 0);
        DifferenceText = settings.FormatMoney(report.Session.DifferenceCents ?? 0);
        GrossText = settings.FormatMoney(report.GrossSalesCents);
        CashText = settings.FormatMoney(report.CashSalesCents);
        SalesCountText = report.SalesCount.ToString();
        IsBalanced = report.Session.DifferenceCents is null or 0;

        foreach (var payment in report.Payments)
        {
            Payments.Add(new PaymentTotalItem(PaymentName(payment.Method), settings.FormatMoney(payment.TotalCents), payment.Count));
        }

        foreach (var movement in report.Movements)
        {
            Movements.Add(new MovementItem(movement, settings.FormatMoney(movement.AmountCents)));
        }
    }

    public ZReport Report { get; }
    public string OpenedText { get; }
    public string ClosedText { get; }
    public string OpeningText { get; }
    public string ExpectedText { get; }
    public string CountedText { get; }
    public string DifferenceText { get; }
    public string GrossText { get; }
    public string CashText { get; }
    public string SalesCountText { get; }
    public bool IsBalanced { get; }
    public ObservableCollection<PaymentTotalItem> Payments { get; } = new();
    public ObservableCollection<MovementItem> Movements { get; } = new();

    public bool HasMovements => Movements.Count > 0;

    [RelayCommand]
    private void CloseReport() => Close(true);

    private static string PaymentName(PaymentMethod method) => method switch
    {
        PaymentMethod.Cash => LocalizationService.Instance.Get("checkout.cash"),
        PaymentMethod.Card => LocalizationService.Instance.Get("checkout.card"),
        PaymentMethod.Transfer => LocalizationService.Instance.Get("checkout.transfer"),
        PaymentMethod.StoreCredit => LocalizationService.Instance.Get("checkout.storeCredit"),
        PaymentMethod.Credit => LocalizationService.Instance.Get("checkout.credit"),
        _ => method.ToString()
    };
}

public sealed class PaymentTotalItem
{
    public PaymentTotalItem(string method, string totalText, int count)
    {
        Method = method;
        TotalText = totalText;
        Count = count;
    }

    public string Method { get; }
    public string TotalText { get; }
    public int Count { get; }
}
