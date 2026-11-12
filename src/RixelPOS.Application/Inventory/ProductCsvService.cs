using System.Globalization;
using System.Text;
using RixelPOS.Application.Abstractions;
using RixelPOS.Application.Services;
using RixelPOS.Domain.Common;
using RixelPOS.Domain.Entities;

namespace RixelPOS.Application.Inventory;

/// <summary>CSV import/export compatible with Excel and LibreOffice.</summary>
public sealed class ProductCsvService
{
    private static readonly string[] Headers =
    {
        "sku", "name", "barcode", "category", "cost", "price", "tax_percent", "tax_inclusive",
        "stock", "min_stock", "unit", "favorite", "active"
    };

    private readonly IPosStore _store;
    private readonly AuditService _audit;
    private readonly IClock _clock;

    public ProductCsvService(IPosStore store, AuditService audit, IClock clock)
    {
        _store = store;
        _audit = audit;
        _clock = clock;
    }

    public string Export(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var products = _store.GetProducts(includeInactive: true);

        var sb = new StringBuilder();
        sb.AppendLine(string.Join(",", Headers));
        foreach (var p in products)
        {
            sb.AppendLine(string.Join(",", new[]
            {
                Csv(p.Sku),
                Csv(p.Name),
                Csv(p.Barcodes.FirstOrDefault()?.Code ?? ""),
                Csv(p.CategoryName ?? ""),
                (p.CostCents / 100m).ToString("0.00", CultureInfo.InvariantCulture),
                (p.PriceCents / 100m).ToString("0.00", CultureInfo.InvariantCulture),
                (p.TaxRateBasisPoints / 100m).ToString("0.##", CultureInfo.InvariantCulture),
                p.TaxInclusive ? "1" : "0",
                p.Stock.ToString("0.###", CultureInfo.InvariantCulture),
                p.MinStock.ToString("0.###", CultureInfo.InvariantCulture),
                Csv(p.Unit),
                p.IsFavorite ? "1" : "0",
                p.IsActive ? "1" : "0"
            }));
        }

        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
        return path;
    }

    public (int Created, int Updated, int Skipped) Import(string path, bool updateExisting)
    {
        if (!File.Exists(path))
        {
            throw new BusinessRuleException("import.not_found", "The CSV file was not found.");
        }

        var lines = File.ReadAllLines(path, Encoding.UTF8);
        if (lines.Length < 2)
        {
            throw new BusinessRuleException("import.empty", "The CSV file has no products.");
        }

        var header = SplitCsv(lines[0]).Select(h => h.Trim().ToLowerInvariant()).ToList();
        var indexes = Headers.ToDictionary(h => h, h => header.IndexOf(h));
        if (indexes["name"] < 0 || indexes["price"] < 0)
        {
            throw new BusinessRuleException("import.headers",
                "The CSV needs at least the columns: name, price. Download the template from Export.");
        }

        var created = 0;
        var updated = 0;
        var skipped = 0;
        var categories = _store.GetCategories().ToDictionary(c => c.Name, c => c, StringComparer.OrdinalIgnoreCase);
        var taxRates = _store.GetTaxRates();

        _store.ExecuteInTransaction(store =>
        {
            for (var i = 1; i < lines.Length; i++)
            {
                if (string.IsNullOrWhiteSpace(lines[i]))
                {
                    continue;
                }

                try
                {
                    var values = SplitCsv(lines[i]);
                    string Get(string column)
                    {
                        var index = indexes[column];
                        return index >= 0 && index < values.Count ? values[index].Trim() : "";
                    }

                    var name = Get("name");
                    if (string.IsNullOrWhiteSpace(name))
                    {
                        skipped++;
                        continue;
                    }

                    var price = ParseMoney(Get("price"));
                    var sku = Get("sku");
                    var barcode = Get("barcode");
                    Product? existing = null;
                    if (!string.IsNullOrWhiteSpace(sku))
                    {
                        existing = store.FindProductByCode(sku);
                    }

                    if (existing is null && !string.IsNullOrWhiteSpace(barcode))
                    {
                        existing = store.FindProductByCode(barcode);
                    }

                    if (existing is not null && !updateExisting)
                    {
                        skipped++;
                        continue;
                    }

                    if (existing is null && !string.IsNullOrWhiteSpace(barcode) && store.BarcodeExists(barcode))
                    {
                        skipped++;
                        continue;
                    }

                    var product = existing ?? new Product
                    {
                        CreatedAt = _clock.UtcNow,
                        Barcodes = new List<ProductBarcode>()
                    };
                    product.Name = name;
                    product.Sku = string.IsNullOrWhiteSpace(sku) ? $"P-{i:000}-{_clock.LocalNow:HHmmss}" : sku;
                    product.PriceCents = price;
                    product.CostCents = ParseMoney(Get("cost"));
                    product.TaxRateBasisPoints = ParsePercent(Get("tax_percent"));
                    var inclusive = Get("tax_inclusive");
                    product.TaxInclusive = string.IsNullOrWhiteSpace(inclusive)
                        || inclusive is "1" or "true" or "True" or "si" or "sí";
                    product.Stock = ParseQuantity(Get("stock"));
                    product.MinStock = ParseQuantity(Get("min_stock"));
                    product.Unit = string.IsNullOrWhiteSpace(Get("unit")) ? "unit" : Get("unit");
                    product.IsFavorite = Get("favorite") is "1" or "true" or "True";
                    product.IsActive = Get("active") is not ("0" or "false" or "False");
                    product.UpdatedAt = _clock.UtcNow;

                    var categoryName = Get("category");
                    if (!string.IsNullOrWhiteSpace(categoryName))
                    {
                        if (!categories.TryGetValue(categoryName, out var category))
                        {
                            category = new Category { Name = categoryName, CreatedAt = _clock.UtcNow };
                            store.SaveCategory(category);
                            categories[categoryName] = category;
                        }

                        product.CategoryId = category.Id;
                    }

                    if (!string.IsNullOrWhiteSpace(barcode) && product.Barcodes.All(b => b.Code != barcode))
                    {
                        product.Barcodes.Add(new ProductBarcode
                        {
                            ProductId = product.Id,
                            Code = barcode,
                            IsPrimary = product.Barcodes.Count == 0
                        });
                    }

                    store.SaveProduct(product);
                    if (existing is null)
                    {
                        created++;
                    }
                    else
                    {
                        updated++;
                    }
                }
                catch
                {
                    skipped++;
                }
            }

            return true;
        });

        _audit.Record(null, "product.import", "inventory", null, $"created={created};updated={updated};skipped={skipped}");
        return (created, updated, skipped);
    }

    public static string Template(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var sb = new StringBuilder();
        sb.AppendLine(string.Join(",", Headers));
        sb.AppendLine("P-001,Arroz 1 lb,746000000001,Alimentos,40.00,65.00,18,1,20,5,unit,1,1");
        sb.AppendLine("P-002,Refresco 500 ml,746000000002,Bebidas,30.00,55.00,18,1,12,6,unit,0,1");
        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
        return path;
    }

    private static string Csv(string value) =>
        value.Contains(',') || value.Contains('"') || value.Contains('\n')
            ? $"\"{value.Replace("\"", "\"\"")}\""
            : value;

    private static List<string> SplitCsv(string line)
    {
        var result = new List<string>();
        var sb = new StringBuilder();
        var inQuotes = false;
        for (var i = 0; i < line.Length; i++)
        {
            var ch = line[i];
            if (ch == '"')
            {
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                {
                    sb.Append('"');
                    i++;
                }
                else
                {
                    inQuotes = !inQuotes;
                }
            }
            else if (ch == ',' && !inQuotes)
            {
                result.Add(sb.ToString());
                sb.Clear();
            }
            else
            {
                sb.Append(ch);
            }
        }

        result.Add(sb.ToString());
        return result;
    }

    private static long ParseMoney(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return 0;
        }

        // Accept any currency prefix/suffix (RD$, US$, €, ...) and both decimal styles.
        var cleaned = new string(text.Where(ch => char.IsDigit(ch) || ch is '.' or ',' or '-').ToArray());
        if (cleaned.Length == 0)
        {
            return 0;
        }

        var lastComma = cleaned.LastIndexOf(',');
        var lastDot = cleaned.LastIndexOf('.');
        if (lastComma >= 0 && lastDot >= 0)
        {
            // The right-most separator is the decimal one.
            cleaned = lastComma > lastDot
                ? cleaned.Replace(".", "").Replace(',', '.')
                : cleaned.Replace(",", "");
        }
        else if (lastComma >= 0)
        {
            var decimals = cleaned.Length - lastComma - 1;
            cleaned = decimals is 1 or 2 ? cleaned.Replace(',', '.') : cleaned.Replace(",", "");
        }

        return decimal.TryParse(cleaned, NumberStyles.Number, CultureInfo.InvariantCulture, out var value)
            ? Money.ToCents(value)
            : 0;
    }

    private static int ParsePercent(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return 0;
        }

        text = text.Replace("%", "").Trim();
        return decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var value)
            ? (int)Math.Round(value * 100, MidpointRounding.AwayFromZero)
            : 0;
    }

    private static decimal ParseQuantity(string text) =>
        decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var value) ? value : 0;
}
