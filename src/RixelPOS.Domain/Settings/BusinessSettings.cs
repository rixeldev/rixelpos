using RixelPOS.Domain.Common;
using RixelPOS.Domain.Enums;

namespace RixelPOS.Domain.Settings;

/// <summary>All user-configurable business settings. Persisted as key/value rows.</summary>
public sealed class BusinessSettings
{
    public string BusinessName { get; set; } = "";
    public string LegalName { get; set; } = "";
    public string TaxId { get; set; } = "";
    public string Address { get; set; } = "";
    public string Phone { get; set; } = "";
    public string Email { get; set; } = "";
    public string Website { get; set; } = Branding.Website;
    public string LogoPath { get; set; } = "";

    public string CurrencyCode { get; set; } = Branding.DefaultCurrencyCode;
    public string CurrencySymbol { get; set; } = Branding.DefaultCurrencySymbol;
    public string Locale { get; set; } = Branding.DefaultLocale;

    public string TaxName { get; set; } = "ITBIS";
    public int DefaultTaxRateBasisPoints { get; set; } = Defaults.DefaultTaxRateBasisPoints;

    /// <summary>
    /// Sale prices already contain tax. The tax is extracted from the entered price
    /// instead of being added on top of the total, which is how most small businesses
    /// price their products.
    /// </summary>
    public bool PricesIncludeTax { get; set; } = true;

    public string InvoicePrefix { get; set; } = Defaults.InvoicePrefix;
    public int InvoicePadding { get; set; } = Defaults.InvoicePadding;
    public string ReceiptFooter { get; set; } = "¡Gracias por su compra!";
    public bool ShowQrOnReceipt { get; set; } = true;
    public ReceiptWidth ReceiptWidth { get; set; } = ReceiptWidth.Mm80;
    public bool AutoPrintReceipt { get; set; } = true;
    public bool PreviewReceiptBeforePrint { get; set; } = true;

    public string PrinterConnection { get; set; } = "";
    public string PrinterName { get; set; } = "";
    public bool CashDrawerEnabled { get; set; } = true;
    public bool AutoOpenDrawerOnCash { get; set; } = true;
    public bool CustomerDisplayEnabled { get; set; }
    public string CustomerDisplayConnection { get; set; } = "";

    public int ScannerDebounceMs { get; set; } = Defaults.ScannerDebounceMs;
    public string ScannerConnection { get; set; } = "";
    public bool AllowNegativeStock { get; set; }
    public int LowStockThresholdMilli { get; set; } = Defaults.LowStockDefaultMilli;
    public int DiscountApprovalPercent { get; set; } = Defaults.DiscountApprovalPercent;
    public bool RequireManagerForVoids { get; set; } = true;
    public bool RequireManagerForRefunds { get; set; } = true;

    /// <summary>Minutes of inactivity before the POS locks itself. 0 disables the lock.</summary>
    public int AutoLockMinutes { get; set; } = 5;

    public AppLanguage Language { get; set; } = AppLanguage.Spanish;
    public AppTheme Theme { get; set; } = AppTheme.Dark;
    public int UiScalePercent { get; set; } = 100;

    public string BackupFolder { get; set; } = "";
    public bool AutoBackupOnClose { get; set; } = true;
    public int BackupKeepCount { get; set; } = Defaults.BackupKeepCount;

    public FiscalMode FiscalMode { get; set; } = FiscalMode.None;
    public string FiscalRnc { get; set; } = "";
    public string NcfPrefix { get; set; } = "B02";

    public bool StoreCreditEnabled { get; set; } = true;
    public bool CreditSalesEnabled { get; set; }

    public bool Configured { get; set; }
    public bool OnboardingCompleted { get; set; }

    public decimal DefaultTaxPercent => DefaultTaxRateBasisPoints / 100m;

    public string FormatMoney(long cents) =>
        string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{CurrencySymbol} {cents / 100m:N2}");

    public BusinessSettings Clone() => (BusinessSettings)MemberwiseClone();
}
