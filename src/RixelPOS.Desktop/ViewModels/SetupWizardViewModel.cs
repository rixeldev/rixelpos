using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RixelPOS.Application.Abstractions;
using RixelPOS.Application.Printing;
using RixelPOS.Application.Services;
using RixelPOS.Application.Setup;
using RixelPOS.Desktop.Localization;
using RixelPOS.Desktop.Services;
using RixelPOS.Domain.Enums;
using RixelPOS.Domain.Settings;

namespace RixelPOS.Desktop.ViewModels;

public sealed partial class SetupWizardViewModel : ViewModelBase
{
    private readonly SetupService _setup;
    private readonly SettingsService _settings;
    private readonly IDeviceDiscovery _devices;
    private readonly IReceiptPrinter _printer;
    private readonly ThemeService _theme;
    private readonly LocalizationService _loc;

    [ObservableProperty] private int _step;
    [ObservableProperty] private string _businessName = "";
    [ObservableProperty] private string _legalName = "";
    [ObservableProperty] private string _taxId = "";
    [ObservableProperty] private string _address = "";
    [ObservableProperty] private string _phone = "";
    [ObservableProperty] private string _currencyCode = "DOP";
    [ObservableProperty] private string _currencySymbol = "RD$";
    [ObservableProperty] private string _taxName = "ITBIS";
    [ObservableProperty] private string _taxPercent = "18";
    [ObservableProperty] private bool _pricesIncludeTax = true;
    [ObservableProperty] private string _invoicePrefix = "A";
    [ObservableProperty] private int _receiptWidthIndex;
    [ObservableProperty] private string _printerConnection = "";
    [ObservableProperty] private DeviceInfo? _selectedPrinter;
    [ObservableProperty] private string _adminFullName = "";
    [ObservableProperty] private string _adminUsername = "";
    [ObservableProperty] private string _adminPassword = "";
    [ObservableProperty] private string _adminPin = "";
    [ObservableProperty] private int _selectedLanguageIndex;
    [ObservableProperty] private int _selectedThemeIndex = 1;
    [ObservableProperty] private string _statusMessage = "";
    [ObservableProperty] private bool _statusIsError;
    [ObservableProperty] private bool _isDone;

    public SetupWizardViewModel(
        SetupService setup,
        SettingsService settings,
        IDeviceDiscovery devices,
        IReceiptPrinter printer,
        ThemeService theme,
        LocalizationService loc)
    {
        _setup = setup;
        _settings = settings;
        _devices = devices;
        _printer = printer;
        _theme = theme;
        _loc = loc;
        SelectedLanguageIndex = settings.Current.Language == AppLanguage.English ? 1 : 0;
        SelectedThemeIndex = settings.Current.Theme == AppTheme.Light ? 0 : 1;
    }

    public event EventHandler? Completed;

    public ObservableCollection<DeviceInfo> Printers { get; } = new();

    public IReadOnlyList<string> ReceiptWidths { get; } = new[] { "58 mm", "80 mm" };
    public IReadOnlyList<string> Languages { get; } = new[] { "Español", "English" };
    public IReadOnlyList<string> Themes { get; } = new[] { "Claro", "Oscuro" };

    public bool IsStep0 => Step == 0;
    public bool IsStep1 => Step == 1;
    public bool IsStep2 => Step == 2;
    public bool IsStep3 => Step == 3;
    public bool IsStep4 => Step == 4;
    public bool CanGoBack => Step is > 0 and < 4;
    public string StepLabel => $"{Step + 1} / 5";

    partial void OnStepChanged(int value)
    {
        OnPropertyChanged(nameof(IsStep0));
        OnPropertyChanged(nameof(IsStep1));
        OnPropertyChanged(nameof(IsStep2));
        OnPropertyChanged(nameof(IsStep3));
        OnPropertyChanged(nameof(IsStep4));
        OnPropertyChanged(nameof(CanGoBack));
        OnPropertyChanged(nameof(StepLabel));
        StatusMessage = "";
        StatusIsError = false;
    }

    partial void OnSelectedPrinterChanged(DeviceInfo? value)
    {
        if (value is not null)
        {
            PrinterConnection = value.Id;
        }
    }

    partial void OnSelectedLanguageIndexChanged(int value)
    {
        if (value == 1)
        {
            _loc.SetLanguage(AppLanguage.English);
        }
        else
        {
            _loc.SetLanguage(AppLanguage.Spanish);
        }
    }

    partial void OnSelectedThemeIndexChanged(int value)
    {
        _theme.Apply(value == 0 ? AppTheme.Light : AppTheme.Dark);
    }

    [RelayCommand]
    private void Next()
    {
        if (!ValidateStep())
        {
            return;
        }

        if (Step == 3)
        {
            Finish();
            return;
        }

        Step = Math.Min(4, Step + 1);
    }

    [RelayCommand]
    private void Back()
    {
        if (Step > 0)
        {
            Step--;
        }
    }

    [RelayCommand]
    private async Task DetectPrintersAsync()
    {
        try
        {
            IsBusy = true;
            var devices = await _devices.DiscoverPrintersAsync();
            Printers.Clear();
            foreach (var device in devices)
            {
                Printers.Add(device);
            }

            if (Printers.Count == 1)
            {
                SelectedPrinter = Printers[0];
            }

            StatusMessage = Printers.Count == 0
                ? _loc.Get("setup.noPrintersDetected")
                : $"{Printers.Count} impresora(s) encontrada(s).";
            StatusIsError = false;
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
            StatusIsError = true;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task TestPrinterAsync()
    {
        IsBusy = true;
        try
        {
            _settings.Current.PrinterConnection = PrinterConnection.Trim();
            var result = await _printer.PrintTestAsync();
            StatusMessage = result.Message;
            StatusIsError = !result.Success;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void Finish()
    {
        if (!ValidateStep())
        {
            return;
        }

        try
        {
            IsBusy = true;
            var request = new SetupRequest(
                BusinessName,
                LegalName,
                TaxId,
                Address,
                Phone,
                CurrencyCode,
                CurrencySymbol,
                TaxName,
                (int)Math.Round(ParseDecimal(TaxPercent, 18) * 100),
                PricesIncludeTax,
                InvoicePrefix,
                ReceiptWidthIndex == 0 ? ReceiptWidth.Mm58 : ReceiptWidth.Mm80,
                AdminUsername,
                AdminFullName,
                AdminPassword,
                AdminPin,
                SelectedLanguageIndex == 1 ? AppLanguage.English : AppLanguage.Spanish,
                SelectedThemeIndex == 1 ? AppTheme.Dark : AppTheme.Light,
                PrinterConnection.Trim());

            _setup.CompleteSetup(request);
            _loc.SetLanguage(request.Language);
            _theme.Apply(request.Theme);
            IsDone = true;
            Step = 4;
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
            StatusIsError = true;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void OpenApp() => Completed?.Invoke(this, EventArgs.Empty);

    private bool ValidateStep()
    {
        string? error = Step switch
        {
            0 when string.IsNullOrWhiteSpace(BusinessName) => _loc.Get("setup.required"),
            1 when string.IsNullOrWhiteSpace(CurrencySymbol) || string.IsNullOrWhiteSpace(TaxName) => _loc.Get("setup.required"),
            3 when string.IsNullOrWhiteSpace(AdminFullName) || string.IsNullOrWhiteSpace(AdminUsername) => _loc.Get("setup.required"),
            3 when AdminPassword.Length < 8 => _loc.Get("users.password"),
            3 when AdminPin.Length is < 4 or > 8 || !AdminPin.All(char.IsDigit) => _loc.Get("users.pin"),
            _ => null
        };

        if (error is not null)
        {
            StatusMessage = error;
            StatusIsError = true;
            return false;
        }

        StatusMessage = "";
        StatusIsError = false;
        return true;
    }

    private static decimal ParseDecimal(string text, decimal fallback) =>
        decimal.TryParse(text.Replace(',', '.'), System.Globalization.NumberStyles.Number,
            System.Globalization.CultureInfo.InvariantCulture, out var value)
            ? value
            : fallback;
}
