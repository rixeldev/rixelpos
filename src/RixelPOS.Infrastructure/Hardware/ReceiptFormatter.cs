using System.Globalization;
using System.Text;
using RixelPOS.Application.Printing;
using RixelPOS.Domain.Common;
using RixelPOS.Domain.Entities;
using RixelPOS.Domain.Enums;

namespace RixelPOS.Infrastructure.Hardware;

public static class ReceiptFormatter
{
    public static byte[] Format(ReceiptData data)
    {
        var width = data.Width == ReceiptWidth.Mm58 ? 32 : 48;
        var sale = data.Sale;
        var b = new EscPosBuilder();
        b.Initialize();

        if (!string.IsNullOrWhiteSpace(data.Business.LogoPath))
        {
            b.LogoRaster(data.Business.LogoPath, data.Width == ReceiptWidth.Mm58 ? 384 : 576);
        }

        b.Align(EscPosBuilder.TextAlign.Center).Bold().DoubleSize().Line(Truncate(data.Business.Name, width / 2));
        b.DoubleSize(false).Bold(false);
        if (!string.IsNullOrWhiteSpace(data.Business.LegalName) && data.Business.LegalName != data.Business.Name)
        {
            b.Line(Truncate(data.Business.LegalName, width));
        }

        if (!string.IsNullOrWhiteSpace(data.Business.TaxId))
        {
            b.Line($"RNC: {data.Business.TaxId}");
        }

        if (!string.IsNullOrWhiteSpace(data.Business.Address))
        {
            foreach (var line in Wrap(data.Business.Address, width))
            {
                b.Line(line);
            }
        }

        if (!string.IsNullOrWhiteSpace(data.Business.Phone))
        {
            b.Line($"Tel: {data.Business.Phone}");
        }

        if (!string.IsNullOrWhiteSpace(data.Business.Email))
        {
            b.Line(data.Business.Email);
        }

        b.Separator('=', width);
        var title = data.Invoice is not null ? "FACTURA" : "RECIBO";
        b.Bold().Line(title);
        if (data.Invoice is not null)
        {
            b.Line($"No. {data.Invoice.Number}");
            if (!string.IsNullOrWhiteSpace(data.Invoice.Ncf))
            {
                b.Line($"NCF: {data.Invoice.Ncf}");
            }
        }

        b.Bold(false);
        b.Line($"Fecha: {(sale.CompletedAt ?? sale.CreatedAt).ToLocalTime():dd/MM/yyyy HH:mm}");
        b.Line($"Cajero: {data.CashierName}");
        if (!string.IsNullOrWhiteSpace(sale.CustomerName))
        {
            b.Line($"Cliente: {sale.CustomerName}");
        }

        if (!string.IsNullOrWhiteSpace(data.CustomerTaxId))
        {
            b.Line($"RNC cliente: {data.CustomerTaxId}");
        }

        b.Separator('-', width);

        foreach (var line in sale.Lines)
        {
            foreach (var nameLine in Wrap(line.Name, width))
            {
                b.Line(nameLine);
            }

            var qty = line.Quantity == Math.Truncate(line.Quantity)
                ? line.Quantity.ToString("0", CultureInfo.InvariantCulture)
                : line.Quantity.ToString("0.###", CultureInfo.InvariantCulture);
            var unit = FormatMoney(line.UnitPriceCents, data.CurrencySymbol);
            b.Line(Columns($"  {qty} x {unit}", FormatMoney(line.TotalCents, data.CurrencySymbol), width));
            if (line.DiscountCents > 0)
            {
                b.Line(Columns("  Descuento", "-" + FormatMoney(line.DiscountCents, data.CurrencySymbol), width));
            }
        }

        b.Separator('-', width);
        b.Line(Columns("Subtotal", FormatMoney(sale.SubtotalCents, data.CurrencySymbol), width));

        if (sale.DiscountCents > 0)
        {
            b.Line(Columns("Descuento", "-" + FormatMoney(sale.DiscountCents, data.CurrencySymbol), width));
        }

        foreach (var taxGroup in sale.Lines
                     .Where(l => l.TaxCents != 0)
                     .GroupBy(l => l.TaxRateBasisPoints)
                     .OrderBy(g => g.Key))
        {
            var label = $"{data.TaxName} {taxGroup.Key / 100m:0.##}%";
            b.Line(Columns(label, FormatMoney(taxGroup.Sum(l => l.TaxCents), data.CurrencySymbol), width));
        }

        b.Separator('=', width);
        b.Align(EscPosBuilder.TextAlign.Left).Bold().DoubleSize();
        b.Line(Columns("TOTAL", FormatMoney(sale.TotalCents, data.CurrencySymbol), width / 2));
        b.DoubleSize(false).Bold(false);
        b.Separator('=', width);

        foreach (var payment in sale.Payments)
        {
            var label = PaymentLabel(payment.Method);
            b.Line(Columns(label, FormatMoney(payment.AmountCents, data.CurrencySymbol), width));
            if (payment.Method == PaymentMethod.Cash && payment.TenderedCents > payment.AmountCents)
            {
                b.Line(Columns("  Recibido", FormatMoney(payment.TenderedCents, data.CurrencySymbol), width));
            }
        }

        if (sale.ChangeCents > 0)
        {
            b.Bold().Line(Columns("CAMBIO", FormatMoney(sale.ChangeCents, data.CurrencySymbol), width)).Bold(false);
        }

        var itemCount = sale.Lines.Sum(l => l.Quantity);
        b.Line($"Artículos: {(itemCount == Math.Truncate(itemCount) ? itemCount.ToString("0") : itemCount.ToString("0.###"))}");

        if (!string.IsNullOrWhiteSpace(sale.Note))
        {
            b.Line();
            foreach (var noteLine in Wrap($"Nota: {sale.Note}", width))
            {
                b.Line(noteLine);
            }
        }

        b.Line();
        b.Align(EscPosBuilder.TextAlign.Center);
        if (!string.IsNullOrWhiteSpace(data.Footer))
        {
            foreach (var footerLine in Wrap(data.Footer, width))
            {
                b.Line(footerLine);
            }
        }

        if (data.ShowQr && !string.IsNullOrWhiteSpace(data.QrPayload))
        {
            b.QrRaster(data.QrPayload, data.Width == ReceiptWidth.Mm58 ? 4 : 5);
        }

        if (data.Invoice is not null)
        {
            b.BarcodeCode128(data.Invoice.Number, 50);
        }

        b.Feed(2).Cut();
        return b.Build();
    }

    public static string FormatMoney(long cents, string symbol) =>
        $"{symbol} {cents / 100m:N2}";

    public static string PaymentLabel(PaymentMethod method) => method switch
    {
        PaymentMethod.Cash => "Efectivo",
        PaymentMethod.Card => "Tarjeta",
        PaymentMethod.Transfer => "Transferencia",
        PaymentMethod.StoreCredit => "Crédito a favor",
        PaymentMethod.Credit => "Crédito",
        _ => method.ToString()
    };

    public static string Columns(string left, string right, int width)
    {
        left = EscPosBuilder.AsciiText(left);
        right = EscPosBuilder.AsciiText(right);
        var space = Math.Max(1, width - left.Length - right.Length);
        if (left.Length + right.Length + 1 > width)
        {
            left = left[..Math.Max(0, width - right.Length - 1)];
            space = 1;
        }

        return left + new string(' ', space) + right;
    }

    public static IEnumerable<string> Wrap(string text, int width)
    {
        text = EscPosBuilder.AsciiText(text);
        if (string.IsNullOrWhiteSpace(text))
        {
            yield break;
        }

        foreach (var paragraph in text.Split('\n'))
        {
            var words = paragraph.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var current = new StringBuilder();
            foreach (var word in words)
            {
                if (current.Length > 0 && current.Length + 1 + word.Length > width)
                {
                    yield return current.ToString();
                    current.Clear();
                }

                if (current.Length > 0)
                {
                    current.Append(' ');
                }

                current.Append(word);
            }

            if (current.Length > 0)
            {
                yield return current.ToString();
            }
        }
    }

    private static string Truncate(string text, int width)
    {
        text = EscPosBuilder.AsciiText(text);
        return text.Length <= width ? text : text[..Math.Max(0, width - 1)] + "…".Replace("…", ".");
    }
}
