using System.Globalization;
using System.Text;
using RixelPOS.Application.Abstractions;
using RixelPOS.Application.Services;
using RixelPOS.Application.Sessions;
using RixelPOS.Domain.Common;
using RixelPOS.Domain.Entities;
using RixelPOS.Domain.Enums;

namespace RixelPOS.Application.Inventory;

public sealed record ReceivingRequest(Guid? ProductId, decimal Quantity, Product? NewProduct = null, string? Note = null);

public sealed record ReceivingResult(int CreatedProducts, int UpdatedProducts, decimal TotalUnits);

public sealed record StockCountScanResult(StockCount Count, StockCountLine Line);

public sealed class InventoryService
{
    private readonly IPosStore _store;
    private readonly SettingsService _settings;
    private readonly AppSession _session;
    private readonly AuditService _audit;
    private readonly IClock _clock;

    public InventoryService(IPosStore store, SettingsService settings, AppSession session, AuditService audit, IClock clock)
    {
        _store = store;
        _settings = settings;
        _session = session;
        _audit = audit;
        _clock = clock;
    }

    public IReadOnlyList<Product> Search(string? term = null)
    {
        if (string.IsNullOrWhiteSpace(term))
        {
            return _store.GetProducts();
        }

        return _store.SearchProducts(term.Trim(), 100);
    }

    public Product? FindByCode(string code) => _store.FindProductByCode(code.Trim());

    public Product? GetProduct(Guid id) => _store.GetProduct(id);

    public IReadOnlyList<Product> GetLowStock() => _store.GetLowStockProducts();

    public IReadOnlyList<Category> GetCategories() => _store.GetCategories();

    public IReadOnlyList<TaxRate> GetTaxRates() => _store.GetTaxRates();

    /// <summary>
    /// Builds a readable, unique SKU from the product name and barcode. Falls back to a
    /// numeric suffix when the base code is already taken.
    /// </summary>
    public string GenerateSku(string name, string? barcode = null, Guid? exceptProductId = null)
    {
        var slug = Slug(name);
        if (slug.Length == 0)
        {
            slug = "PROD";
        }

        if (slug.Length > 12)
        {
            slug = slug[..12];
        }

        var code = new string((barcode ?? "").Where(char.IsLetterOrDigit).ToArray());
        if (code.Length > 8)
        {
            code = code[^8..];
        }

        var baseSku = code.Length == 0 ? slug : $"{slug}-{code}";
        var candidate = baseSku;
        var suffix = 2;
        while (_store.SkuExists(candidate, exceptProductId))
        {
            candidate = $"{baseSku}-{suffix++}";
        }

        return candidate;
    }

    private static string Slug(string value)
    {
        var normalized = value.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(normalized.Length);
        foreach (var ch in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsLetterOrDigit(ch))
            {
                sb.Append(char.ToUpperInvariant(ch));
            }
            else if (sb.Length > 0 && sb[^1] != '-')
            {
                sb.Append('-');
            }
        }

        return sb.ToString().Trim('-');
    }

    public void CreateCategory(string name)
    {
        var user = RequireUser();
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new BusinessRuleException("category.name_required", "Category name is required.");
        }

        var category = new Category { Name = name.Trim(), CreatedAt = _clock.UtcNow };
        _store.SaveCategory(category);
        _audit.Record(user, "category.create", "category", category.Id.ToString(), category.Name);
    }

    public void SaveProduct(Product product, bool isNew)
    {
        var user = RequireUser();
        if (string.IsNullOrWhiteSpace(product.Name))
        {
            throw new BusinessRuleException("product.name_required", "Product name is required.");
        }

        if (product.PriceCents < 0 || product.CostCents < 0)
        {
            throw new BusinessRuleException("product.invalid_price", "Prices cannot be negative.");
        }

        if (!string.IsNullOrWhiteSpace(product.Sku) && _store.SkuExists(product.Sku, product.Id))
        {
            throw new BusinessRuleException("product.sku_exists", "That SKU is already used by another product.");
        }

        if (string.IsNullOrWhiteSpace(product.Sku))
        {
            product.Sku = $"P-{_clock.LocalNow:yyMMddHHmmssfff}";
        }

        foreach (var barcode in product.Barcodes.Where(b => !string.IsNullOrWhiteSpace(b.Code)))
        {
            if (_store.BarcodeExists(barcode.Code.Trim(), product.Id))
            {
                throw new BusinessRuleException("product.barcode_exists", $"The barcode {barcode.Code} is already assigned.");
            }

            barcode.Code = barcode.Code.Trim();
            barcode.ProductId = product.Id;
        }

        product.UpdatedAt = _clock.UtcNow;
        var desiredStock = product.Stock;

        _store.ExecuteInTransaction(store =>
        {
            var existing = isNew ? null : store.GetProduct(product.Id);
            if (existing is not null)
            {
                product.Stock = existing.Stock;
            }
            else
            {
                product.Stock = 0;
            }

            store.SaveProduct(product);
            if (isNew && desiredStock != 0)
            {
                store.AdjustStock(product.Id, desiredStock,
                    desiredStock > 0 ? StockMovementType.Initial : StockMovementType.Adjustment,
                    user.Id, user.FullName, "Stock inicial", null, "Inventario inicial");
            }

            return true;
        });

        _audit.Record(user, isNew ? "product.create" : "product.update", "product", product.Id.ToString(), product.Name);
    }

    public void ArchiveProduct(Guid productId)
    {
        var user = RequireUser();
        _store.ArchiveProduct(productId);
        _audit.Record(user, "product.archive", "product", productId.ToString());
    }

    public void AdjustStock(Guid productId, decimal delta, string? reason)
    {
        var user = RequireUser();
        if (delta == 0)
        {
            return;
        }

        _store.ExecuteInTransaction(store =>
        {
            var product = store.GetProduct(productId)
                ?? throw new BusinessRuleException("product.not_found", "Product not found.");
            if (product.TrackStock && !_settings.Current.AllowNegativeStock && product.Stock + delta < 0)
            {
                throw new BusinessRuleException("stock.would_be_negative", "The adjustment would leave negative stock.");
            }

            store.AdjustStock(productId, delta, StockMovementType.Adjustment, user.Id, user.FullName, reason);
        });

        _audit.Record(user, "stock.adjust", "product", productId.ToString(), $"delta={delta};reason={reason}");
    }

    /// <summary>
    /// Applies a whole receiving batch atomically. If any row is invalid, nothing is saved.
    /// </summary>
    public ReceivingResult ReceiveBatch(IReadOnlyList<ReceivingRequest> requests)
    {
        var user = RequireUser();
        if (requests.Count == 0)
        {
            throw new BusinessRuleException("receiving.empty", "There are no items to receive.");
        }

        var created = 0;
        var updated = 0;
        decimal units = 0;

        _store.ExecuteInTransaction(store =>
        {
            foreach (var request in requests)
            {
                if (request.Quantity <= 0)
                {
                    throw new BusinessRuleException("receiving.invalid_quantity", "Every received quantity must be greater than zero.");
                }

                Guid productId;
                if (request.ProductId is not null)
                {
                    productId = request.ProductId.Value;
                    if (store.GetProduct(productId) is null)
                    {
                        throw new BusinessRuleException("product.not_found", "A product in the receiving batch no longer exists.");
                    }

                    updated++;
                }
                else
                {
                    var product = request.NewProduct
                        ?? throw new BusinessRuleException("receiving.product_required", "New products need a name and a price.");
                    if (string.IsNullOrWhiteSpace(product.Name))
                    {
                        throw new BusinessRuleException("product.name_required", "Product name is required.");
                    }

                    if (string.IsNullOrWhiteSpace(product.Sku))
                    {
                        product.Sku = $"P-{_clock.LocalNow:yyMMddHHmmssfff}";
                    }

                    if (store.SkuExists(product.Sku, product.Id))
                    {
                        throw new BusinessRuleException("product.sku_exists", "That SKU is already used by another product.");
                    }

                    foreach (var barcode in product.Barcodes.Where(b => !string.IsNullOrWhiteSpace(b.Code)))
                    {
                        if (store.BarcodeExists(barcode.Code.Trim(), product.Id))
                        {
                            throw new BusinessRuleException("product.barcode_exists", $"The barcode {barcode.Code} already exists.");
                        }

                        barcode.Code = barcode.Code.Trim();
                        barcode.ProductId = product.Id;
                    }

                    product.Stock = 0;
                    product.CreatedAt = _clock.UtcNow;
                    product.UpdatedAt = _clock.UtcNow;
                    store.SaveProduct(product);
                    productId = product.Id;
                    created++;
                }

                store.AdjustStock(productId, request.Quantity, StockMovementType.Purchase, user.Id, user.FullName,
                    "recepción", null, request.Note);
                units += request.Quantity;
            }

            _audit.Record(user, "stock.receive_batch", "inventory", null,
                $"created={created};updated={updated};units={units}");
        });

        return new ReceivingResult(created, updated, units);
    }

    public StockCount StartStockCount(string? notes)
    {
        var user = RequireUser();
        if (_store.GetOpenStockCount() is not null)
        {
            throw new BusinessRuleException("stock_count.already_open", "There is already an open stock count.");
        }

        var count = _store.CreateStockCount(user.Id, user.FullName, notes);
        _audit.Record(user, "stock_count.start", "stock_count", count.Id.ToString());
        return count;
    }

    public StockCount? GetOpenStockCount() => _store.GetOpenStockCount();

    public StockCountScanResult? ScanStockCount(Guid countId, string code)
    {
        var product = _store.FindProductByCode(code.Trim());
        if (product is null)
        {
            return null;
        }

        _store.AddStockCountLine(countId, product.Id, 1);
        var count = _store.GetOpenStockCount()
            ?? throw new BusinessRuleException("stock_count.closed", "The stock count was closed.");
        var line = count.Lines.FirstOrDefault(l => l.ProductId == product.Id)
            ?? throw new BusinessRuleException("stock_count.line_missing", "The counted item could not be saved.");
        return new StockCountScanResult(count, line);
    }

    public void CloseStockCount(Guid countId, bool applyAdjustments)
    {
        var user = RequireUser();
        _store.CloseStockCount(countId, user.Id, user.FullName, applyAdjustments);
        _audit.Record(user, "stock_count.close", "stock_count", countId.ToString(), $"apply={applyAdjustments}");
    }

    public string CreateInternalBarcode(Guid productId)
    {
        var user = RequireUser();
        var product = _store.GetProduct(productId)
            ?? throw new BusinessRuleException("product.not_found", "Product not found.");

        for (var attempt = 0; attempt < 50; attempt++)
        {
            var sequence = _store.NextSequence("internal_barcode");
            var code = BarcodeGenerator.BuildEan13("200", sequence);
            if (_store.BarcodeExists(code, productId))
            {
                continue;
            }

            product.Barcodes.Add(new ProductBarcode { ProductId = product.Id, Code = code, IsPrimary = !product.Barcodes.Any() });
            product.UpdatedAt = _clock.UtcNow;
            _store.SaveProduct(product);
            _audit.Record(user, "product.internal_barcode", "product", productId.ToString(), code);
            return code;
        }

        throw new BusinessRuleException("barcode.generation_failed", "Could not generate a unique internal barcode.");
    }

    private User RequireUser() =>
        _session.CurrentUser ?? throw new BusinessRuleException("auth.required", "Please sign in first.");
}
