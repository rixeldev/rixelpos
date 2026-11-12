using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Avalonia;
using RixelPOS.Application.Abstractions;
using RixelPOS.Application.Printing;
using RixelPOS.Application.Services;
using RixelPOS.Application.Sessions;
using RixelPOS.Desktop.Localization;
using RixelPOS.Desktop.Services;
using RixelPOS.Domain.Common;
using RixelPOS.Domain.Entities;
using RixelPOS.Domain.Enums;
using RixelPOS.Domain.Settings;
using RixelPOS.Infrastructure;

namespace RixelPOS.Desktop.ViewModels;

public sealed class UserListItem
{
    public UserListItem(User user)
    {
        User = user;
        FullName = user.FullName;
        Username = user.Username;
        RoleText = user.Role switch
        {
            UserRole.Admin => LocalizationService.Instance.Get("users.admin"),
            UserRole.Manager => LocalizationService.Instance.Get("users.manager"),
            _ => LocalizationService.Instance.Get("users.cashier")
        };
        IsActive = user.IsActive;
        StatusText = IsActive ? LocalizationService.Instance.Get("common.active") : LocalizationService.Instance.Get("common.inactive");
    }

    public User User { get; }
    public string FullName { get; }
    public string Username { get; }
    public string RoleText { get; }
    public bool IsActive { get; }
    public string StatusText { get; }
}

public sealed class AuditItem
{
    public AuditItem(AuditEntry entry)
    {
        DateText = entry.CreatedAt.ToLocalTime().ToString("dd/MM/yyyy HH:mm:ss");
        UserName = entry.UserName;
        Action = entry.Action;
        EntityType = entry.EntityType;
        Details = entry.Details ?? "";
    }

    public string DateText { get; }
    public string UserName { get; }
    public string Action { get; }
    public string EntityType { get; }
    public string Details { get; }
}

public sealed partial class SettingsViewModel : ViewModelBase, IActivatable
{
    private readonly SettingsService _settings;
    private readonly AuthService _auth;
    private readonly DialogService _dialogs;
    private readonly FileDialogService _files;
    private readonly IDeviceDiscovery _devices;
    private readonly IReceiptPrinter _printer;
    private readonly ICustomerDisplay _display;
    private readonly IBackupService _backups;
    private readonly IPosStore _store;
    private readonly ThemeService _theme;
    private readonly LocalizationService _loc;
    private readonly AppSession _session;
    private readonly IAppLogger _logger;
    private readonly ClipboardService _clipboard;

    [ObservableProperty] private int _selectedTabIndex;
    [ObservableProperty] private string _businessName = "";
    [ObservableProperty] private string _legalName = "";
    [ObservableProperty] private string _taxId = "";
    [ObservableProperty] private string _address = "";
    [ObservableProperty] private string _phone = "";
    [ObservableProperty] private string _email = "";
    [ObservableProperty] private string _website = "";
    [ObservableProperty] private string _logoPath = "";
    [ObservableProperty] private string _currencyCode = "";
    [ObservableProperty] private string _currencySymbol = "";
    [ObservableProperty] private string _taxName = "";
    [ObservableProperty] private string _taxPercentText = "";
    [ObservableProperty] private bool _pricesIncludeTax;
    [ObservableProperty] private string _invoicePrefix = "";
    [ObservableProperty] private string _invoicePaddingText = "";
    [ObservableProperty] private int _fiscalModeIndex;
    [ObservableProperty] private string _fiscalRnc = "";
    [ObservableProperty] private string _ncfPrefix = "";
    [ObservableProperty] private int _receiptWidthIndex = 1;
    [ObservableProperty] private string _receiptFooter = "";
    [ObservableProperty] private bool _autoPrintReceipt;
    [ObservableProperty] private bool _previewReceiptBeforePrint;
    [ObservableProperty] private string _autoLockMinutesText = "";
    [ObservableProperty] private bool _showQrOnReceipt;
    [ObservableProperty] private string _printerConnection = "";
    [ObservableProperty] private string _printerName = "";
    [ObservableProperty] private bool _cashDrawerEnabled;
    [ObservableProperty] private bool _autoOpenDrawerOnCash;
    [ObservableProperty] private bool _customerDisplayEnabled;
    [ObservableProperty] private string _customerDisplayConnection = "";
    [ObservableProperty] private string _scannerConnection = "";
    [ObservableProperty] private string _scannerDebounceText = "";
    [ObservableProperty] private bool _allowNegativeStock;
    [ObservableProperty] private string _lowStockThresholdText = "";
    [ObservableProperty] private string _discountApprovalText = "";
    [ObservableProperty] private bool _requireManagerForVoids;
    [ObservableProperty] private bool _requireManagerForRefunds;
    [ObservableProperty] private bool _storeCreditEnabled;
    [ObservableProperty] private bool _creditSalesEnabled;
    [ObservableProperty] private int _themeIndex = 1;
    [ObservableProperty] private int _languageIndex;
    [ObservableProperty] private string _uiScaleText = "100";
    [ObservableProperty] private string _backupFolder = "";
    [ObservableProperty] private bool _autoBackupOnClose;
    [ObservableProperty] private string _backupKeepText = "";
    [ObservableProperty] private DeviceInfo? _selectedPrinter;
    [ObservableProperty] private UserListItem? _selectedUser;
    [ObservableProperty] private BackupInfo? _selectedBackup;
    [ObservableProperty] private string _statusMessage = "";
    [ObservableProperty] private bool _statusIsError;
    [ObservableProperty] private bool _hasStatus;
    [ObservableProperty] private string _versionText = "";

    public SettingsViewModel(
        SettingsService settings,
        AuthService auth,
        DialogService dialogs,
        FileDialogService files,
        IDeviceDiscovery devices,
        IReceiptPrinter printer,
        ICustomerDisplay display,
        IBackupService backups,
        IPosStore store,
        ThemeService theme,
        LocalizationService loc,
        AppSession session,
        IAppLogger logger,
        ClipboardService clipboard)
    {
        _settings = settings;
        _auth = auth;
        _dialogs = dialogs;
        _files = files;
        _devices = devices;
        _printer = printer;
        _display = display;
        _backups = backups;
        _store = store;
        _theme = theme;
        _loc = loc;
        _session = session;
        _logger = logger;
        _clipboard = clipboard;
        VersionText = $"{Branding.ProductName} {Branding.Version} · {Branding.Company}";
        Load();
    }


    public ObservableCollection<DeviceInfo> Printers { get; } = new();
    public ObservableCollection<UserListItem> Users { get; } = new();
    public ObservableCollection<BackupInfo> BackupList { get; } = new();
    public ObservableCollection<AuditItem> AuditItems { get; } = new();

    public string[] ReceiptWidths => new[] { "58 mm", "80 mm" };
    public string[] FiscalModes => new[] { _loc.Get("settings.none"), _loc.Get("settings.dominicanNcf") };
    public string[] Themes => new[] { _loc.Get("settings.light"), _loc.Get("settings.dark"), _loc.Get("settings.system") };
    public string[] Languages => new[] { "Español", "English" };
    public string DataFolder => AppPaths.DataDirectory;
    public string LogFolder => AppPaths.LogsDirectory;
    public string SupportEmail => Branding.SupportEmail;
    public string WebsiteUrl => Branding.Website;
    public string LicenseName => _loc.Get("settings.licenseName");
    public string LicenseText => _loc.Get("settings.licenseText");

    [RelayCommand]
    private async Task EmailSupportAsync()
    {
        try
        {
            OpenUrl($"mailto:{Branding.SupportEmail}");
        }
        catch
        {
            await CopyEmailAsync();
        }
    }

    [RelayCommand]
    private async Task OpenWebsiteAsync()
    {
        try
        {
            OpenUrl(Branding.Website);
        }
        catch (Exception ex)
        {
            ReportError(ex);
        }
    }

    private static void OpenUrl(string url)
    {
        if (OperatingSystem.IsWindows())
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        else if (OperatingSystem.IsMacOS())
        {
            Process.Start("open", url);
        }
        else
        {
            Process.Start("xdg-open", url);
        }
    }

    [RelayCommand]
    private async Task CopyEmailAsync()
    {
        var copied = await _clipboard.SetTextAsync(Branding.SupportEmail);
        StatusMessage = copied ? _loc.Get("settings.emailCopied") : Branding.SupportEmail;
        StatusIsError = !copied;
        HasStatus = true;
    }

    public void Activate()
    {
        Load();
        RefreshUsers();
        RefreshBackups();
        RefreshAudit();
    }

    partial void OnSelectedPrinterChanged(DeviceInfo? value)
    {
        if (value is not null)
        {
            PrinterConnection = value.Id;
        }
    }

    partial void OnSelectedTabIndexChanged(int value)
    {
        if (value == 3)
        {
            RefreshUsers();
        }
        else if (value == 4)
        {
            RefreshBackups();
        }
        else if (value == 6)
        {
            RefreshAudit();
        }
    }

    [RelayCommand]
    private void Save()
    {
        ErrorMessage = null;
        try
        {
            var s = _settings.Current.Clone();
            s.BusinessName = BusinessName.Trim();
            s.LegalName = LegalName.Trim();
            s.TaxId = TaxId.Trim();
            s.Address = Address.Trim();
            s.Phone = Phone.Trim();
            s.Email = Email.Trim();
            s.Website = Website.Trim();
            s.LogoPath = LogoPath.Trim();
            s.CurrencyCode = CurrencyCode.Trim().ToUpperInvariant();
            s.CurrencySymbol = CurrencySymbol.Trim();
            s.TaxName = TaxName.Trim();
            s.DefaultTaxRateBasisPoints = (int)Math.Round(ParseDecimal(TaxPercentText, 0) * 100);
            s.PricesIncludeTax = true;
            s.InvoicePrefix = InvoicePrefix.Trim();
            s.InvoicePadding = (int)Math.Round(ParseDecimal(InvoicePaddingText, 6));
            s.FiscalMode = FiscalModeIndex == 1 ? FiscalMode.DominicanNcf : FiscalMode.None;
            s.FiscalRnc = FiscalRnc.Trim();
            s.NcfPrefix = NcfPrefix.Trim();
            s.ReceiptWidth = ReceiptWidthIndex == 0 ? ReceiptWidth.Mm58 : ReceiptWidth.Mm80;
            s.ReceiptFooter = ReceiptFooter.Trim();
            s.AutoPrintReceipt = AutoPrintReceipt;
            s.PreviewReceiptBeforePrint = PreviewReceiptBeforePrint;
            s.ShowQrOnReceipt = ShowQrOnReceipt;
            s.PrinterConnection = PrinterConnection.Trim();
            s.PrinterName = PrinterName.Trim();
            s.CashDrawerEnabled = CashDrawerEnabled;
            s.AutoOpenDrawerOnCash = AutoOpenDrawerOnCash;
            s.CustomerDisplayEnabled = CustomerDisplayEnabled;
            s.CustomerDisplayConnection = CustomerDisplayConnection.Trim();
            s.ScannerConnection = ScannerConnection.Trim();
            s.ScannerDebounceMs = (int)Math.Round(ParseDecimal(ScannerDebounceText, 350));
            s.AllowNegativeStock = AllowNegativeStock;
            s.LowStockThresholdMilli = (int)Math.Round(ParseDecimal(LowStockThresholdText, 5) * 1000);
            s.DiscountApprovalPercent = (int)Math.Round(ParseDecimal(DiscountApprovalText, 20));
            s.RequireManagerForVoids = RequireManagerForVoids;
            s.RequireManagerForRefunds = RequireManagerForRefunds;
            s.AutoLockMinutes = (int)Math.Round(ParseDecimal(AutoLockMinutesText, 5));
            s.StoreCreditEnabled = StoreCreditEnabled;
            s.CreditSalesEnabled = CreditSalesEnabled;
            s.Theme = ThemeIndex switch { 0 => AppTheme.Light, 1 => AppTheme.Dark, _ => AppTheme.System };
            s.Language = LanguageIndex == 1 ? AppLanguage.English : AppLanguage.Spanish;
            s.UiScalePercent = (int)Math.Round(ParseDecimal(UiScaleText, 100));
            s.BackupFolder = BackupFolder.Trim();
            s.AutoBackupOnClose = AutoBackupOnClose;
            s.BackupKeepCount = (int)Math.Round(ParseDecimal(BackupKeepText, 14));

            _settings.Save(s);
            _theme.Apply(s.Theme);
            _theme.ApplyUiScale(s.UiScalePercent);
            if (_loc.Language != s.Language)
            {
                StatusMessage = _loc.Get("settings.restartLanguage");
            }
            else
            {
                StatusMessage = _loc.Get("settings.saved");
            }

            StatusIsError = false;
            HasStatus = true;
            Load();
        }
        catch (Exception ex)
        {
            ReportError(ex);
            StatusIsError = true;
            HasStatus = true;
        }
    }

    [RelayCommand]
    private async Task DetectPrintersAsync()
    {
        IsBusy = true;
        try
        {
            var devices = await _devices.DiscoverPrintersAsync();
            Printers.Clear();
            foreach (var device in devices)
            {
                Printers.Add(device);
            }

            SelectedPrinter = Printers.FirstOrDefault();
            StatusMessage = Printers.Count == 0
                ? _loc.Get("settings.noPrintersDetected")
                : $"{Printers.Count} dispositivo(s) encontrado(s).";
            StatusIsError = false;
            HasStatus = true;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task TestPrintAsync()
    {
        _settings.Current.PrinterConnection = PrinterConnection.Trim();
        var result = await _printer.PrintTestAsync();
        StatusMessage = result.Message;
        StatusIsError = !result.Success;
        HasStatus = true;
    }

    [RelayCommand]
    private async Task TestDrawerAsync()
    {
        _settings.Current.PrinterConnection = PrinterConnection.Trim();
        var result = await _printer.OpenCashDrawerAsync();
        StatusMessage = result.Message;
        StatusIsError = !result.Success;
        HasStatus = true;
    }

    [RelayCommand]
    private async Task TestDisplayAsync()
    {
        _settings.Current.CustomerDisplayConnection = CustomerDisplayConnection.Trim();
        _settings.Current.CustomerDisplayEnabled = CustomerDisplayEnabled;
        await _display.ShowLineAsync($"{BusinessName}  {_settings.FormatMoney(0)}");
        StatusMessage = "Mensaje enviado a la pantalla del cliente.";
        StatusIsError = false;
        HasStatus = true;
    }

    [RelayCommand]
    private async Task PickLogoAsync()
    {
        var path = await _files.PickOpenAsync(_loc.Get("settings.chooseLogo"), "Imagen", "png", "jpg", "jpeg");
        if (path is not null)
        {
            LogoPath = path;
        }
    }

    [RelayCommand]
    private async Task PickBackupFolderAsync()
    {
        var path = await _files.PickFolderAsync(_loc.Get("settings.chooseFolder"));
        if (path is not null)
        {
            BackupFolder = path;
        }
    }

    [RelayCommand]
    private async Task CreateBackupAsync()
    {
        try
        {
            _settings.Current.BackupFolder = BackupFolder.Trim();
            var backup = _backups.CreateBackup("manual");
            RefreshBackups();
            StatusMessage = $"{_loc.Get("settings.backupCreated")}: {backup.Path}";
            StatusIsError = false;
            HasStatus = true;
        }
        catch (Exception ex)
        {
            ReportError(ex);
            StatusIsError = true;
            HasStatus = true;
        }
    }

    [RelayCommand]
    private async Task RestoreBackupAsync()
    {
        if (SelectedBackup is null)
        {
            StatusMessage = _loc.Get("toast.selectRow");
            StatusIsError = true;
            HasStatus = true;
            return;
        }

        if (!await _dialogs.ConfirmAsync(_loc.Get("settings.restoreBackup"),
                _loc.Get("settings.restoreWarning"), _loc.Get("dialogs.restore"), danger: true))
        {
            return;
        }

        try
        {
            _backups.Restore(SelectedBackup.Path);
            StatusMessage = _loc.Get("settings.restoreDone");
            StatusIsError = false;
            HasStatus = true;
        }
        catch (Exception ex)
        {
            ReportError(ex);
            StatusIsError = true;
            HasStatus = true;
        }
    }

    [RelayCommand]
    private void RefreshBackups()
    {
        BackupList.Clear();
        foreach (var backup in _backups.ListBackups())
        {
            BackupList.Add(backup);
        }

        SelectedBackup = BackupList.FirstOrDefault();
    }

    [RelayCommand]
    private void RefreshUsers()
    {
        Users.Clear();
        foreach (var user in _store.GetUsers())
        {
            Users.Add(new UserListItem(user));
        }
    }

    [RelayCommand]
    private void RefreshAudit()
    {
        AuditItems.Clear();
        foreach (var entry in _store.GetAuditEntries(300))
        {
            AuditItems.Add(new AuditItem(entry));
        }
    }

    [RelayCommand]
    private async Task AddUserAsync()
    {
        if (await _dialogs.EditUserAsync(null))
        {
            RefreshUsers();
            StatusMessage = _loc.Get("users.saved");
            StatusIsError = false;
            HasStatus = true;
        }
    }

    [RelayCommand]
    private async Task EditUserAsync(UserListItem? item)
    {
        item ??= SelectedUser;
        if (item is null)
        {
            return;
        }

        if (await _dialogs.EditUserAsync(item.User))
        {
            RefreshUsers();
            StatusMessage = _loc.Get("users.saved");
            StatusIsError = false;
            HasStatus = true;
        }
    }

    [RelayCommand]
    private async Task ResetPinAsync(UserListItem? item)
    {
        item ??= SelectedUser;
        if (item is null)
        {
            return;
        }

        var pin = await _dialogs.AskTextAsync(_loc.Get("settings.resetPin"), _loc.Get("users.pin"), "");
        if (string.IsNullOrWhiteSpace(pin))
        {
            return;
        }

        try
        {
            _auth.SetPin(item.User.Id, pin.Trim());
            StatusMessage = _loc.Get("toast.saved");
            StatusIsError = false;
            HasStatus = true;
        }
        catch (Exception ex)
        {
            ReportError(ex);
            StatusIsError = true;
            HasStatus = true;
        }
    }

    [RelayCommand]
    private async Task ToggleUserActiveAsync(UserListItem? item)
    {
        item ??= SelectedUser;
        if (item is null)
        {
            return;
        }

        try
        {
            _auth.SetActive(item.User.Id, !item.User.IsActive);
            RefreshUsers();
            StatusMessage = _loc.Get("toast.saved");
            StatusIsError = false;
            HasStatus = true;
        }
        catch (Exception ex)
        {
            ReportError(ex);
            StatusIsError = true;
            HasStatus = true;
        }
    }

    [RelayCommand]
    private void OpenBackupFolder()
    {
        try
        {
            var folder = string.IsNullOrWhiteSpace(BackupFolder) ? AppPaths.BackupsDirectory : BackupFolder;
            Directory.CreateDirectory(folder);
            if (OperatingSystem.IsWindows())
            {
                Process.Start(new ProcessStartInfo("explorer.exe", folder) { UseShellExecute = true });
            }
            else if (OperatingSystem.IsMacOS())
            {
                Process.Start("open", folder);
            }
            else
            {
                Process.Start("xdg-open", folder);
            }
        }
        catch (Exception ex)
        {
            _logger.Warn($"Could not open folder: {ex.Message}");
        }
    }

    private void Load()
    {
        var s = _settings.Current;
        BusinessName = s.BusinessName;
        LegalName = s.LegalName;
        TaxId = s.TaxId;
        Address = s.Address;
        Phone = s.Phone;
        Email = s.Email;
        Website = s.Website;
        LogoPath = s.LogoPath;
        CurrencyCode = s.CurrencyCode;
        CurrencySymbol = s.CurrencySymbol;
        TaxName = s.TaxName;
        TaxPercentText = (s.DefaultTaxRateBasisPoints / 100m).ToString("0.##", CultureInfo.InvariantCulture);
        PricesIncludeTax = s.PricesIncludeTax;
        InvoicePrefix = s.InvoicePrefix;
        InvoicePaddingText = s.InvoicePadding.ToString();
        FiscalModeIndex = s.FiscalMode == FiscalMode.DominicanNcf ? 1 : 0;
        FiscalRnc = s.FiscalRnc;
        NcfPrefix = s.NcfPrefix;
        ReceiptWidthIndex = s.ReceiptWidth == ReceiptWidth.Mm58 ? 0 : 1;
        ReceiptFooter = s.ReceiptFooter;
        AutoPrintReceipt = s.AutoPrintReceipt;
        PreviewReceiptBeforePrint = s.PreviewReceiptBeforePrint;
        AutoLockMinutesText = s.AutoLockMinutes.ToString();
        ShowQrOnReceipt = s.ShowQrOnReceipt;
        PrinterConnection = s.PrinterConnection;
        PrinterName = s.PrinterName;
        CashDrawerEnabled = s.CashDrawerEnabled;
        AutoOpenDrawerOnCash = s.AutoOpenDrawerOnCash;
        CustomerDisplayEnabled = s.CustomerDisplayEnabled;
        CustomerDisplayConnection = s.CustomerDisplayConnection;
        ScannerConnection = s.ScannerConnection;
        ScannerDebounceText = s.ScannerDebounceMs.ToString();
        AllowNegativeStock = s.AllowNegativeStock;
        LowStockThresholdText = (s.LowStockThresholdMilli / 1000m).ToString("0.###", CultureInfo.InvariantCulture);
        DiscountApprovalText = s.DiscountApprovalPercent.ToString();
        RequireManagerForVoids = s.RequireManagerForVoids;
        RequireManagerForRefunds = s.RequireManagerForRefunds;
        StoreCreditEnabled = s.StoreCreditEnabled;
        CreditSalesEnabled = s.CreditSalesEnabled;
        ThemeIndex = s.Theme switch { AppTheme.Light => 0, AppTheme.Dark => 1, _ => 2 };
        LanguageIndex = s.Language == AppLanguage.English ? 1 : 0;
        UiScaleText = s.UiScalePercent.ToString();
        BackupFolder = s.BackupFolder;
        AutoBackupOnClose = s.AutoBackupOnClose;
        BackupKeepText = s.BackupKeepCount.ToString();
    }

    private static decimal ParseDecimal(string text, decimal fallback) =>
        decimal.TryParse(text.Replace(',', '.'), NumberStyles.Number, CultureInfo.InvariantCulture, out var value)
            ? value
            : fallback;
}
