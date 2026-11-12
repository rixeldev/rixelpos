using RixelPOS.Application.Abstractions;
using RixelPOS.Application.Services;
using RixelPOS.Application.Sessions;
using RixelPOS.Domain.Common;
using RixelPOS.Domain.Entities;
using RixelPOS.Domain.Enums;

namespace RixelPOS.Application.Customers;

public sealed class CustomerService
{
    private readonly IPosStore _store;
    private readonly AppSession _session;
    private readonly AuditService _audit;
    private readonly IClock _clock;
    private readonly SettingsService _settings;

    public CustomerService(IPosStore store, AppSession session, AuditService audit, IClock clock, SettingsService settings)
    {
        _store = store;
        _session = session;
        _audit = audit;
        _clock = clock;
        _settings = settings;
    }

    public IReadOnlyList<Customer> Search(string? term = null) => _store.GetCustomers(term);

    public Customer? Get(Guid id) => _store.GetCustomer(id);

    public IReadOnlyList<Sale> GetHistory(Guid customerId) => _store.GetCustomerSales(customerId);

    public Customer Save(Customer customer, bool isNew)
    {
        var user = RequireUser();
        if (string.IsNullOrWhiteSpace(customer.Name))
        {
            throw new BusinessRuleException("customer.name_required", "Customer name is required.");
        }

        if (customer.CreditLimitCents < 0)
        {
            throw new BusinessRuleException("customer.invalid_credit", "Credit limit cannot be negative.");
        }

        customer.Name = customer.Name.Trim();
        customer.UpdatedAt = _clock.UtcNow;
        _store.SaveCustomer(customer);
        _audit.Record(user, isNew ? "customer.create" : "customer.update", "customer", customer.Id.ToString(), customer.Name);
        return customer;
    }

    public void AddStoreCredit(Guid customerId, long amountCents)
    {
        if (amountCents <= 0)
        {
            throw new BusinessRuleException("customer.invalid_amount", "The amount must be greater than zero.");
        }

        var user = RequireUser();
        _store.ExecuteInTransaction(store =>
        {
            var customer = store.GetCustomer(customerId)
                ?? throw new BusinessRuleException("customer.not_found", "Customer not found.");
            store.AdjustCustomerBalances(customerId, 0, amountCents);
            var session = _session.CashSession;
            if (session is not null)
            {
                store.AddCashMovement(new CashMovement
                {
                    SessionId = session.Id,
                    Type = CashMovementType.In,
                    AmountCents = amountCents,
                    Reason = $"Crédito a favor: {customer.Name}",
                    UserId = user.Id,
                    UserName = user.FullName,
                    CreatedAt = _clock.UtcNow
                });
            }

            _audit.Record(user, "customer.store_credit_add", "customer", customerId.ToString(), $"amount={amountCents}");
        });
    }

    public void PayBalance(Guid customerId, long amountCents)
    {
        if (amountCents <= 0)
        {
            throw new BusinessRuleException("customer.invalid_amount", "The amount must be greater than zero.");
        }

        var user = RequireUser();
        _store.ExecuteInTransaction(store =>
        {
            var customer = store.GetCustomer(customerId)
                ?? throw new BusinessRuleException("customer.not_found", "Customer not found.");
            if (amountCents > customer.BalanceCents)
            {
                throw new BusinessRuleException("customer.payment_too_high", "The payment exceeds the outstanding balance.");
            }

            store.AdjustCustomerBalances(customerId, -amountCents, 0);
            var session = _session.CashSession;
            if (session is not null)
            {
                store.AddCashMovement(new CashMovement
                {
                    SessionId = session.Id,
                    Type = CashMovementType.In,
                    AmountCents = amountCents,
                    Reason = $"Abono de cliente: {customer.Name}",
                    UserId = user.Id,
                    UserName = user.FullName,
                    CreatedAt = _clock.UtcNow
                });
            }

            _audit.Record(user, "customer.balance_payment", "customer", customerId.ToString(), $"amount={amountCents}");
        });
    }

    private User RequireUser() =>
        _session.CurrentUser ?? throw new BusinessRuleException("auth.required", "Please sign in first.");
}
