using RixelPOS.Domain.Entities;
using RixelPOS.Domain.Enums;

namespace RixelPOS.Application.Fiscal;

public sealed record FiscalInvoiceData(
    string? Number,
    string? Json,
    IReadOnlyList<string> ReceiptLines);

public interface IFiscalProvider
{
    string Name { get; }
    FiscalInvoiceData CreateInvoice(Domain.Settings.BusinessSettings settings, Sale sale, Customer? customer, long sequence);
}

public interface IFiscalProviderFactory
{
    IFiscalProvider Create(Domain.Settings.BusinessSettings settings);
}

public sealed class NoFiscalProvider : IFiscalProvider
{
    public string Name => "none";

    public FiscalInvoiceData CreateInvoice(Domain.Settings.BusinessSettings settings, Sale sale, Customer? customer, long sequence) =>
        new(null, null, Array.Empty<string>());
}

public sealed class FiscalProviderFactory : IFiscalProviderFactory
{
    private readonly DominicanRepublicNcfProvider _dr = new();

    public IFiscalProvider Create(Domain.Settings.BusinessSettings settings) =>
        settings.FiscalMode == FiscalMode.DominicanNcf ? _dr : new NoFiscalProvider();
}
