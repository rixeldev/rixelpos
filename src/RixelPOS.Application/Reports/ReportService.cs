using System.Globalization;
using System.Text;
using RixelPOS.Application.Abstractions;
using RixelPOS.Application.Printing;
using RixelPOS.Application.Services;
using RixelPOS.Domain.Entities;
using RixelPOS.Domain.Reporting;

namespace RixelPOS.Application.Reports;

public sealed class ReportService
{
    private readonly IPosStore _store;
    private readonly IPdfService _pdf;
    private readonly SettingsService _settings;
    private readonly IClock _clock;

    public ReportService(IPosStore store, IPdfService pdf, SettingsService settings, IClock clock)
    {
        _store = store;
        _pdf = pdf;
        _settings = settings;
        _clock = clock;
    }

    public SalesReport Get(DateTime from, DateTime to)
    {
        if (to < from)
        {
            throw new ArgumentException("The end date must be after the start date.");
        }

        return _store.BuildSalesReport(from, to);
    }

    public SalesReport GetToday() => Get(_clock.Today.ToDateTime(TimeOnly.MinValue), _clock.Today.ToDateTime(TimeOnly.MaxValue));

    public DashboardSnapshot GetDashboard() => _store.BuildDashboard(_clock.Today.ToDateTime(TimeOnly.MinValue));

    public string ExportCsv(SalesReport report, string outputPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        var sb = new StringBuilder();
        void Row(params object?[] values) =>
            sb.AppendLine(string.Join(",", values.Select(v => Csv(v?.ToString() ?? ""))));

        Row("RixelPOS", "Reporte de ventas");
        Row("Desde", report.From.ToString("yyyy-MM-dd HH:mm"));
        Row("Hasta", report.To.ToString("yyyy-MM-dd HH:mm"));
        Row();
        Row("Resumen");
        Row("Ventas", report.SalesCount);
        Row("Artículos", report.ItemCount);
        Row("Total", report.TotalCents / 100m);
        Row("Impuestos", report.TaxCents / 100m);
        Row("Descuentos", report.DiscountCents / 100m);
        Row("Costo", report.CostCents / 100m);
        Row("Ganancia", report.ProfitCents / 100m);
        Row("Ticket promedio", report.AverageTicketCents / 100m);
        Row();
        Row("Ventas por día", "Total", "Cantidad");
        foreach (var day in report.Daily)
        {
            Row(day.Date.ToString("yyyy-MM-dd"), day.TotalCents / 100m, day.SalesCount);
        }

        Row();
        Row("Top productos", "Cantidad", "Total", "Costo", "Ganancia");
        foreach (var p in report.TopProducts)
        {
            Row(p.Name, p.Quantity, p.TotalCents / 100m, p.CostCents / 100m, p.ProfitCents / 100m);
        }

        Row();
        Row("Método de pago", "Cantidad", "Total");
        foreach (var p in report.ByPayment)
        {
            Row(p.Method.ToString(), p.Count, p.TotalCents / 100m);
        }

        Row();
        Row("Cajero", "Ventas", "Total");
        foreach (var c in report.ByCashier)
        {
            Row(c.CashierName, c.SalesCount, c.TotalCents / 100m);
        }

        Row();
        Row("Categoría", "Cantidad", "Total");
        foreach (var c in report.ByCategory)
        {
            Row(c.CategoryName, c.Quantity, c.TotalCents / 100m);
        }

        File.WriteAllText(outputPath, sb.ToString(), new UTF8Encoding(true));
        return outputPath;
    }

    public string ExportPdf(SalesReport report, string outputPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        return _pdf.GenerateReportPdf(report, _settings.Current.BusinessName, outputPath, _settings.Current.CurrencySymbol);
    }

    private static string Csv(string value) =>
        value.Contains(',') || value.Contains('"') || value.Contains('\n')
            ? $"\"{value.Replace("\"", "\"\"")}\""
            : value;
}
