using RixelPOS.Application.Abstractions;
using RixelPOS.Application.Services;
using RixelPOS.Application.Sessions;
using RixelPOS.Domain.Common;
using RixelPOS.Domain.Entities;
using RixelPOS.Domain.Enums;
using RixelPOS.Domain.Reporting;

namespace RixelPOS.Application.Cash;

public sealed class CashRegisterService
{
    private readonly IPosStore _store;
    private readonly AppSession _session;
    private readonly AuditService _audit;
    private readonly IClock _clock;

    public CashRegisterService(IPosStore store, AppSession session, AuditService audit, IClock clock)
    {
        _store = store;
        _session = session;
        _audit = audit;
        _clock = clock;
    }

    public CashSession? Current
    {
        get
        {
            var open = _store.GetOpenCashSession();
            _session.CashSession = open;
            return open;
        }
    }

    public CashSession Open(long openingCents, string? notes = null)
    {
        var user = RequireUser();
        if (openingCents < 0)
        {
            throw new BusinessRuleException("cash.opening_negative", "The opening amount cannot be negative.");
        }

        if (_store.GetOpenCashSession() is not null)
        {
            throw new BusinessRuleException("cash.already_open", "The cash register is already open.");
        }

        var session = new CashSession
        {
            OpenedAt = _clock.UtcNow,
            OpeningCents = openingCents,
            OpenedBy = user.Id,
            OpenedByName = user.FullName,
            Notes = notes
        };
        _store.SaveCashSession(session);
        _session.CashSession = session;
        _session.NotifyChanged();
        _audit.Record(user, "cash.open", "cash_session", session.Id.ToString(), $"opening={openingCents}");
        return session;
    }

    public void AddMovement(CashMovementType type, long amountCents, string reason)
    {
        var user = RequireUser();
        var session = RequireSession();
        if (amountCents <= 0)
        {
            throw new BusinessRuleException("cash.invalid_amount", "The amount must be greater than zero.");
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new BusinessRuleException("cash.reason_required", "A reason is required.");
        }

        _store.AddCashMovement(new CashMovement
        {
            SessionId = session.Id,
            Type = type,
            AmountCents = amountCents,
            Reason = reason.Trim(),
            UserId = user.Id,
            UserName = user.FullName,
            CreatedAt = _clock.UtcNow
        });
        _audit.Record(user, type == CashMovementType.In ? "cash.in" : "cash.out", "cash_session", session.Id.ToString(),
            $"amount={amountCents};reason={reason}");
    }

    public ZReport Close(long countedCents, string? notes = null)
    {
        var user = RequireUser();
        var session = RequireSession();
        var report = BuildZReport(session);

        if (countedCents < 0)
        {
            throw new BusinessRuleException("cash.counted_negative", "The counted amount cannot be negative.");
        }

        session.ClosedAt = _clock.UtcNow;
        session.ExpectedCents = report.ExpectedCashCents;
        session.ClosingCents = countedCents;
        session.DifferenceCents = countedCents - report.ExpectedCashCents;
        session.ClosedBy = user.Id;
        session.ClosedByName = user.FullName;
        session.Notes = string.IsNullOrWhiteSpace(notes) ? session.Notes : notes;

        _store.SaveCashSession(session);
        _audit.Record(user, "cash.close", "cash_session", session.Id.ToString(),
            $"expected={session.ExpectedCents};counted={countedCents};difference={session.DifferenceCents}");

        _session.CashSession = null;
        _session.NotifyChanged();
        return report with { Session = session };
    }

    public ZReport BuildZReport(CashSession session)
    {
        var to = session.ClosedAt ?? _clock.UtcNow;
        var sales = _store.GetCompletedSales(session.OpenedAt, to.AddMinutes(1), 10_000)
            .Where(s => s.CashSessionId == session.Id)
            .OrderBy(s => s.CompletedAt)
            .ToList();
        var movements = _store.GetCashMovements(session.Id);

        var payments = sales.SelectMany(s => s.Payments)
            .GroupBy(p => p.Method)
            .Select(g => new PaymentTotalRow(g.Key, g.Sum(x => x.AmountCents), g.Count()))
            .OrderBy(r => r.Method)
            .ToList();

        long Sum(PaymentMethod m) => payments.FirstOrDefault(p => p.Method == m)?.TotalCents ?? 0;

        var cashIn = movements.Where(m => m.Type == CashMovementType.In).Sum(m => m.AmountCents);
        var cashOut = movements.Where(m => m.Type == CashMovementType.Out).Sum(m => m.AmountCents);
        var cashSales = Sum(PaymentMethod.Cash);
        var expected = session.OpeningCents + cashSales + cashIn - cashOut;

        return new ZReport(
            session,
            sales.Count,
            sales.Sum(s => s.TotalCents),
            cashSales,
            Sum(PaymentMethod.Card),
            Sum(PaymentMethod.Transfer),
            Sum(PaymentMethod.Credit),
            Sum(PaymentMethod.StoreCredit),
            cashIn,
            cashOut,
            expected,
            payments,
            movements,
            sales);
    }

    public IReadOnlyList<CashSession> GetHistory(int limit = 30) => _store.GetCashSessions(limit);

    public IReadOnlyList<CashMovement> GetMovements(Guid sessionId) => _store.GetCashMovements(sessionId);

    private CashSession RequireSession() =>
        _store.GetOpenCashSession() ?? throw new BusinessRuleException("cash.register_closed", "The cash register is closed.");

    private User RequireUser() =>
        _session.CurrentUser ?? throw new BusinessRuleException("auth.required", "Please sign in first.");
}
