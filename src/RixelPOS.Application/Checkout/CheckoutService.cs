using RixelPOS.Application.Abstractions;
using RixelPOS.Application.Fiscal;
using RixelPOS.Application.Sales;
using RixelPOS.Application.Services;
using RixelPOS.Application.Sessions;
using RixelPOS.Domain.Common;
using RixelPOS.Domain.Entities;
using RixelPOS.Domain.Enums;
using RixelPOS.Domain.Services;

namespace RixelPOS.Application.Checkout;

public sealed class CheckoutService
{
    private readonly IPosStore _store;
    private readonly SettingsService _settings;
    private readonly AppSession _session;
    private readonly IClock _clock;
    private readonly IFiscalProviderFactory _fiscalFactory;
    private readonly AuditService _audit;
    private readonly IAppLogger _logger;

    public CheckoutService(
        IPosStore store,
        SettingsService settings,
        AppSession session,
        IClock clock,
        IFiscalProviderFactory fiscalFactory,
        AuditService audit,
        IAppLogger logger)
    {
        _store = store;
        _settings = settings;
        _session = session;
        _clock = clock;
        _fiscalFactory = fiscalFactory;
        _audit = audit;
        _logger = logger;
    }

    public Sale CompleteSale(Cart cart, IReadOnlyList<Payment> payments, Customer? customer, string? note)
    {
        var user = RequireUser();
        var cashSession = _session.CashSession ?? _store.GetOpenCashSession()
            ?? throw new BusinessRuleException("cash.register_closed", "The cash register is closed. Open a shift first.");

        var sale = cart.Recalculate();
        if (sale.Lines.Count == 0)
        {
            throw new BusinessRuleException("cart.empty", "Add at least one product before charging.");
        }

        var total = sale.TotalCents;
        var cleanedPayments = payments.Where(p => p.AmountCents > 0).ToList();
        if (cleanedPayments.Count == 0)
        {
            throw new BusinessRuleException("payment.none", "Select a payment method.");
        }

        var creditAmount = cleanedPayments.Where(p => p.Method == PaymentMethod.Credit).Sum(p => p.AmountCents);
        var storeCreditUsed = cleanedPayments.Where(p => p.Method == PaymentMethod.StoreCredit).Sum(p => p.AmountCents);
        var applied = cleanedPayments.Sum(p => p.AmountCents);

        if (applied < total)
        {
            throw new BusinessRuleException("payment.insufficient", "The payment is lower than the total.");
        }

        if (applied > total)
        {
            throw new BusinessRuleException("payment.overpaid", "The paid amount exceeds the total. Use the cash tendered field to calculate change.");
        }

        foreach (var payment in cleanedPayments.Where(p => p.Method == PaymentMethod.Cash))
        {
            if (payment.TenderedCents < payment.AmountCents)
            {
                throw new BusinessRuleException("payment.tendered_low", "Cash tendered is lower than the amount applied.");
            }
        }

        if (creditAmount > 0)
        {
            if (customer is null)
            {
                throw new BusinessRuleException("customer.required_credit", "Select a customer to sell on credit.");
            }

            if (!_settings.Current.CreditSalesEnabled)
            {
                throw new BusinessRuleException("credit.disabled", "Credit sales are disabled in settings.");
            }
        }

        if (storeCreditUsed > 0)
        {
            if (customer is null)
            {
                throw new BusinessRuleException("customer.required_store_credit", "Select a customer to use store credit.");
            }

            if (!_settings.Current.StoreCreditEnabled)
            {
                throw new BusinessRuleException("store_credit.disabled", "Store credit is disabled in settings.");
            }
        }

        var change = cleanedPayments.Where(p => p.Method == PaymentMethod.Cash)
            .Sum(p => p.TenderedCents - p.AmountCents);

        var now = _clock.UtcNow;
        sale.Status = SaleStatus.Completed;
        sale.CashierId = user.Id;
        sale.CashierName = user.FullName;
        sale.CashSessionId = cashSession.Id;
        sale.CompletedAt = now;
        sale.CustomerId = customer?.Id;
        sale.CustomerName = customer?.Name;
        sale.Note = note ?? sale.Note;
        sale.ChangeCents = change;
        sale.PaidCents = applied;
        sale.IsCredit = creditAmount > 0;

        foreach (var payment in cleanedPayments)
        {
            payment.SaleId = sale.Id;
            payment.CreatedAt = now;
        }

        sale.Payments = cleanedPayments;

        Invoice? invoice = null;

        _store.ExecuteInTransaction(store =>
        {
            var dbCustomer = customer is null ? null : store.GetCustomer(customer.Id)
                ?? throw new BusinessRuleException("customer.not_found", "The selected customer no longer exists.");

            if (dbCustomer is not null && creditAmount > 0 && !dbCustomer.CanBuyOnCredit(creditAmount))
            {
                throw new BusinessRuleException(
                    "customer.credit_limit",
                    $"Credit limit exceeded. Available: {((dbCustomer.CreditLimitCents - dbCustomer.BalanceCents) / 100m):N2}.");
            }

            if (dbCustomer is not null && storeCreditUsed > dbCustomer.StoreCreditCents)
            {
                throw new BusinessRuleException("customer.store_credit_low", "The customer does not have enough store credit.");
            }

            foreach (var line in sale.Lines.Where(l => l.ProductId is not null))
            {
                var product = store.GetProduct(line.ProductId!.Value)
                    ?? throw new BusinessRuleException("product.missing", "A product in the cart no longer exists.");

                if (product.TrackStock && !_settings.Current.AllowNegativeStock && product.Stock < line.Quantity)
                {
                    throw new BusinessRuleException(
                        "stock.insufficient",
                        $"Not enough stock for '{product.Name}'. Available: {product.Stock:0.###}.");
                }
            }

            sale.Number = store.NextSequence("sale");

            var fiscal = _fiscalFactory.Create(_settings.Current);
            var fiscalSequence = store.NextSequence("invoice");
            var invoiceNumber = InvoiceNumberFormatter.Format(_settings.Current.InvoicePrefix, fiscalSequence, _settings.Current.InvoicePadding);
            var fiscalData = fiscal.CreateInvoice(_settings.Current, sale, dbCustomer, fiscalSequence);

            invoice = new Invoice
            {
                SaleId = sale.Id,
                Number = invoiceNumber,
                Sequence = fiscalSequence,
                Prefix = _settings.Current.InvoicePrefix,
                IssuedAt = now,
                CustomerName = dbCustomer?.Name,
                CustomerTaxId = dbCustomer?.TaxId,
                TotalCents = sale.TotalCents,
                FiscalData = fiscalData.Json,
                Status = InvoiceStatus.Issued
            };
            sale.Invoice = invoice;

            store.InsertSale(sale);
            store.InsertInvoice(invoice);

            foreach (var line in sale.Lines.Where(l => l.ProductId is not null))
            {
                store.AdjustStock(
                    line.ProductId!.Value,
                    -line.Quantity,
                    StockMovementType.Sale,
                    user.Id,
                    user.FullName,
                    invoiceNumber,
                    sale.Id);
            }

            if (dbCustomer is not null && (creditAmount > 0 || storeCreditUsed > 0))
            {
                store.AdjustCustomerBalances(dbCustomer.Id, creditAmount, -storeCreditUsed);
            }

            _audit.Record(user, "sale.completed", "sale", sale.Id.ToString(),
                $"total={sale.TotalCents};invoice={invoiceNumber};credit={creditAmount};storeCredit={storeCreditUsed}");
        });

        _logger.Info($"Sale #{sale.Number} completed, invoice {invoice?.Number}, total {sale.TotalCents} cents");
        cart.Clear();
        return sale;
    }

    public Sale HoldSale(Cart cart, string? label)
    {
        var user = RequireUser();
        var sale = cart.Recalculate();
        if (sale.Lines.Count == 0)
        {
            throw new BusinessRuleException("cart.empty", "The cart is empty.");
        }

        sale.Status = SaleStatus.Held;
        sale.CashierId = user.Id;
        sale.CashierName = user.FullName;
        sale.HeldLabel = string.IsNullOrWhiteSpace(label) ? $"Venta {sale.CreatedAt.ToLocalTime():HH:mm}" : label.Trim();
        sale.CreatedAt = _clock.UtcNow;

        _store.InsertSale(sale);
        _audit.Record(user, "sale.held", "sale", sale.Id.ToString(), sale.HeldLabel);
        cart.Clear();
        return sale;
    }

    public Cart ResumeSale(Guid saleId)
    {
        RequireUser();
        var sale = _store.GetSale(saleId)
            ?? throw new BusinessRuleException("sale.not_found", "The held sale was not found.");
        if (sale.Status != SaleStatus.Held)
        {
            throw new BusinessRuleException("sale.not_held", "That sale is no longer on hold.");
        }

        _store.DeleteSale(saleId);
        return new Cart(sale);
    }

    public IReadOnlyList<Sale> GetHeldSales() => _store.GetHeldSales();

    public void DeleteHeldSale(Guid saleId)
    {
        var user = RequireUser();
        var sale = _store.GetSale(saleId);
        if (sale is null || sale.Status != SaleStatus.Held)
        {
            return;
        }

        _store.DeleteSale(saleId);
        _audit.Record(user, "sale.hold_deleted", "sale", saleId.ToString());
    }

    public void VoidSale(Guid saleId, string reason, User approver)
    {
        var user = RequireUser();
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new BusinessRuleException("void.reason_required", "A reason is required to void a sale.");
        }

        _store.ExecuteInTransaction(store =>
        {
            var sale = store.GetSale(saleId)
                ?? throw new BusinessRuleException("sale.not_found", "The sale was not found.");
            if (sale.Status != SaleStatus.Completed)
            {
                throw new BusinessRuleException("sale.not_completed", "Only completed sales can be voided.");
            }

            foreach (var line in sale.Lines.Where(l => l.ProductId is not null))
            {
                store.AdjustStock(
                    line.ProductId!.Value,
                    line.Quantity,
                    StockMovementType.Void,
                    user.Id,
                    user.FullName,
                    $"void:{sale.Number}",
                    sale.Id);
            }

            var credit = sale.Payments.Where(p => p.Method == PaymentMethod.Credit).Sum(p => p.AmountCents);
            var storeCredit = sale.Payments.Where(p => p.Method == PaymentMethod.StoreCredit).Sum(p => p.AmountCents);
            if (sale.CustomerId is not null && (credit > 0 || storeCredit > 0))
            {
                store.AdjustCustomerBalances(sale.CustomerId.Value, -credit, storeCredit);
            }

            sale.Status = SaleStatus.Voided;
            sale.VoidedAt = _clock.UtcNow;
            sale.VoidedBy = user.Id;
            sale.VoidReason = reason;
            store.UpdateSale(sale);
            store.UpdateInvoiceStatus(sale.Id, InvoiceStatus.Voided);

            _audit.Record(user, "sale.voided", "sale", sale.Id.ToString(),
                $"reason={reason};approvedBy={approver.Username};total={sale.TotalCents}");
        });

        _logger.Warn($"Sale {saleId} voided by {user.Username}, approved by {approver.Username}");
    }

    private User RequireUser() =>
        _session.CurrentUser ?? throw new BusinessRuleException("auth.required", "Please sign in first.");
}
