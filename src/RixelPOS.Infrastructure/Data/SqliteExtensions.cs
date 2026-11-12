using System.Data;
using System.Text.RegularExpressions;
using Dapper;
using Microsoft.Data.Sqlite;

namespace RixelPOS.Infrastructure.Data;

/// <summary>
/// Dapper helpers scoped to SQLite. Microsoft.Data.Sqlite matches parameter
/// names case-sensitively, so we normalize C# property names (UserName) to the
/// exact placeholder casing used in SQL (userName) before binding.
/// </summary>
internal static class SqliteExtensions
{
    private static readonly Regex ParameterPattern = new(@"[@:$]([A-Za-z_][A-Za-z0-9_]*)", RegexOptions.Compiled);

    private static object? Normalize(object? parameters, string sql)
    {
        if (parameters is null or DynamicParameters)
        {
            return parameters;
        }

        var properties = parameters.GetType().GetProperties()
            .ToDictionary(p => p.Name, p => p, StringComparer.OrdinalIgnoreCase);
        var dynamicParameters = new DynamicParameters();
        var added = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (Match match in ParameterPattern.Matches(sql))
        {
            var name = match.Groups[1].Value;
            if (!added.Add(name))
            {
                continue;
            }

            if (properties.TryGetValue(name, out var property))
            {
                dynamicParameters.Add("@" + name, property.GetValue(parameters));
            }
        }

        return dynamicParameters;
    }

    public static int Execute(this SqliteConnection connection, string sql, object? param = null,
        IDbTransaction? transaction = null) =>
        SqlMapper.Execute(connection, sql, Normalize(param, sql), transaction);

    public static T? ExecuteScalar<T>(this SqliteConnection connection, string sql, object? param = null,
        IDbTransaction? transaction = null) =>
        SqlMapper.ExecuteScalar<T>(connection, sql, Normalize(param, sql), transaction);

    public static IEnumerable<T> Query<T>(this SqliteConnection connection, string sql, object? param = null,
        IDbTransaction? transaction = null) =>
        SqlMapper.Query<T>(connection, sql, Normalize(param, sql), transaction);

    public static T? QuerySingleOrDefault<T>(this SqliteConnection connection, string sql, object? param = null,
        IDbTransaction? transaction = null) =>
        SqlMapper.QuerySingleOrDefault<T>(connection, sql, Normalize(param, sql), transaction);

    public static T? QueryFirstOrDefault<T>(this SqliteConnection connection, string sql, object? param = null,
        IDbTransaction? transaction = null) =>
        SqlMapper.QueryFirstOrDefault<T>(connection, sql, Normalize(param, sql), transaction);
}
