using QRCoder;
using RixelPOS.Application.Printing;
using RixelPOS.Domain.Entities;
using RixelPOS.Domain.Enums;
using RixelPOS.Domain.Reporting;
using RixelPOS.Infrastructure.Hardware;
using SkiaSharp;

namespace RixelPOS.Infrastructure.Printing;

public sealed class PdfService : IPdfService
{
    private const float PageWidth = 595f;
    private const float PageHeight = 842f;
    private const float Margin = 42f;

    private static readonly SKColor Ink = new(0x14, 0x1B, 0x24);
    private static readonly SKColor Muted = new(0x5C, 0x67, 0x73);
    private static readonly SKColor Accent = new(0x00, 0xA6, 0x91);
    private static readonly SKColor Line = new(0xD8, 0xDE, 0xE5);
    private static readonly SKColor Light = new(0xF4, 0xF7, 0xF9);

    public string GenerateInvoicePdf(ReceiptData data, string outputPath)
    {
        _symbol = data.CurrencySymbol;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);

        using var stream = new SKFileWStream(outputPath);
        using var document = SKDocument.CreatePdf(stream);
        var page = new PdfPage(document, "Factura " + (data.Invoice?.Number ?? ""));

        DrawInvoiceHeader(page, data);

        var y = page.Y;
        y = DrawTableHeader(page, y, "Descripción", "Cant.", "Precio", "Imp.", "Total");

        foreach (var line in data.Sale.Lines)
        {
            var rowHeight = Math.Max(24f, 14f * (WrapLines(line.Name, 220f, 9.5f).Count + 1));
            if (page.NeedsPageBreak(y + rowHeight))
            {
                page.EndPage();
                page = new PdfPage(document, "Factura " + (data.Invoice?.Number ?? "") + " (cont.)");
                DrawContinuationHeader(page, data);
                y = DrawTableHeader(page, page.Y, "Descripción", "Cant.", "Precio", "Imp.", "Total");
            }

            y = DrawInvoiceLine(page, y, line, rowHeight);
        }

        y = DrawLine(page, y);
        y += 10;

        if (page.NeedsPageBreak(y + 190))
        {
            page.EndPage();
            page = new PdfPage(document, "Factura " + (data.Invoice?.Number ?? "") + " (cont.)");
            DrawContinuationHeader(page, data);
            y = page.Y;
        }

        y = DrawTotals(page, y, data);
        y += 16;
        y = DrawPayments(page, y, data);
        y += 16;
        DrawFooter(page, y, data);

        page.EndPage();
        document.Close();
        return outputPath;
    }

    public string GenerateReportPdf(SalesReport report, string businessName, string outputPath, string currencySymbol)
    {
        _symbol = currencySymbol;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);

        using var stream = new SKFileWStream(outputPath);
        using var document = SKDocument.CreatePdf(stream);
        var page = new PdfPage(document, "Reporte de ventas");
        var y = 0f;

        DrawText(page.Canvas, string.IsNullOrWhiteSpace(businessName) ? "RixelPOS" : businessName, Margin, 52, 20, true, Ink);
        DrawText(page.Canvas, "Reporte de ventas", Margin, 76, 13, false, Muted);
        DrawText(page.Canvas, $"{report.From:dd/MM/yyyy HH:mm}  —  {report.To:dd/MM/yyyy HH:mm}", Margin, 94, 9.5f, false, Muted);
        DrawTextRight(page.Canvas, "Generado " + DateTime.Now.ToString("dd/MM/yyyy HH:mm"), PageWidth - Margin, 94, 8.5f, false, Muted);
        y = 116;
        page.Y = y;

        var summary = new (string Label, string Value)[]
        {
            ("Ventas", report.SalesCount.ToString()),
            ("Artículos", report.ItemCount.ToString()),
            ("Total", Money(report.TotalCents)),
            ("Impuestos", Money(report.TaxCents)),
            ("Descuentos", Money(report.DiscountCents)),
            ("Ganancia", Money(report.ProfitCents)),
            ("Ticket promedio", Money(report.AverageTicketCents))
        };

        y = DrawSummaryBoxes(page, y, summary);
        y += 18;

        y = DrawSection(page, ref y, "Ventas por día", new[] { "Fecha", "Total", "Ventas" },
            report.Daily.OrderByDescending(d => d.Date).Take(60)
                .Select(d => new[] { d.Date.ToString("dd/MM/yyyy"), Money(d.TotalCents), d.SalesCount.ToString() })
                .ToList());

        y = DrawSection(page, ref y, "Productos más vendidos", new[] { "Producto", "Cant.", "Total", "Ganancia" },
            report.TopProducts.Take(25)
                .Select(p => new[] { p.Name, p.Quantity.ToString("0.###"), Money(p.TotalCents), Money(p.ProfitCents) })
                .ToList());

        y = DrawSection(page, ref y, "Métodos de pago", new[] { "Método", "Ventas", "Total" },
            report.ByPayment
                .Select(p => new[] { PaymentName(p.Method), p.Count.ToString(), Money(p.TotalCents) })
                .ToList());

        y = DrawSection(page, ref y, "Ventas por cajero", new[] { "Cajero", "Ventas", "Total" },
            report.ByCashier
                .Select(c => new[] { c.CashierName, c.SalesCount.ToString(), Money(c.TotalCents) })
                .ToList());

        y = DrawSection(page, ref y, "Ventas por categoría", new[] { "Categoría", "Cant.", "Total" },
            report.ByCategory
                .Select(c => new[] { c.CategoryName, c.Quantity.ToString("0.###"), Money(c.TotalCents) })
                .ToList(), pageBreak: false);

        DrawText(page.Canvas, "RixelPOS · RixelDev", Margin, PageHeight - 28, 8, false, Muted);
        page.EndPage();
        document.Close();
        return outputPath;
    }

    private static void DrawInvoiceHeader(PdfPage page, ReceiptData data)
    {
        var c = page.Canvas;
        var business = data.Business;
        var x = Margin;

        if (!string.IsNullOrWhiteSpace(business.LogoPath) && File.Exists(business.LogoPath))
        {
            try
            {
                using var logo = SKBitmap.Decode(business.LogoPath);
                if (logo is not null)
                {
                    var scale = Math.Min(64f / logo.Height, 150f / logo.Width);
                    c.DrawBitmap(logo, new SKRect(x, 38, x + logo.Width * scale, 38 + logo.Height * scale));
                }
            }
            catch
            {
                // logo is optional
            }
        }

        var textX = string.IsNullOrWhiteSpace(business.LogoPath) ? x : x + 160;
        DrawText(c, business.Name, textX, 58, 18, true, Ink);
        var meta = new[] { business.LegalName, business.TaxId.Length > 0 ? "RNC: " + business.TaxId : "", business.Address, business.Phone, business.Email }
            .Where(s => !string.IsNullOrWhiteSpace(s));
        var metaY = 76f;
        foreach (var item in meta)
        {
            DrawText(c, item, textX, metaY, 8.7f, false, Muted);
            metaY += 12;
        }

        DrawTextRight(c, "FACTURA", PageWidth - Margin, 58, 22, true, Accent);
        DrawTextRight(c, data.Invoice?.Number ?? "—", PageWidth - Margin, 78, 12, true, Ink);
        if (!string.IsNullOrWhiteSpace(data.Invoice?.Ncf))
        {
            DrawTextRight(c, "NCF: " + data.Invoice.Ncf, PageWidth - Margin, 94, 8.5f, false, Muted);
        }

        DrawTextRight(c, (data.Sale.CompletedAt ?? data.Sale.CreatedAt).ToLocalTime().ToString("dd/MM/yyyy HH:mm"),
            PageWidth - Margin, 110, 8.5f, false, Muted);

        var infoY = 150f;
        DrawText(c, "Cliente", Margin, infoY, 8, true, Muted);
        DrawText(c, string.IsNullOrWhiteSpace(data.Sale.CustomerName) ? "Consumidor final" : data.Sale.CustomerName,
            Margin, infoY + 14, 10, false, Ink);
        if (!string.IsNullOrWhiteSpace(data.CustomerTaxId))
        {
            DrawText(c, "RNC: " + data.CustomerTaxId, Margin, infoY + 27, 8.5f, false, Muted);
        }

        DrawText(c, "Cajero", PageWidth / 2 + 30, infoY, 8, true, Muted);
        DrawText(c, data.CashierName, PageWidth / 2 + 30, infoY + 14, 10, false, Ink);

        page.Y = 190;
    }

    private static void DrawContinuationHeader(PdfPage page, ReceiptData data)
    {
        DrawText(page.Canvas, data.Business.Name + " · Factura " + (data.Invoice?.Number ?? ""), Margin, 50, 11, true, Ink);
        page.Y = 66;
    }

    private static float DrawTableHeader(PdfPage page, float y, string desc, string qty, string price, string tax, string total)
    {
        var c = page.Canvas;
        var rect = new SKRect(Margin, y - 12, PageWidth - Margin, y + 8);
        c.DrawRect(rect, new SKPaint { Color = Light });
        DrawText(c, desc, Margin + 6, y, 8, true, Muted);
        DrawTextRight(c, qty, Margin + 310, y, 8, true, Muted);
        DrawTextRight(c, price, Margin + 390, y, 8, true, Muted);
        DrawTextRight(c, tax, Margin + 440, y, 8, true, Muted);
        DrawTextRight(c, total, PageWidth - Margin - 6, y, 8, true, Muted);
        return y + 20;
    }

    private static float DrawInvoiceLine(PdfPage page, float y, SaleLine line, float rowHeight)
    {
        var c = page.Canvas;
        var lines = WrapLines(line.Name, 280f, 9.5f);
        var nameY = y + 10;
        foreach (var text in lines)
        {
            DrawText(c, text, Margin + 6, nameY, 9.5f, false, Ink);
            nameY += 13;
        }

        DrawTextRight(c, line.Quantity.ToString("0.###"), Margin + 310, y + 10, 9, false, Ink);
        DrawTextRight(c, Money(line.UnitPriceCents), Margin + 390, y + 10, 9, false, Ink);
        DrawTextRight(c, TaxLabel(line), Margin + 440, y + 10, 9, false, Muted);
        DrawTextRight(c, Money(line.TotalCents), PageWidth - Margin - 6, y + 10, 9.5f, true, Ink);

        var bottom = y + rowHeight;
        c.DrawLine(Margin, bottom, PageWidth - Margin, bottom, new SKPaint { Color = Line, StrokeWidth = 0.5f });
        return bottom + 6;
    }

    private static float DrawTotals(PdfPage page, float y, ReceiptData data)
    {
        var c = page.Canvas;
        var left = PageWidth - Margin - 230;
        var valueX = PageWidth - Margin - 6;
        y = DrawTotalRow(c, left, valueX, y, "Subtotal", Money(data.Sale.SubtotalCents), false);
        if (data.Sale.DiscountCents > 0)
        {
            y = DrawTotalRow(c, left, valueX, y, "Descuento", "-" + Money(data.Sale.DiscountCents), false);
        }

        foreach (var group in data.Sale.Lines.Where(l => l.TaxCents != 0).GroupBy(l => l.TaxRateBasisPoints))
        {
            y = DrawTotalRow(c, left, valueX, y, $"{data.TaxName} {group.Key / 100m:0.##}%", Money(group.Sum(l => l.TaxCents)), false);
        }

        c.DrawRect(new SKRect(left - 10, y, PageWidth - Margin, y + 32), new SKPaint { Color = Light });
        DrawText(c, "TOTAL", left, y + 21, 11, true, Ink);
        DrawTextRight(c, Money(data.Sale.TotalCents), valueX, y + 22, 15, true, Accent);
        return y + 42;
    }

    private static float DrawTotalRow(SKCanvas canvas, float left, float valueX, float y, string label, string value, bool bold)
    {
        DrawText(canvas, label, left, y + 12, 9, bold, Muted);
        DrawTextRight(canvas, value, valueX, y + 12, 9.5f, bold, Ink);
        return y + 16;
    }

    private static float DrawPayments(PdfPage page, float y, ReceiptData data)
    {
        DrawText(page.Canvas, "Pagos", Margin, y, 9, true, Ink);
        y += 13;
        foreach (var payment in data.Sale.Payments)
        {
            DrawText(page.Canvas, PaymentName(payment.Method), Margin, y, 9, false, Muted);
            DrawTextRight(page.Canvas, Money(payment.AmountCents), PageWidth - Margin, y, 9, false, Ink);
            y += 13;
        }

        if (data.Sale.ChangeCents > 0)
        {
            DrawText(page.Canvas, "Cambio", Margin, y, 9, true, Ink);
            DrawTextRight(page.Canvas, Money(data.Sale.ChangeCents), PageWidth - Margin, y, 9.5f, true, Accent);
            y += 13;
        }

        return y;
    }

    private static void DrawFooter(PdfPage page, float y, ReceiptData data)
    {
        var c = page.Canvas;
        if (data.ShowQr && !string.IsNullOrWhiteSpace(data.QrPayload))
        {
            try
            {
                using var generator = new QRCodeGenerator();
                using var qrData = generator.CreateQrCode(data.QrPayload, QRCodeGenerator.ECCLevel.M, forceUtf8: true);
                var png = new PngByteQRCode(qrData).GetGraphic(8);
                using var bitmap = SKBitmap.Decode(png);
                if (bitmap is not null)
                {
                    c.DrawBitmap(bitmap, new SKRect(Margin, y, Margin + 74, y + 74));
                }
            }
            catch
            {
                // QR is optional
            }
        }

        DrawText(c, data.Footer, Margin + 88, y + 18, 9, false, Muted);
        DrawText(c, "Gracias por su compra", Margin + 88, y + 34, 8, false, Muted);
        DrawText(c, "RixelPOS · RixelDev", Margin + 88, y + 50, 8, false, Muted);
    }

    private static float DrawSummaryBoxes(PdfPage page, float y, IReadOnlyList<(string Label, string Value)> items)
    {
        var columns = 4;
        var gap = 8f;
        var width = (PageWidth - Margin * 2 - gap * (columns - 1)) / columns;
        for (var i = 0; i < items.Count; i++)
        {
            var row = i / columns;
            var col = i % columns;
            var x = Margin + col * (width + gap);
            var boxY = y + row * 56;
            var rect = new SKRect(x, boxY, x + width, boxY + 48);
            page.Canvas.DrawRoundRect(rect, 6, 6, new SKPaint { Color = Light });
            DrawText(page.Canvas, items[i].Label, x + 10, boxY + 17, 8, false, Muted);
            DrawText(page.Canvas, items[i].Value, x + 10, boxY + 37, 12, true, Ink);
        }

        return y + (int)Math.Ceiling(items.Count / (double)columns) * 56;
    }

    private static float DrawSection(PdfPage page, ref float y, string title, string[] headers, IReadOnlyList<string[]> rows, bool pageBreak = true)
    {
        if (pageBreak && page.NeedsPageBreak(y + 80))
        {
            page.EndPage();
            page.NewPage();
            y = 50;
        }

        DrawText(page.Canvas, title, Margin, y + 12, 12, true, Ink);
        y += 24;
        var columns = headers.Length;
        var width = (PageWidth - Margin * 2) / columns;

        page.Canvas.DrawRect(new SKRect(Margin, y - 11, PageWidth - Margin, y + 5), new SKPaint { Color = Light });
        for (var i = 0; i < columns; i++)
        {
            if (i == 0)
            {
                DrawText(page.Canvas, headers[i], Margin + 5, y, 8, true, Muted);
            }
            else
            {
                DrawTextRight(page.Canvas, headers[i], Margin + width * (i + 1) - 5, y, 8, true, Muted);
            }
        }

        y += 17;
        foreach (var row in rows)
        {
            if (page.NeedsPageBreak(y + 18))
            {
                page.EndPage();
                page.NewPage();
                y = 50;
            }

            for (var i = 0; i < Math.Min(columns, row.Length); i++)
            {
                var text = row[i];
                if (i == 0)
                {
                    DrawText(page.Canvas, Truncate(text, 46), Margin + 5, y, 8.5f, false, Ink);
                }
                else
                {
                    DrawTextRight(page.Canvas, text, Margin + width * (i + 1) - 5, y, 8.5f, false, Ink);
                }
            }

            page.Canvas.DrawLine(Margin, y + 6, PageWidth - Margin, y + 6, new SKPaint { Color = Line, StrokeWidth = 0.4f });
            y += 16;
        }

        return y + 18;
    }

    private static float DrawLine(PdfPage page, float y)
    {
        page.Canvas.DrawLine(Margin, y, PageWidth - Margin, y, new SKPaint { Color = Line, StrokeWidth = 0.8f });
        return y + 6;
    }

    private static void DrawText(SKCanvas canvas, string text, float x, float y, float size, bool bold, SKColor color)
    {
        using var paint = new SKPaint
        {
            Color = color,
            IsAntialias = true,
            TextSize = size,
            Typeface = bold ? BoldFace : RegularFace
        };
        canvas.DrawText(EscPosBuilder.AsciiText(text), x, y, paint);
    }

    private static void DrawTextRight(SKCanvas canvas, string text, float right, float y, float size, bool bold, SKColor color)
    {
        using var paint = new SKPaint
        {
            Color = color,
            IsAntialias = true,
            TextSize = size,
            Typeface = bold ? BoldFace : RegularFace
        };
        var value = EscPosBuilder.AsciiText(text);
        canvas.DrawText(value, right - paint.MeasureText(value), y, paint);
    }

    private static readonly SKTypeface RegularFace = SKTypeface.FromFamilyName("DejaVu Sans") ?? SKTypeface.Default;
    private static readonly SKTypeface BoldFace = SKTypeface.FromFamilyName("DejaVu Sans",
        SKFontStyleWeight.Bold, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright) ?? SKTypeface.Default;

    private static List<string> WrapLines(string text, float maxWidth, float fontSize)
    {
        var words = EscPosBuilder.AsciiText(text).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var lines = new List<string>();
        var current = "";
        using var paint = new SKPaint { TextSize = fontSize, Typeface = RegularFace };
        foreach (var word in words)
        {
            var candidate = current.Length == 0 ? word : current + " " + word;
            if (paint.MeasureText(candidate) <= maxWidth)
            {
                current = candidate;
            }
            else
            {
                if (current.Length > 0)
                {
                    lines.Add(current);
                }

                current = word;
            }
        }

        if (current.Length > 0)
        {
            lines.Add(current);
        }

        return lines.Count == 0 ? new List<string> { "" } : lines;
    }

    private static string _symbol = "RD$";
    private static string Money(long cents) => $"{_symbol} {cents / 100m:N2}";

    private static string PaymentName(PaymentMethod method) => method switch
    {
        PaymentMethod.Cash => "Efectivo",
        PaymentMethod.Card => "Tarjeta",
        PaymentMethod.Transfer => "Transferencia",
        PaymentMethod.StoreCredit => "Crédito a favor",
        PaymentMethod.Credit => "Crédito",
        _ => method.ToString()
    };

    private static string TaxLabel(SaleLine line) =>
        line.TaxRateBasisPoints == 0 ? "—" : $"{line.TaxRateBasisPoints / 100m:0.##}%";

    private static string Truncate(string text, int max)
    {
        text = EscPosBuilder.AsciiText(text);
        return text.Length <= max ? text : text[..max] + "...";
    }

    private sealed class PdfPage
    {
        private readonly SKDocument _document;
        private readonly string _title;

        public PdfPage(SKDocument document, string title)
        {
            _document = document;
            _title = title;
            Canvas = document.BeginPage(PageWidth, PageHeight);
        }

        public SKCanvas Canvas { get; private set; }
        public float Y { get; set; } = 40;

        public bool NeedsPageBreak(float y) => y > PageHeight - 70;

        public void EndPage() => _document.EndPage();

        public void NewPage()
        {
            Canvas = _document.BeginPage(PageWidth, PageHeight);
            Y = 40;
        }
    }
}
