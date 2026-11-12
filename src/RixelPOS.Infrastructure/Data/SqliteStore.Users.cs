using Dapper;
using RixelPOS.Domain.Entities;

namespace RixelPOS.Infrastructure.Data;

public sealed partial class SqliteStore
{
    public IReadOnlyList<User> GetUsers() =>
        WithConnection(c => c.Query<User>("""
            SELECT id, username, full_name, role, pin_hash, password_hash, is_active, created_at, updated_at
            FROM users ORDER BY full_name COLLATE NOCASE
            """).ToList());

    public User? GetUser(Guid id) =>
        WithConnection(c => c.QuerySingleOrDefault<User>("""
            SELECT id, username, full_name, role, pin_hash, password_hash, is_active, created_at, updated_at
            FROM users WHERE id = @id
            """, new { id = id.ToString() }));

    public User? FindUserByUsername(string username) =>
        WithConnection(c => c.QuerySingleOrDefault<User>("""
            SELECT id, username, full_name, role, pin_hash, password_hash, is_active, created_at, updated_at
            FROM users WHERE username = @username COLLATE NOCASE
            """, new { username }));

    public IReadOnlyList<User> GetUsersWithPin() =>
        WithConnection(c => c.Query<User>("""
            SELECT id, username, full_name, role, pin_hash, password_hash, is_active, created_at, updated_at
            FROM users WHERE pin_hash IS NOT NULL AND is_active = 1
            ORDER BY role DESC, full_name COLLATE NOCASE
            """).ToList());

    public void SaveUser(User user) =>
        WithConnection((c, tx) => c.Execute("""
            INSERT INTO users(id, username, full_name, role, pin_hash, password_hash, is_active, created_at, updated_at)
            VALUES(@id, @username, @fullName, @role, @pinHash, @passwordHash, @isActive, @createdAt, @updatedAt)
            ON CONFLICT(id) DO UPDATE SET
                username = excluded.username, full_name = excluded.full_name, role = excluded.role,
                pin_hash = excluded.pin_hash, password_hash = excluded.password_hash,
                is_active = excluded.is_active, updated_at = excluded.updated_at
            """,
            new
            {
                id = user.Id.ToString(),
                user.Username,
                user.FullName,
                role = user.Role.ToString(),
                user.PinHash,
                user.PasswordHash,
                user.IsActive,
                user.CreatedAt,
                user.UpdatedAt
            }, tx));

    public int CountAdmins() =>
        WithConnection(c => c.ExecuteScalar<int>(
            "SELECT COUNT(1) FROM users WHERE role = 'Admin' AND is_active = 1"));
}
