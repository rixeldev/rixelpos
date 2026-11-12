using Dapper;
using Microsoft.Data.Sqlite;
using RixelPOS.Domain.Common;
using RixelPOS.Domain.Entities;
using RixelPOS.Domain.Enums;

namespace RixelPOS.Infrastructure.Data;

public sealed partial class SqliteStore
{
    private const string ProductSelect = """
        SELECT p.id, p.sku, p.name, p.category_id, c.name AS category_name,
               p.cost_cents, p.price_cents, p.tax_rate_bp AS tax_rate_basis_points, p.tax_inclusive,
               p.stock_milli, p.min_stock_milli, p.unit, p.is_favorite, p.is_active,
               p.track_stock, p.image_path, p.notes, p.created_at, p.updated_at
        FROM products p
        LEFT JOIN categories c ON c.id = p.category_id
        """;

    public IReadOnlyList<Product> GetProducts(bool includeInactive = false) =>
        WithConnection(c =>
        {
            var sql = ProductSelect + (includeInactive ? "" : " WHERE p.is_active = 1") + " ORDER BY p.name COLLATE NOCASE";
            var rows = c.Query<ProductRow>(sql).ToList();
            AttachBarcodes(c, null, rows);
            return (IReadOnlyList<Product>)rows.Select(r => r.ToDomain()).ToList();
        });

    public IReadOnlyList<Product> SearchProducts(string term, int limit = 30) =>
        WithConnection(c =>
        {
            var like = $"%{term.Trim()}%";
            var sql = ProductSelect + """
                 WHERE p.is_active = 1
                   AND (p.name LIKE @like COLLATE NOCASE
                        OR p.sku LIKE @like COLLATE NOCASE
                        OR EXISTS (SELECT 1 FROM product_barcodes b WHERE b.product_id = p.id AND b.code LIKE @like))
                 ORDER BY p.is_favorite DESC, p.name COLLATE NOCASE
                 LIMIT @limit
                """;
            var rows = c.Query<ProductRow>(sql, new { like, limit }).ToList();
            AttachBarcodes(c, null, rows);
            return (IReadOnlyList<Product>)rows.Select(r => r.ToDomain()).ToList();
        });

    public Product? GetProduct(Guid id) =>
        WithConnection(c =>
        {
            var row = c.QuerySingleOrDefault<ProductRow>(ProductSelect + " WHERE p.id = @id", new { id = id.ToString() });
            if (row is null)
            {
                return null;
            }

            AttachBarcodes(c, null, new[] { row });
            return row.ToDomain();
        });

    public Product? FindProductByCode(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return null;
        }

        code = code.Trim();
        return WithConnection(c =>
        {
            var row = c.QuerySingleOrDefault<ProductRow>(
                ProductSelect + """
                 WHERE p.is_active = 1
                   AND (EXISTS (SELECT 1 FROM product_barcodes b WHERE b.product_id = p.id AND b.code = @code)
                        OR p.sku = @code COLLATE NOCASE)
                 LIMIT 1
                """, new { code });
            if (row is null)
            {
                return null;
            }

            AttachBarcodes(c, null, new[] { row });
            return row.ToDomain();
        });
    }

    public IReadOnlyList<Product> GetLowStockProducts(int limit = 50) =>
        WithConnection(c =>
        {
            var rows = c.Query<ProductRow>(
                ProductSelect + """
                 WHERE p.is_active = 1 AND p.track_stock = 1 AND p.stock_milli <= p.min_stock_milli
                 ORDER BY p.stock_milli ASC, p.name COLLATE NOCASE
                 LIMIT @limit
                """, new { limit }).ToList();
            AttachBarcodes(c, null, rows);
            return (IReadOnlyList<Product>)rows.Select(r => r.ToDomain()).ToList();
        });

    public IReadOnlyList<Product> GetFavoriteProducts(int limit = 24) =>
        WithConnection(c =>
        {
            var rows = c.Query<ProductRow>(
                ProductSelect + """
                 WHERE p.is_active = 1 AND p.is_favorite = 1
                 ORDER BY p.name COLLATE NOCASE
                 LIMIT @limit
                """, new { limit }).ToList();
            AttachBarcodes(c, null, rows);
            return (IReadOnlyList<Product>)rows.Select(r => r.ToDomain()).ToList();
        });

    public bool BarcodeExists(string code, Guid? exceptProductId = null) =>
        WithConnection(c => c.ExecuteScalar<long>(
            "SELECT COUNT(1) FROM product_barcodes WHERE code = @code AND (@except IS NULL OR product_id <> @except)",
            new { code, except = exceptProductId?.ToString() }) > 0);

    public bool SkuExists(string sku, Guid? exceptProductId = null) =>
        WithConnection(c => c.ExecuteScalar<long>(
            "SELECT COUNT(1) FROM products WHERE sku = @sku COLLATE NOCASE AND (@except IS NULL OR id <> @except)",
            new { sku, except = exceptProductId?.ToString() }) > 0);

    public void SaveProduct(Product product)
    {
        InTransaction((connection, transaction) =>
        {
            try
            {
                connection.Execute("""
                    INSERT INTO products
                        (id, sku, name, category_id, cost_cents, price_cents, tax_rate_bp, tax_inclusive,
                         stock_milli, min_stock_milli, unit, is_favorite, is_active, track_stock,
                         image_path, notes, created_at, updated_at)
                    VALUES
                        (@Id, @Sku, @Name, @CategoryId, @CostCents, @PriceCents, @TaxRateBasisPoints, @TaxInclusive,
                         @StockMilli, @MinStockMilli, @Unit, @IsFavorite, @IsActive, @TrackStock,
                         @ImagePath, @Notes, @CreatedAt, @UpdatedAt)
                    ON CONFLICT(id) DO UPDATE SET
                        sku = excluded.sku, name = excluded.name, category_id = excluded.category_id,
                        cost_cents = excluded.cost_cents, price_cents = excluded.price_cents,
                        tax_rate_bp = excluded.tax_rate_bp, tax_inclusive = excluded.tax_inclusive,
                        stock_milli = excluded.stock_milli, min_stock_milli = excluded.min_stock_milli,
                        unit = excluded.unit, is_favorite = excluded.is_favorite, is_active = excluded.is_active,
                        track_stock = excluded.track_stock, image_path = excluded.image_path,
                        notes = excluded.notes, updated_at = excluded.updated_at;
                    """,
                    new
                    {
                        Id = product.Id.ToString(),
                        product.Sku,
                        product.Name,
                        CategoryId = product.CategoryId?.ToString(),
                        product.CostCents,
                        product.PriceCents,
                        product.TaxRateBasisPoints,
                        product.TaxInclusive,
                        StockMilli = DbConvert.ToMilli(product.Stock),
                        MinStockMilli = DbConvert.ToMilli(product.MinStock),
                        product.Unit,
                        product.IsFavorite,
                        product.IsActive,
                        product.TrackStock,
                        product.ImagePath,
                        product.Notes,
                        product.CreatedAt,
                        product.UpdatedAt
                    }, transaction);

                connection.Execute("DELETE FROM product_barcodes WHERE product_id = @id",
                    new { id = product.Id.ToString() }, transaction);

                foreach (var barcode in product.Barcodes.Where(b => !string.IsNullOrWhiteSpace(b.Code)))
                {
                    connection.Execute("""
                        INSERT INTO product_barcodes(id, product_id, code, is_primary)
                        VALUES(@id, @productId, @code, @isPrimary)
                        """,
                        new
                        {
                            id = barcode.Id.ToString(),
                            productId = product.Id.ToString(),
                            code = barcode.Code.Trim(),
                            barcode.IsPrimary
                        }, transaction);
                }
            }
            catch (SqliteException ex) when (ex.SqliteErrorCode == 19)
            {
                throw new BusinessRuleException("product.duplicate", "The SKU or barcode is already used by another product.");
            }

            return true;
        });
    }

    public void ArchiveProduct(Guid id) =>
        WithConnection((c, tx) => c.Execute(
            "UPDATE products SET is_active = 0, updated_at = @now WHERE id = @id",
            new { id = id.ToString(), now = DateTime.UtcNow }, tx));

    public void AdjustStock(Guid productId, decimal delta, StockMovementType type, Guid? userId,
        string? userName, string? reference = null, Guid? referenceId = null, string? note = null)
    {
        InTransaction((connection, transaction) =>
        {
            var product = connection.QuerySingleOrDefault<ProductNameRow>(
                "SELECT name AS Name, stock_milli AS StockMilli FROM products WHERE id = @id",
                new { id = productId.ToString() }, transaction);

            if (product is null)
            {
                throw new BusinessRuleException("product.not_found", "Product not found.");
            }

            connection.Execute(
                "UPDATE products SET stock_milli = stock_milli + @delta, updated_at = @now WHERE id = @id",
                new { delta = DbConvert.ToMilli(delta), now = DateTime.UtcNow, id = productId.ToString() }, transaction);

            connection.Execute("""
                INSERT INTO stock_movements
                    (id, product_id, product_name, type, quantity_milli, reference_id, reference, user_id, user_name, note, created_at)
                VALUES
                    (@id, @productId, @productName, @type, @quantityMilli, @referenceId, @reference, @userId, @userName, @note, @createdAt)
                """,
                new
                {
                    id = Guid.NewGuid().ToString(),
                    productId = productId.ToString(),
                    productName = product.Name,
                    type = type.ToString(),
                    quantityMilli = DbConvert.ToMilli(delta),
                    referenceId = referenceId?.ToString(),
                    reference,
                    userId = userId?.ToString(),
                    userName,
                    note,
                    createdAt = DateTime.UtcNow
                }, transaction);

            return true;
        });
    }

    public IReadOnlyList<StockMovement> GetStockMovements(Guid productId, int limit = 50) =>
        WithConnection(c => c.Query<StockMovementRow>("""
            SELECT id, product_id, product_name, type, quantity_milli, reference_id, reference, user_id, user_name, note, created_at
            FROM stock_movements WHERE product_id = @id
            ORDER BY created_at DESC LIMIT @limit
            """, new { id = productId.ToString(), limit })
            .Select(r => r.ToDomain())
            .ToList());

    public IReadOnlyList<Category> GetCategories() =>
        WithConnection(c => c.Query<Category>(
            "SELECT id, name, color, is_active, created_at FROM categories WHERE is_active = 1 ORDER BY name COLLATE NOCASE")
            .ToList());

    public void SaveCategory(Category category) =>
        WithConnection((c, tx) => c.Execute("""
            INSERT INTO categories(id, name, color, is_active, created_at)
            VALUES(@id, @name, @color, @isActive, @createdAt)
            ON CONFLICT(id) DO UPDATE SET name = excluded.name, color = excluded.color, is_active = excluded.is_active
            """,
            new { id = category.Id.ToString(), category.Name, category.Color, category.IsActive, category.CreatedAt }, tx));

    public IReadOnlyList<TaxRate> GetTaxRates() =>
        WithConnection(c => c.Query<TaxRate>(
            "SELECT id, name, rate_bp, is_inclusive, is_default, is_active FROM tax_rates WHERE is_active = 1 ORDER BY rate_bp")
            .ToList());

    public void SaveTaxRate(TaxRate rate) =>
        WithConnection((c, tx) => c.Execute("""
            INSERT INTO tax_rates(id, name, rate_bp, is_inclusive, is_default, is_active)
            VALUES(@id, @name, @rateBp, @isInclusive, @isDefault, @isActive)
            ON CONFLICT(id) DO UPDATE SET name = excluded.name, rate_bp = excluded.rate_bp,
                is_inclusive = excluded.is_inclusive, is_default = excluded.is_default, is_active = excluded.is_active
            """,
            new { id = rate.Id.ToString(), rate.Name, rateBp = rate.RateBasisPoints, rate.IsInclusive, rate.IsDefault, rate.IsActive }, tx));

    private static void AttachBarcodes(SqliteConnection connection, SqliteTransaction? tx, IReadOnlyList<ProductRow> rows)
    {
        if (rows.Count == 0)
        {
            return;
        }

        var ids = rows.Select(r => r.Id).ToList();
        var barcodes = connection.Query<BarcodeRow>(
            "SELECT id, product_id, code, is_primary FROM product_barcodes WHERE product_id IN @ids ORDER BY is_primary DESC",
            new { ids }, tx);
        var map = barcodes.GroupBy(b => b.ProductId).ToDictionary(g => g.Key, g => g.ToList());

        foreach (var row in rows)
        {
            if (map.TryGetValue(row.Id, out var items))
            {
                row.Barcodes = items;
            }
        }
    }

    internal sealed class ProductRow
    {
        public string Id { get; set; } = "";
        public string Sku { get; set; } = "";
        public string Name { get; set; } = "";
        public string? CategoryId { get; set; }
        public string? CategoryName { get; set; }
        public long CostCents { get; set; }
        public long PriceCents { get; set; }
        public int TaxRateBasisPoints { get; set; }
        public bool TaxInclusive { get; set; }
        public long StockMilli { get; set; }
        public long MinStockMilli { get; set; }
        public string Unit { get; set; } = "unit";
        public bool IsFavorite { get; set; }
        public bool IsActive { get; set; }
        public bool TrackStock { get; set; }
        public string? ImagePath { get; set; }
        public string? Notes { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
        public List<BarcodeRow> Barcodes { get; set; } = new();

        public Product ToDomain() => new()
        {
            Id = Guid.Parse(Id),
            Sku = Sku,
            Name = Name,
            CategoryId = string.IsNullOrWhiteSpace(CategoryId) ? null : Guid.Parse(CategoryId),
            CategoryName = CategoryName,
            CostCents = CostCents,
            PriceCents = PriceCents,
            TaxRateBasisPoints = TaxRateBasisPoints,
            TaxInclusive = TaxInclusive,
            Stock = DbConvert.FromMilli(StockMilli),
            MinStock = DbConvert.FromMilli(MinStockMilli),
            Unit = Unit,
            IsFavorite = IsFavorite,
            IsActive = IsActive,
            TrackStock = TrackStock,
            ImagePath = ImagePath,
            Notes = Notes,
            CreatedAt = CreatedAt,
            UpdatedAt = UpdatedAt,
            Barcodes = Barcodes.Select(b => b.ToDomain()).ToList()
        };
    }

    internal sealed class BarcodeRow
    {
        public string Id { get; set; } = "";
        public string ProductId { get; set; } = "";
        public string Code { get; set; } = "";
        public bool IsPrimary { get; set; }

        public ProductBarcode ToDomain() => new()
        {
            Id = Guid.Parse(Id),
            ProductId = Guid.Parse(ProductId),
            Code = Code,
            IsPrimary = IsPrimary
        };
    }

    internal sealed class ProductNameRow
    {
        public string Name { get; set; } = "";
        public long StockMilli { get; set; }
    }

    internal sealed class StockMovementRow
    {
        public string Id { get; set; } = "";
        public string ProductId { get; set; } = "";
        public string ProductName { get; set; } = "";
        public string Type { get; set; } = "Adjustment";
        public long QuantityMilli { get; set; }
        public string? ReferenceId { get; set; }
        public string? Reference { get; set; }
        public string? UserId { get; set; }
        public string? UserName { get; set; }
        public string? Note { get; set; }
        public DateTime CreatedAt { get; set; }

        public StockMovement ToDomain() => new()
        {
            Id = Guid.Parse(Id),
            ProductId = Guid.Parse(ProductId),
            ProductName = ProductName,
            Type = System.Enum.TryParse<StockMovementType>(Type, true, out var type) ? type : StockMovementType.Adjustment,
            Quantity = DbConvert.FromMilli(QuantityMilli),
            ReferenceId = ReferenceId,
            Reference = Reference,
            UserId = string.IsNullOrWhiteSpace(UserId) ? null : Guid.Parse(UserId),
            Note = Note,
            CreatedAt = CreatedAt
        };
    }
}
