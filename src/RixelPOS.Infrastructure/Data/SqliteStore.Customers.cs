using Dapper;
using RixelPOS.Domain.Entities;

namespace RixelPOS.Infrastructure.Data;

public sealed partial class SqliteStore
{
    private const string CustomerSelect = """
        SELECT id, name, phone, tax_id, address, email, credit_limit_cents, balance_cents,
               store_credit_cents, notes, is_active, created_at, updated_at
        FROM customers
        """;

    public IReadOnlyList<Customer> GetCustomers(string? search = null, int limit = 200) =>
        WithConnection(c =>
        {
            if (string.IsNullOrWhiteSpace(search))
            {
                return c.Query<Customer>(CustomerSelect + " WHERE is_active = 1 ORDER BY name COLLATE NOCASE LIMIT @limit",
                    new { limit }).ToList();
            }

            var like = $"%{search.Trim()}%";
            return c.Query<Customer>(CustomerSelect + """
                WHERE is_active = 1 AND (name LIKE @like COLLATE NOCASE OR phone LIKE @like OR tax_id LIKE @like)
                ORDER BY name COLLATE NOCASE LIMIT @limit
                """, new { like, limit }).ToList();
        });

    public Customer? GetCustomer(Guid id) =>
        WithConnection(c => c.QuerySingleOrDefault<Customer>(CustomerSelect + " WHERE id = @id",
            new { id = id.ToString() }));

    public void SaveCustomer(Customer customer) =>
        WithConnection((c, tx) => c.Execute("""
            INSERT INTO customers
                (id, name, phone, tax_id, address, email, credit_limit_cents, balance_cents,
                 store_credit_cents, notes, is_active, created_at, updated_at)
            VALUES
                (@id, @name, @phone, @taxId, @address, @email, @creditLimit, @balance, @storeCredit,
                 @notes, @isActive, @createdAt, @updatedAt)
            ON CONFLICT(id) DO UPDATE SET
                name = excluded.name, phone = excluded.phone, tax_id = excluded.tax_id,
                address = excluded.address, email = excluded.email,
                credit_limit_cents = excluded.credit_limit_cents, balance_cents = excluded.balance_cents,
                store_credit_cents = excluded.store_credit_cents, notes = excluded.notes,
                is_active = excluded.is_active, updated_at = excluded.updated_at
            """,
            new
            {
                id = customer.Id.ToString(),
                customer.Name,
                customer.Phone,
                customer.TaxId,
                customer.Address,
                customer.Email,
                creditLimit = customer.CreditLimitCents,
                balance = customer.BalanceCents,
                storeCredit = customer.StoreCreditCents,
                customer.Notes,
                customer.IsActive,
                customer.CreatedAt,
                customer.UpdatedAt
            }, tx));

    public void AdjustCustomerBalances(Guid customerId, long balanceDeltaCents, long storeCreditDeltaCents) =>
        WithConnection((c, tx) => c.Execute("""
            UPDATE customers SET
                balance_cents = balance_cents + @balanceDelta,
                store_credit_cents = store_credit_cents + @storeCreditDelta,
                updated_at = @now
            WHERE id = @id
            """,
            new
            {
                balanceDelta = balanceDeltaCents,
                storeCreditDelta = storeCreditDeltaCents,
                now = DateTime.UtcNow,
                id = customerId.ToString()
            }, tx));

    public IReadOnlyList<Sale> GetCustomerSales(Guid customerId, int limit = 50)
    {
        var rows = WithConnection(c => c.Query<SaleRow>("""
            SELECT * FROM sales WHERE customer_id = @customerId AND status = 'Completed'
            ORDER BY completed_at DESC LIMIT @limit
            """, new { customerId = customerId.ToString(), limit }).ToList());
        return LoadSales(rows);
    }
}
