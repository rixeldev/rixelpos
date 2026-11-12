using RixelPOS.Domain.Entities;
using RixelPOS.Domain.Enums;
using RixelPOS.Domain.Reporting;

namespace RixelPOS.Application.Printing;

public sealed record ReceiptBusiness(
    string Name,
    string LegalName,
    string TaxId,
    string Address,
    string Phone,
    string Email,
    string LogoPath);

public sealed record ReceiptData(
    ReceiptBusiness Business,
    Sale Sale,
    Invoice? Invoice,
    string CashierName,
    string CurrencySymbol,
    string TaxName,
    string Footer,
    bool ShowQr,
    string? QrPayload,
    ReceiptWidth Width,
    string? CustomerTaxId = null,
    string? CustomerAddress = null);

public sealed record PrintResult(bool Success, string Message, string? ErrorCode = null)
{
    public static PrintResult Ok(string message = "OK") => new(true, message);
    public static PrintResult Fail(string message, string? code = null) => new(false, message, code);
}

public sealed record DeviceInfo(string Id, string Name, string Kind, string? Detail = null);

public sealed record ScanEventArgs(string Code, DateTime At);

public sealed record LabelData(
    string ProductName,
    string Sku,
    string? Barcode,
    string PriceText,
    string BusinessName,
    string CurrencySymbol);

public interface IReceiptPrinter
{
    bool IsConfigured { get; }
    string Description { get; }
    Task<PrintResult> PrintAsync(ReceiptData data, CancellationToken cancellationToken = default);
    Task<PrintResult> PrintTestAsync(CancellationToken cancellationToken = default);
    Task<PrintResult> OpenCashDrawerAsync(CancellationToken cancellationToken = default);
}

public interface ILabelPrinter
{
    Task<PrintResult> PrintLabelAsync(LabelData label, CancellationToken cancellationToken = default);
}

public interface IDeviceDiscovery
{
    Task<IReadOnlyList<DeviceInfo>> DiscoverPrintersAsync(CancellationToken cancellationToken = default);
    IReadOnlyList<string> DiscoverSerialPorts();
    Task<PrintResult> TestConnectionAsync(string connectionString, CancellationToken cancellationToken = default);
}

/// <summary>Optional serial/HID scanner listener. Keyboard-wedge scanners need no listener.</summary>
public interface IScannerListener
{
    event EventHandler<ScanEventArgs>? Scanned;
    bool IsRunning { get; }
    Task StartAsync(string connectionString, CancellationToken cancellationToken = default);
    Task StopAsync();
}

public interface ICustomerDisplay
{
    bool IsConfigured { get; }
    Task ShowLineAsync(string text, CancellationToken cancellationToken = default);
}

public interface IPdfService
{
    string GenerateInvoicePdf(ReceiptData data, string outputPath);
    string GenerateReportPdf(SalesReport report, string businessName, string outputPath, string currencySymbol);
}

public interface IBackupService
{
    string BackupFolder { get; }
    BackupInfo CreateBackup(string? label = null);
    IReadOnlyList<BackupInfo> ListBackups();
    void Restore(string backupPath);
    void Prune(int keepCount);
}

public sealed record BackupInfo(string Path, DateTime CreatedAt, long SizeBytes);
