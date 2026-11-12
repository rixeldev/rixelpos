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

        foreach (var line in Wrap(data.Business.Address, width))
        {
            b.Line(line);
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
        b.Bold().Line(data.Invoice is not null ? "FACTURA" : "RECIBO");
        if (data.Invoice is not null)
        {
            b.Line($"No. {data.Invoice.Number}");
            if (!string.IsNullOrWhiteSpace(data.Invoice.Ncf))
            {
                b.Line($"NCF: {data.Invoice.Ncf}");
            }
        }

        b.Bold(false);
        b.Line((sale.CompletedAt ?? sale.CreatedAt).ToLocalTime().ToString("dd/MM/yyyy HH:mm"));
        b.Align(EscPosBuilder.TextAlign.Left);
        b.Separator('-', width);
        b.Line(Columns("Cliente", string.IsNullOrWhiteSpace(sale.CustomerName) ? "Consumidor final" : sale.CustomerName, width));
        b.Line(Columns("Cajero", data.CashierName, width));
        b.Line(Columns("Pago", PaymentSummary(data), width));
        if (!string.IsNullOrWhiteSpace(data.CustomerTaxId))
        {
            b.Line(Columns("RNC cliente", data.CustomerTaxId, width));
        }

        b.Separator('-', width);
        b.Bold().Line(Columns("CANT / DESCRIPCION", "TOTAL", width)).Bold(false);
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
        b.Line(Columns(sale.TaxCents > 0 ? "Subtotal (sin imp.)" : "Subtotal",
            FormatMoney(sale.SubtotalCents, data.CurrencySymbol), width));

        foreach (var taxGroup in sale.Lines
                     .Where(l => l.TaxCents != 0)
                     .GroupBy(l => l.TaxRateBasisPoints)
                     .OrderBy(g => g.Key))
        {
            var label = $"{data.TaxName} {taxGroup.Key / 100m:0.##}%";
            b.Line(Columns(label, FormatMoney(taxGroup.Sum(l => l.TaxCents), data.CurrencySymbol), width));
        }

        var paid = sale.Payments.Sum(p => p.AmountCents);
        var received = sale.Payments
            .Where(p => p.Method is PaymentMethod.Cash or PaymentMethod.Card or PaymentMethod.Transfer)
            .Sum(p => p.TenderedCents);

        b.Separator('=', width);
        b.Bold().Line(Columns("TOTAL", FormatMoney(sale.TotalCents, data.CurrencySymbol), width)).Bold(false);
        b.Separator('=', width);
        b.Line(Columns("Pagado", FormatMoney(paid, data.CurrencySymbol), width));
        if (received > 0)
        {
            b.Line(Columns("Recibido", FormatMoney(received, data.CurrencySymbol), width));
        }

        if (sale.ChangeCents > 0)
        {
            b.Bold().Line(Columns("CAMBIO", FormatMoney(sale.ChangeCents, data.CurrencySymbol), width)).Bold(false);
        }

        if (sale.DiscountCents > 0)
        {
            b.Line(Columns("Descuento aplicado", FormatMoney(sale.DiscountCents, data.CurrencySymbol), width));
        }

        if (sale.Payments.Count > 1 || sale.Payments.Any(p => p.Method == PaymentMethod.Cash && p.TenderedCents > p.AmountCents))
        {
            b.Separator('-', width);
            b.Bold().Line("Pagos").Bold(false);
            foreach (var payment in sale.Payments)
            {
                b.Line(Columns(PaymentLabel(payment.Method), FormatMoney(payment.AmountCents, data.CurrencySymbol), width));
                if (payment.Method == PaymentMethod.Cash && payment.TenderedCents > payment.AmountCents)
                {
                    b.Line(Columns("  Recibido", FormatMoney(payment.TenderedCents, data.CurrencySymbol), width));
                }
            }
        }

        var itemCount = sale.Lines.Sum(l => l.Quantity);
        b.Line($"Articulos: {(itemCount == Math.Truncate(itemCount) ? itemCount.ToString("0") : itemCount.ToString("0.###"))}");

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

        b.Feed(3).Cut();
        return b.Build();
    }

    /// <summary>Plain-text rendering used by the in-app receipt preview.</summary>
    public static string FormatPreview(ReceiptData data)
    {
        var width = data.Width == ReceiptWidth.Mm58 ? 32 : 42;
        var sale = data.Sale;
        var symbol = data.CurrencySymbol;
        var sb = new StringBuilder();

        void Center(string text)
        {
            var value = Truncate(text, width);
            var pad = Math.Max(0, (width - value.Length) / 2);
            sb.AppendLine(new string(' ', pad) + value);
        }

        void Row(string left, string right) => sb.AppendLine(Columns(left, right, width));
        void Separator(char c = '-') => sb.AppendLine(new string(c, width));

        Center(data.Business.Name);
        if (!string.IsNullOrWhiteSpace(data.Business.LegalName) && data.Business.LegalName != data.Business.Name)
        {
            Center(data.Business.LegalName);
        }

        if (!string.IsNullOrWhiteSpace(data.Business.TaxId))
        {
            Center($"RNC: {data.Business.TaxId}");
        }

        foreach (var line in Wrap(data.Business.Address, width))
        {
            Center(line);
        }

        if (!string.IsNullOrWhiteSpace(data.Business.Phone))
        {
            Center($"Tel: {data.Business.Phone}");
        }

        if (!string.IsNullOrWhiteSpace(data.Business.Email))
        {
            Center(data.Business.Email);
        }

        Separator('=');
        Center(data.Invoice is not null ? "FACTURA" : "RECIBO");
        if (data.Invoice is not null)
        {
            Center($"No. {data.Invoice.Number}");
            if (!string.IsNullOrWhiteSpace(data.Invoice.Ncf))
            {
                Center($"NCF: {data.Invoice.Ncf}");
            }
        }

        Center((sale.CompletedAt ?? sale.CreatedAt).ToLocalTime().ToString("dd/MM/yyyy HH:mm"));
        Separator();
        Row("Cliente", string.IsNullOrWhiteSpace(sale.CustomerName) ? "Consumidor final" : sale.CustomerName);
        Row("Cajero", data.CashierName);
        Row("Pago", PaymentSummary(data));
        if (!string.IsNullOrWhiteSpace(data.CustomerTaxId))
        {
            Row("RNC cliente", data.CustomerTaxId);
        }

        Separator();
        sb.AppendLine(Columns("CANT / DESCRIPCION", "TOTAL", width));
        Separator();
        foreach (var line in sale.Lines)
        {
            foreach (var wrapped in Wrap(line.Name, width))
            {
                sb.AppendLine(wrapped);
            }

            Row($"  {line.Quantity:0.###} x {FormatMoney(line.UnitPriceCents, symbol)}",
                FormatMoney(line.TotalCents, symbol));
            if (line.DiscountCents > 0)
            {
                Row("  Descuento", "-" + FormatMoney(line.DiscountCents, symbol));
            }
        }

        Separator();
        Row(sale.TaxCents > 0 ? "Subtotal (sin imp.)" : "Subtotal", FormatMoney(sale.SubtotalCents, symbol));

        foreach (var group in sale.Lines.Where(l => l.TaxCents != 0).GroupBy(l => l.TaxRateBasisPoints).OrderBy(g => g.Key))
        {
            Row($"{data.TaxName} {group.Key / 100m:0.##}%", FormatMoney(group.Sum(l => l.TaxCents), symbol));
        }

        var paid = sale.Payments.Sum(p => p.AmountCents);
        var received = sale.Payments
            .Where(p => p.Method is PaymentMethod.Cash or PaymentMethod.Card or PaymentMethod.Transfer)
            .Sum(p => p.TenderedCents);

        Separator('=');
        Row("TOTAL", FormatMoney(sale.TotalCents, symbol));
        Separator('=');
        Row("Pagado", FormatMoney(paid, symbol));
        if (received > 0)
        {
            Row("Recibido", FormatMoney(received, symbol));
        }

        if (sale.ChangeCents > 0)
        {
            Row("CAMBIO", FormatMoney(sale.ChangeCents, symbol));
        }

        if (sale.DiscountCents > 0)
        {
            Row("Descuento aplicado", FormatMoney(sale.DiscountCents, symbol));
        }

        if (sale.Payments.Count > 1 || sale.Payments.Any(p => p.Method == PaymentMethod.Cash && p.TenderedCents > p.AmountCents))
        {
            Separator();
            sb.AppendLine("Pagos");
            foreach (var payment in sale.Payments)
            {
                Row(PaymentLabel(payment.Method), FormatMoney(payment.AmountCents, symbol));
                if (payment.Method == PaymentMethod.Cash && payment.TenderedCents > payment.AmountCents)
                {
                    Row("  Recibido", FormatMoney(payment.TenderedCents, symbol));
                }
            }
        }

        sb.AppendLine($"Articulos: {sale.Lines.Sum(l => l.Quantity):0.###}");
        if (!string.IsNullOrWhiteSpace(sale.Note))
        {
            sb.AppendLine();
            foreach (var line in Wrap("Nota: " + sale.Note, width))
            {
                sb.AppendLine(line);
            }
        }

        sb.AppendLine();
        if (!string.IsNullOrWhiteSpace(data.Footer))
        {
            foreach (var line in Wrap(data.Footer, width))
            {
                Center(line);
            }
        }

        if (data.ShowQr && !string.IsNullOrWhiteSpace(data.QrPayload))
        {
            Center("QR incluido en el comprobante impreso");
        }

        return sb.ToString();
    }

    public static string PaymentSummary(ReceiptData data)
    {
        var methods = data.Sale.Payments.Select(p => p.Method).Distinct().ToList();
        return methods.Count switch
        {
            0 => "—",
            1 => PaymentLabel(methods[0]),
            _ => "Mixto"
        };
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
