using System.Text.Json;
using RixelPOS.Domain.Entities;
using RixelPOS.Domain.Enums;

namespace RixelPOS.Application.Fiscal;

/// <summary>
/// Optional Dominican Republic fiscal module: generates an NCF (comprobante fiscal)
/// and can later be extended with DGII e-CF signing/submission without touching the core.
/// </summary>
public sealed class DominicanRepublicNcfProvider : IFiscalProvider
{
    public string Name => "DO-NCF";

    public FiscalInvoiceData CreateInvoice(Domain.Settings.BusinessSettings settings, Sale sale, Customer? customer, long sequence)
    {
        var prefix = string.IsNullOrWhiteSpace(settings.NcfPrefix) ? "B02" : settings.NcfPrefix.Trim().ToUpperInvariant();
        var ncf = prefix + sequence.ToString("00000000");
        var data = JsonSerializer.Serialize(new
        {
            ncf,
            rnc = settings.FiscalRnc,
            issuedAt = DateTime.UtcNow.ToString("O"),
            customer = customer?.Name,
            customerTaxId = customer?.TaxId,
            totalCents = sale.TotalCents,
            taxCents = sale.TaxCents,
            mode = "NCF"
        });

        return new FiscalInvoiceData(ncf, data, new[] { $"NCF: {ncf}" });
    }
}
