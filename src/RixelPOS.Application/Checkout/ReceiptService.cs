using RixelPOS.Application.Abstractions;
using RixelPOS.Application.Printing;
using RixelPOS.Application.Services;
using RixelPOS.Domain.Entities;

namespace RixelPOS.Application.Checkout;

public sealed class ReceiptService
{
    private readonly IPosStore _store;
    private readonly SettingsService _settings;
    private readonly IReceiptPrinter _printer;
    private readonly IPdfService _pdf;
    private readonly IClock _clock;

    public ReceiptService(IPosStore store, SettingsService settings, IReceiptPrinter printer, IPdfService pdf, IClock clock)
    {
        _store = store;
        _settings = settings;
        _printer = printer;
        _pdf = pdf;
        _clock = clock;
    }

    public ReceiptData BuildData(Sale sale, Invoice? invoice = null, string? cashierName = null)
    {
        var s = _settings.Current;
        invoice ??= sale.Invoice ?? _store.GetInvoiceForSale(sale.Id);
        var qr = invoice is null
            ? null
            : $"RixelPOS|{invoice.Number}|{sale.TotalCents}|{sale.CompletedAt:yyyyMMddHHmm}";

        return new ReceiptData(
            new ReceiptBusiness(s.BusinessName, s.LegalName, s.TaxId, s.Address, s.Phone, s.Email, s.LogoPath),
            sale,
            invoice,
            cashierName ?? sale.CashierName,
            s.CurrencySymbol,
            s.TaxName,
            s.ReceiptFooter,
            s.ShowQrOnReceipt,
            qr,
            s.ReceiptWidth,
            sale.CustomerName,
            null);
    }

    public async Task<PrintResult> PrintAsync(Sale sale, Invoice? invoice = null, CancellationToken ct = default)
    {
        if (!_printer.IsConfigured)
        {
            return PrintResult.Fail("No hay impresora configurada. Ve a Ajustes → Recibos.", "printer.not_configured");
        }

        var data = BuildData(sale, invoice);
        var result = await _printer.PrintAsync(data, ct).ConfigureAwait(false);
        if (result.Success && _settings.Current.CashDrawerEnabled
            && _settings.Current.AutoOpenDrawerOnCash
            && sale.Payments.Any(p => p.Method == Domain.Enums.PaymentMethod.Cash))
        {
            await _printer.OpenCashDrawerAsync(ct).ConfigureAwait(false);
        }

        return result;
    }

    public Task<PrintResult> PrintTestAsync(CancellationToken ct = default) => _printer.PrintTestAsync(ct);

    public Task<PrintResult> OpenDrawerAsync(CancellationToken ct = default) => _printer.OpenCashDrawerAsync(ct);

    public string ExportPdf(Sale sale, string outputPath) => _pdf.GenerateInvoicePdf(BuildData(sale), outputPath);

    public string DefaultPdfPath(Invoice? invoice, string folder)
    {
        var name = invoice?.Number ?? $"sale-{_clock.LocalNow:yyyyMMdd-HHmmss}";
        return Path.Combine(folder, $"Factura-{name}.pdf");
    }
}
