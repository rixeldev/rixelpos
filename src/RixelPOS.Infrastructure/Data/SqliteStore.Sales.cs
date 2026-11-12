using Dapper;
using Microsoft.Data.Sqlite;
using RixelPOS.Domain.Common;
using RixelPOS.Domain.Entities;
using RixelPOS.Domain.Enums;

namespace RixelPOS.Infrastructure.Data;

public sealed partial class SqliteStore
{
    public void InsertSale(Sale sale) =>
        InTransaction((connection, transaction) =>
        {
            connection.Execute("""
                INSERT INTO sales
                    (id, number, status, cashier_id, cashier_name, customer_id, customer_name,
                     created_at, completed_at, subtotal_cents, discount_cents, tax_cents, total_cents,
                     paid_cents, change_cents, sale_discount_type, sale_discount_value, note, held_label,
                     session_id, is_credit, voided_at, voided_by, void_reason)
                VALUES
                    (@id, @number, @status, @cashierId, @cashierName, @customerId, @customerName,
                     @createdAt, @completedAt, @subtotal, @discount, @tax, @total,
                     @paid, @change, @discountType, @discountValue, @note, @heldLabel,
                     @sessionId, @isCredit, @voidedAt, @voidedBy, @voidReason)
                """,
                new
                {
                    id = sale.Id.ToString(),
                    sale.Number,
                    status = sale.Status.ToString(),
                    cashierId = sale.CashierId?.ToString(),
                    sale.CashierName,
                    customerId = sale.CustomerId?.ToString(),
                    sale.CustomerName,
                    sale.CreatedAt,
                    sale.CompletedAt,
                    subtotal = sale.SubtotalCents,
                    discount = sale.DiscountCents,
                    tax = sale.TaxCents,
                    total = sale.TotalCents,
                    paid = sale.PaidCents,
                    change = sale.ChangeCents,
                    discountType = sale.SaleDiscount.Type.ToString(),
                    discountValue = sale.SaleDiscount.Value,
                    sale.Note,
                    sale.HeldLabel,
                    sessionId = sale.CashSessionId?.ToString(),
                    sale.IsCredit,
                    sale.VoidedAt,
                    voidedBy = sale.VoidedBy?.ToString(),
                    sale.VoidReason
                }, transaction);

            foreach (var line in sale.Lines)
            {
                connection.Execute("""
                    INSERT INTO sale_lines
                        (id, sale_id, product_id, sku, name, quantity_milli, unit_price_cents, cost_cents,
                         discount_type, discount_value, net_cents, tax_cents, total_cents, tax_rate_bp, tax_inclusive)
                    VALUES
                        (@id, @saleId, @productId, @sku, @name, @quantityMilli, @unitPrice, @cost,
                         @discountType, @discountValue, @net, @tax, @total, @taxRateBp, @taxInclusive)
                    """,
                    new
                    {
                        id = line.Id.ToString(),
                        saleId = sale.Id.ToString(),
                        productId = line.ProductId?.ToString(),
                        line.Sku,
                        line.Name,
                        quantityMilli = DbConvert.ToMilli(line.Quantity),
                        unitPrice = line.UnitPriceCents,
                        cost = line.CostCents,
                        discountType = line.Discount.Type.ToString(),
                        discountValue = line.Discount.Value,
                        net = line.NetCents,
                        tax = line.TaxCents,
                        total = line.TotalCents,
                        taxRateBp = line.TaxRateBasisPoints,
                        line.TaxInclusive
                    }, transaction);
            }

            foreach (var payment in sale.Payments)
            {
                connection.Execute("""
                    INSERT INTO payments(id, sale_id, method, amount_cents, tendered_cents, reference, created_at)
                    VALUES(@id, @saleId, @method, @amount, @tendered, @reference, @createdAt)
                    """,
                    new
                    {
                        id = payment.Id.ToString(),
                        saleId = sale.Id.ToString(),
                        method = payment.Method.ToString(),
                        amount = payment.AmountCents,
                        tendered = payment.TenderedCents,
                        payment.Reference,
                        payment.CreatedAt
                    }, transaction);
            }

            return true;
        });

    public void UpdateSale(Sale sale) =>
        WithConnection((connection, transaction) => connection.Execute("""
            UPDATE sales SET
                status = @status, cashier_id = @cashierId, cashier_name = @cashierName,
                customer_id = @customerId, customer_name = @customerName,
                completed_at = @completedAt, subtotal_cents = @subtotal, discount_cents = @discount,
                tax_cents = @tax, total_cents = @total, paid_cents = @paid, change_cents = @change,
                sale_discount_type = @discountType, sale_discount_value = @discountValue,
                note = @note, held_label = @heldLabel, session_id = @sessionId, is_credit = @isCredit,
                voided_at = @voidedAt, voided_by = @voidedBy, void_reason = @voidReason
            WHERE id = @id
            """,
            new
            {
                id = sale.Id.ToString(),
                status = sale.Status.ToString(),
                cashierId = sale.CashierId?.ToString(),
                sale.CashierName,
                customerId = sale.CustomerId?.ToString(),
                sale.CustomerName,
                sale.CompletedAt,
                subtotal = sale.SubtotalCents,
                discount = sale.DiscountCents,
                tax = sale.TaxCents,
                total = sale.TotalCents,
                paid = sale.PaidCents,
                change = sale.ChangeCents,
                discountType = sale.SaleDiscount.Type.ToString(),
                discountValue = sale.SaleDiscount.Value,
                sale.Note,
                sale.HeldLabel,
                sessionId = sale.CashSessionId?.ToString(),
                sale.IsCredit,
                sale.VoidedAt,
                voidedBy = sale.VoidedBy?.ToString(),
                sale.VoidReason
            }, transaction));

    public void DeleteSale(Guid saleId) =>
        WithConnection((c, tx) => c.Execute("DELETE FROM sales WHERE id = @id", new { id = saleId.ToString() }, tx));

    public Sale? GetSale(Guid id)
    {
        var row = WithConnection(c => c.QuerySingleOrDefault<SaleRow>(
            "SELECT * FROM sales WHERE id = @id", new { id = id.ToString() }));
        return row is null ? null : LoadSales(new[] { row }).FirstOrDefault();
    }

    public Sale? GetSaleByInvoiceNumber(string invoiceNumber)
    {
        var row = WithConnection(c => c.QuerySingleOrDefault<SaleRow>("""
            SELECT s.* FROM sales s
            INNER JOIN invoices i ON i.sale_id = s.id
            WHERE i.number = @number
            """, new { number = invoiceNumber }));
        return row is null ? null : LoadSales(new[] { row }).FirstOrDefault();
    }

    public IReadOnlyList<Sale> GetRecentSales(int limit = 20) =>
        LoadSales(WithConnection(c => c.Query<SaleRow>("""
            SELECT * FROM sales WHERE status = 'Completed'
            ORDER BY completed_at DESC LIMIT @limit
            """, new { limit }).ToList()));

    public IReadOnlyList<Sale> GetHeldSales() =>
        LoadSales(WithConnection(c => c.Query<SaleRow>("""
            SELECT * FROM sales WHERE status = 'Held'
            ORDER BY created_at DESC
            """).ToList()));

    public IReadOnlyList<Sale> GetCompletedSales(DateTime from, DateTime to, int limit = 5000) =>
        LoadSales(WithConnection(c => c.Query<SaleRow>("""
            SELECT * FROM sales
            WHERE status = 'Completed' AND completed_at >= @from AND completed_at <= @to
            ORDER BY completed_at ASC LIMIT @limit
            """, new { from, to, limit }).ToList()));

    public void InsertInvoice(Invoice invoice) =>
        WithConnection((connection, transaction) => connection.Execute("""
            INSERT INTO invoices
                (id, sale_id, number, sequence, prefix, issued_at, customer_name, customer_tax_id,
                 total_cents, fiscal_data, status)
            VALUES
                (@id, @saleId, @number, @sequence, @prefix, @issuedAt, @customerName, @customerTaxId,
                 @total, @fiscalData, @status)
            """,
            new
            {
                id = invoice.Id.ToString(),
                saleId = invoice.SaleId.ToString(),
                invoice.Number,
                invoice.Sequence,
                invoice.Prefix,
                invoice.IssuedAt,
                invoice.CustomerName,
                invoice.CustomerTaxId,
                total = invoice.TotalCents,
                invoice.FiscalData,
                status = invoice.Status.ToString()
            }, transaction));

    public void UpdateInvoiceStatus(Guid saleId, InvoiceStatus status) =>
        WithConnection((c, tx) => c.Execute(
            "UPDATE invoices SET status = @status WHERE sale_id = @saleId",
            new { status = status.ToString(), saleId = saleId.ToString() }, tx));

    public Invoice? GetInvoiceForSale(Guid saleId) =>
        WithConnection(c => c.QuerySingleOrDefault<Invoice>(
            "SELECT * FROM invoices WHERE sale_id = @saleId", new { saleId = saleId.ToString() }));

    public IReadOnlyList<Invoice> GetInvoices(DateTime from, DateTime to, int limit = 200) =>
        WithConnection(c => c.Query<Invoice>("""
            SELECT * FROM invoices WHERE issued_at >= @from AND issued_at <= @to
            ORDER BY issued_at DESC LIMIT @limit
            """, new { from, to, limit }).ToList());

    private IReadOnlyList<Sale> LoadSales(IReadOnlyList<SaleRow> rows)
    {
        if (rows.Count == 0)
        {
            return Array.Empty<Sale>();
        }

        var ids = rows.Select(r => r.Id).ToList();

        WithConnection((connection, transaction) =>
        {
            var lines = connection.Query<SaleLineRow>(
                "SELECT * FROM sale_lines WHERE sale_id IN @ids ORDER BY rowid", new { ids }, transaction)
                .GroupBy(l => l.SaleId).ToDictionary(g => g.Key, g => g.ToList());
            var payments = connection.Query<Payment>(
                "SELECT * FROM payments WHERE sale_id IN @ids ORDER BY created_at", new { ids }, transaction)
                .GroupBy(p => p.SaleId.ToString()).ToDictionary(g => g.Key, g => g.ToList());
            var invoices = connection.Query<Invoice>(
                "SELECT * FROM invoices WHERE sale_id IN @ids", new { ids }, transaction)
                .GroupBy(i => i.SaleId.ToString()).ToDictionary(g => g.Key, g => g.First());

            foreach (var row in rows)
            {
                if (lines.TryGetValue(row.Id, out var lineRows))
                {
                    row.Lines = lineRows.Select(l => l.ToDomain()).ToList();
                }

                if (payments.TryGetValue(row.Id, out var paymentRows))
                {
                    row.Payments = paymentRows;
                }

                if (invoices.TryGetValue(row.Id, out var invoice))
                {
                    row.Invoice = invoice;
                }
            }

            return true;
        });

        return rows.Select(r => r.ToDomain()).ToList();
    }

    internal sealed class SaleRow
    {
        public string Id { get; set; } = "";
        public long Number { get; set; }
        public string Status { get; set; } = "Held";
        public string? CashierId { get; set; }
        public string CashierName { get; set; } = "";
        public string? CustomerId { get; set; }
        public string? CustomerName { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? CompletedAt { get; set; }
        public long SubtotalCents { get; set; }
        public long DiscountCents { get; set; }
        public long TaxCents { get; set; }
        public long TotalCents { get; set; }
        public long PaidCents { get; set; }
        public long ChangeCents { get; set; }
        public string SaleDiscountType { get; set; } = "None";
        public long SaleDiscountValue { get; set; }
        public string? Note { get; set; }
        public string? HeldLabel { get; set; }
        public string? SessionId { get; set; }
        public bool IsCredit { get; set; }
        public DateTime? VoidedAt { get; set; }
        public string? VoidedBy { get; set; }
        public string? VoidReason { get; set; }
        public List<SaleLine> Lines { get; set; } = new();
        public List<Payment> Payments { get; set; } = new();
        public Invoice? Invoice { get; set; }

        public Sale ToDomain() => new()
        {
            Id = Guid.Parse(Id),
            Number = Number,
            Status = System.Enum.TryParse<SaleStatus>(Status, true, out var status) ? status : SaleStatus.Held,
            CashierId = IdUtil.Parse(Id: CashierId),
            CashierName = CashierName,
            CustomerId = IdUtil.Parse(Id: CustomerId),
            CustomerName = CustomerName,
            CreatedAt = CreatedAt,
            CompletedAt = CompletedAt,
            SubtotalCents = SubtotalCents,
            DiscountCents = DiscountCents,
            TaxCents = TaxCents,
            TotalCents = TotalCents,
            PaidCents = PaidCents,
            ChangeCents = ChangeCents,
            SaleDiscount = new Discount(
                System.Enum.TryParse<DiscountType>(SaleDiscountType, true, out var dt) ? dt : DiscountType.None,
                SaleDiscountValue),            Note = Note,
            HeldLabel = HeldLabel,
            CashSessionId = IdUtil.Parse(Id: SessionId),
            IsCredit = IsCredit,
            VoidedAt = VoidedAt,
            VoidedBy = IdUtil.Parse(Id: VoidedBy),
            VoidReason = VoidReason,
            Lines = Lines,
            Payments = Payments,
            Invoice = Invoice
        };
    }

    internal sealed class SaleLineRow
    {
        public string Id { get; set; } = "";
        public string SaleId { get; set; } = "";
        public string? ProductId { get; set; }
        public string Sku { get; set; } = "";
        public string Name { get; set; } = "";
        public long QuantityMilli { get; set; }
        public long UnitPriceCents { get; set; }
        public long CostCents { get; set; }
        public string DiscountType { get; set; } = "None";
        public long DiscountValue { get; set; }
        public long NetCents { get; set; }
        public long TaxCents { get; set; }
        public long TotalCents { get; set; }
        public int TaxRateBp { get; set; }
        public bool TaxInclusive { get; set; }

        public SaleLine ToDomain() => new()
        {
            Id = Guid.Parse(Id),
            SaleId = Guid.Parse(SaleId),
            ProductId = IdUtil.Parse(Id: ProductId),
            Sku = Sku,
            Name = Name,
            Quantity = DbConvert.FromMilli(QuantityMilli),
            UnitPriceCents = UnitPriceCents,
            CostCents = CostCents,
            Discount = new Discount(
                System.Enum.TryParse<RixelPOS.Domain.Common.DiscountType>(DiscountType, true, out var dt) ? dt : RixelPOS.Domain.Common.DiscountType.None,
                DiscountValue),
            NetCents = NetCents,
            TaxCents = TaxCents,
            TotalCents = TotalCents,
            TaxRateBasisPoints = TaxRateBp,
            TaxInclusive = TaxInclusive
        };
    }

    private static class IdUtil
    {
        public static Guid? Parse(string? Id) =>
            string.IsNullOrWhiteSpace(Id) || !Guid.TryParse(Id, out var id) ? null : id;
    }
}
