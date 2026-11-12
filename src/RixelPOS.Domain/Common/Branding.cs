namespace RixelPOS.Domain.Common;

/// <summary>Central place for the RixelDev branding. Change the product name here only.</summary>
public static class Branding
{
    public const string Company = "RixelDev";
    public const string ProductName = "RixelPOS";
    public const string ProductCode = "rixelpos";
    public const string Tagline = "Punto de venta rápido, simple y confiable";
    public const string Website = "https://rixel.dev";
    public const string SupportEmail = "support@rixel.dev";
    public const string DefaultCurrencyCode = "DOP";
    public const string DefaultCurrencySymbol = "RD$";
    public const string DefaultLocale = "es-DO";
    public static string Version =>
        typeof(Branding).Assembly.GetName().Version?.ToString(3) ?? "1.0.0";
}

public static class Defaults
{
    public const int InvoicePadding = 6;
    public const int ScannerDebounceMs = 350;
    public const int DiscountApprovalPercent = 20;
    public const int DefaultTaxRateBasisPoints = 0;
    public const int BackupKeepCount = 14;
    public const int LowStockDefaultMilli = 5_000;
    public const int ReceiptWidthMm = 80;
    public const string InvoicePrefix = "A";
}
