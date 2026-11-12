using Dapper;
using RixelPOS.Domain.Entities;

namespace RixelPOS.Infrastructure.Data;

public sealed partial class SqliteStore
{
    public CashSession? GetOpenCashSession() =>
        WithConnection(c => c.QueryFirstOrDefault<CashSession>("""
            SELECT * FROM cash_sessions WHERE closed_at IS NULL ORDER BY opened_at DESC LIMIT 1
            """));

    public CashSession? GetCashSession(Guid id) =>
        WithConnection(c => c.QuerySingleOrDefault<CashSession>(
            "SELECT * FROM cash_sessions WHERE id = @id", new { id = id.ToString() }));

    public IReadOnlyList<CashSession> GetCashSessions(int limit = 30) =>
        WithConnection(c => c.Query<CashSession>(
            "SELECT * FROM cash_sessions ORDER BY opened_at DESC LIMIT @limit", new { limit }).ToList());

    public void SaveCashSession(CashSession session) =>
        WithConnection((c, tx) => c.Execute("""
            INSERT INTO cash_sessions
                (id, opened_at, closed_at, opening_cents, expected_cents, closing_cents, difference_cents,
                 opened_by, opened_by_name, closed_by, closed_by_name, notes)
            VALUES
                (@id, @openedAt, @closedAt, @opening, @expected, @closing, @difference,
                 @openedBy, @openedByName, @closedBy, @closedByName, @notes)
            ON CONFLICT(id) DO UPDATE SET
                closed_at = excluded.closed_at, expected_cents = excluded.expected_cents,
                closing_cents = excluded.closing_cents, difference_cents = excluded.difference_cents,
                closed_by = excluded.closed_by, closed_by_name = excluded.closed_by_name, notes = excluded.notes
            """,
            new
            {
                id = session.Id.ToString(),
                session.OpenedAt,
                session.ClosedAt,
                opening = session.OpeningCents,
                expected = session.ExpectedCents,
                closing = session.ClosingCents,
                difference = session.DifferenceCents,
                openedBy = session.OpenedBy?.ToString(),
                session.OpenedByName,
                closedBy = session.ClosedBy?.ToString(),
                session.ClosedByName,
                session.Notes
            }, tx));

    public void AddCashMovement(CashMovement movement) =>
        WithConnection((c, tx) => c.Execute("""
            INSERT INTO cash_movements(id, session_id, type, amount_cents, reason, user_id, user_name, created_at)
            VALUES(@id, @sessionId, @type, @amount, @reason, @userId, @userName, @createdAt)
            """,
            new
            {
                id = movement.Id.ToString(),
                sessionId = movement.SessionId.ToString(),
                type = movement.Type.ToString(),
                amount = movement.AmountCents,
                movement.Reason,
                userId = movement.UserId?.ToString(),
                movement.UserName,
                movement.CreatedAt
            }, tx));

    public IReadOnlyList<CashMovement> GetCashMovements(Guid sessionId) =>
        WithConnection(c => c.Query<CashMovement>(
            "SELECT * FROM cash_movements WHERE session_id = @sessionId ORDER BY created_at",
            new { sessionId = sessionId.ToString() }).ToList());

    public long GetSessionCashSales(Guid sessionId) =>
        WithConnection(c => c.ExecuteScalar<long>("""
            SELECT COALESCE(SUM(p.amount_cents), 0)
            FROM payments p INNER JOIN sales s ON s.id = p.sale_id
            WHERE s.session_id = @sessionId AND s.status = 'Completed' AND p.method = 'Cash'
            """, new { sessionId = sessionId.ToString() }));
}
