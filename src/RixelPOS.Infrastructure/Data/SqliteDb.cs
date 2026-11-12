using System.Globalization;
using System.Reflection;
using Dapper;
using Microsoft.Data.Sqlite;
using RixelPOS.Domain.Common;

namespace RixelPOS.Infrastructure.Data;

public sealed class SqliteDb
{
    private static readonly object InitLock = new();
    private static bool _handlersRegistered;

    public SqliteDb(string databasePath)
    {
        DatabasePath = Path.GetFullPath(databasePath);
        ConnectionString = new SqliteConnectionStringBuilder
        {
            DataSource = DatabasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            ForeignKeys = true,
            Pooling = true
        }.ToString();

        RegisterHandlers();
    }

    public string DatabasePath { get; }

    public string ConnectionString { get; }

    public SqliteConnection Open()
    {
        var connection = new SqliteConnection(ConnectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA foreign_keys=ON; PRAGMA busy_timeout=5000; PRAGMA synchronous=NORMAL;";
        command.ExecuteNonQuery();
        return connection;
    }

    public void Initialize()
    {
        lock (InitLock)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(DatabasePath)!);
            using var connection = Open();

            using (var pragma = connection.CreateCommand())
            {
                pragma.CommandText = "PRAGMA journal_mode=WAL; PRAGMA wal_autocheckpoint=1000;";
                pragma.ExecuteNonQuery();
            }

            using (var create = connection.CreateCommand())
            {
                create.CommandText = """
                    CREATE TABLE IF NOT EXISTS schema_migrations (
                        version INTEGER PRIMARY KEY,
                        applied_at TEXT NOT NULL
                    );
                    """;
                create.ExecuteNonQuery();
            }

            var applied = connection.Query<long>("SELECT version FROM schema_migrations").ToHashSet();
            foreach (var (version, name, sql) in LoadMigrations())
            {
                if (applied.Contains(version))
                {
                    continue;
                }

                using var transaction = connection.BeginTransaction();
                using (var command = connection.CreateCommand())
                {
                    command.Transaction = transaction;
                    command.CommandText = sql;
                    command.ExecuteNonQuery();
                }

                using (var record = connection.CreateCommand())
                {
                    record.Transaction = transaction;
                    record.CommandText = "INSERT INTO schema_migrations(version, applied_at) VALUES (@v, @at)";
                    record.Parameters.AddWithValue("@v", version);
                    record.Parameters.AddWithValue("@at", DateTime.UtcNow.ToString("O"));
                    record.ExecuteNonQuery();
                }

                transaction.Commit();
            }
        }
    }

    public string CreateSnapshot(string destinationPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
        if (File.Exists(destinationPath))
        {
            File.Delete(destinationPath);
        }

        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "VACUUM INTO @path";
        command.Parameters.AddWithValue("@path", destinationPath);
        command.ExecuteNonQuery();
        return destinationPath;
    }

    public void Checkpoint()
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
        command.ExecuteNonQuery();
    }

    private static IEnumerable<(long Version, string Name, string Sql)> LoadMigrations()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resources = assembly.GetManifestResourceNames()
            .Where(n => n.EndsWith(".sql", StringComparison.OrdinalIgnoreCase))
            .OrderBy(n => n, StringComparer.Ordinal);

        foreach (var resource in resources)
        {
            using var stream = assembly.GetManifestResourceStream(resource)
                ?? throw new InvalidOperationException($"Migration resource {resource} was not found.");
            using var reader = new StreamReader(stream);
            var match = System.Text.RegularExpressions.Regex.Match(resource, @"\.(\d+)_[^.]*\.sql$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (!match.Success || !long.TryParse(match.Groups[1].Value, out var version))
            {
                continue;
            }

            yield return (version, Path.GetFileName(resource), reader.ReadToEnd());
        }
    }

    private static void RegisterHandlers()
    {
        if (_handlersRegistered)
        {
            return;
        }

        _handlersRegistered = true;
        DefaultTypeMap.MatchNamesWithUnderscores = true;
        SqlMapper.AddTypeHandler(new UtcDateTimeHandler());
        SqlMapper.AddTypeHandler(new NullableUtcDateTimeHandler());
        SqlMapper.AddTypeHandler(new GuidHandler());
        SqlMapper.AddTypeHandler(new NullableGuidHandler());
    }

    private sealed class GuidHandler : SqlMapper.TypeHandler<Guid>
    {
        public override Guid Parse(object value) => Guid.Parse(value.ToString()!);
        public override void SetValue(System.Data.IDbDataParameter parameter, Guid value) => parameter.Value = value.ToString();
    }

    private sealed class NullableGuidHandler : SqlMapper.TypeHandler<Guid?>
    {
        public override Guid? Parse(object value) =>
            value is null or DBNull ? null : Guid.Parse(value.ToString()!);

        public override void SetValue(System.Data.IDbDataParameter parameter, Guid? value) =>
            parameter.Value = value is null ? DBNull.Value : value.Value.ToString();
    }

    private sealed class UtcDateTimeHandler : SqlMapper.TypeHandler<DateTime>
    {
        public override DateTime Parse(object value) => ParseDate(value);

        public override void SetValue(System.Data.IDbDataParameter parameter, DateTime value) =>
            parameter.Value = value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

        internal static DateTime ParseDate(object value) => value switch
        {
            DateTime dt => dt.Kind == DateTimeKind.Utc ? dt : dt.ToUniversalTime(),
            string s => DateTime.Parse(s, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal).ToUniversalTime(),
            long ms => DateTimeOffset.FromUnixTimeMilliseconds(ms).UtcDateTime,
            _ => Convert.ToDateTime(value, CultureInfo.InvariantCulture).ToUniversalTime()
        };
    }

    private sealed class NullableUtcDateTimeHandler : SqlMapper.TypeHandler<DateTime?>
    {
        public override DateTime? Parse(object value) => value is null or DBNull ? null : UtcDateTimeHandler.ParseDate(value);

        public override void SetValue(System.Data.IDbDataParameter parameter, DateTime? value) =>
            parameter.Value = value is null ? DBNull.Value : value.Value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
    }
}

internal sealed class DbSession : IDisposable
{
    public DbSession(SqliteConnection connection, SqliteTransaction? transaction, bool ownsConnection)
    {
        Connection = connection;
        Transaction = transaction;
        OwnsConnection = ownsConnection;
    }

    public SqliteConnection Connection { get; }
    public SqliteTransaction? Transaction { get; }
    private bool OwnsConnection { get; }

    public void Dispose()
    {
        Transaction?.Dispose();
        if (OwnsConnection)
        {
            Connection.Dispose();
        }
    }
}

internal static class DbConvert
{
    public static long ToMilli(decimal quantity) => (long)Math.Round(quantity * 1000m, MidpointRounding.AwayFromZero);
    public static decimal FromMilli(long milli) => milli / 1000m;
}
