using AICopilot.Dapper.Security;
using AICopilot.Services.Contracts;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using MySql.Data.MySqlClient;
using Npgsql;
using System.Data;
using System.Diagnostics;

namespace AICopilot.Dapper;

public class DapperDatabaseConnector(
    ISqlGuardrail sqlGuardrail,
    ILogger<DapperDatabaseConnector> logger) : IDatabaseConnector
{
    public IDbConnection GetConnection(BusinessDatabaseConnectionInfo database)
    {
        var connectionString = database.ConnectionString;
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new ArgumentException(
                $"Connection string is required for data source '{database.Name}'.",
                nameof(database));
        }

        return database.Provider switch
        {
            DatabaseProviderType.PostgreSql => new NpgsqlConnection(connectionString),
            DatabaseProviderType.SqlServer => new SqlConnection(connectionString),
            DatabaseProviderType.MySql => new MySqlConnection(connectionString),
            _ => throw new NotSupportedException($"Unsupported database provider: {database.Provider}")
        };
    }

    public async Task<IEnumerable<dynamic>> ExecuteQueryAsync(
        BusinessDatabaseConnectionInfo database,
        string sql,
        object? parameters = null,
        CancellationToken cancellationToken = default)
    {
        var result = await ExecuteQueryWithMetadataAsync(
            database,
            sql,
            parameters,
            cancellationToken: cancellationToken);

        return result.Rows.Select(row => (dynamic)row).ToArray();
    }

    public async Task<DatabaseQueryResult> ExecuteQueryWithMetadataAsync(
        BusinessDatabaseConnectionInfo database,
        string sql,
        object? parameters = null,
        DatabaseQueryOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        EnsureQueryableDataSource(database);

        var guardResult = sqlGuardrail.Validate(sql, database.Provider);
        if (!guardResult.IsSafe)
        {
            logger.LogWarning("SQL security guard rejected query against {DatabaseName}: {Reason}", database.Name, guardResult.ErrorMessage);
            throw new InvalidOperationException(guardResult.ErrorMessage);
        }

        var effectiveOptions = options ?? new DatabaseQueryOptions();
        using var connection = GetConnection(database);
        var commandParameters = NormalizeParameters(parameters);
        var stopwatch = Stopwatch.StartNew();

        try
        {
            var command = new CommandDefinition(
                sql,
                commandParameters,
                commandTimeout: effectiveOptions.CommandTimeoutSeconds,
                cancellationToken: cancellationToken);

            var rawRows = (await connection.QueryAsync(command)).ToList();
            stopwatch.Stop();

            var returnedRowCount = rawRows.Count;
            var isTruncated = returnedRowCount > effectiveOptions.MaxRows;
            var normalizedRows = NormalizeRows(rawRows.Take(effectiveOptions.MaxRows));

            return new DatabaseQueryResult(
                normalizedRows,
                returnedRowCount,
                isTruncated,
                stopwatch.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            logger.LogError(ex, "SQL execution failed on database {DatabaseName}. SQL: {Sql}", database.Name, sql);
            throw;
        }
    }

    public async Task<IEnumerable<dynamic>> GetSchemaInfoAsync(
        BusinessDatabaseConnectionInfo database,
        CancellationToken cancellationToken = default)
    {
        EnsureQueryableDataSource(database);

        var sql = database.Provider switch
        {
            DatabaseProviderType.PostgreSql => @"
                SELECT table_name, table_schema
                FROM information_schema.tables
                WHERE table_schema = 'public' AND table_type = 'BASE TABLE';",
            _ => throw new NotSupportedException("Unsupported database provider")
        };

        return await ExecuteQueryAsync(database, sql, cancellationToken: cancellationToken);
    }

    private static object? NormalizeParameters(object? parameters)
    {
        return parameters switch
        {
            IReadOnlyDictionary<string, object?> readOnlyDictionary => ToDynamicParameters(readOnlyDictionary),
            IDictionary<string, object?> dictionary => ToDynamicParameters(dictionary),
            _ => parameters
        };
    }

    private static DynamicParameters ToDynamicParameters(IEnumerable<KeyValuePair<string, object?>> parameters)
    {
        var dynamicParameters = new DynamicParameters();
        foreach (var parameter in parameters)
        {
            dynamicParameters.Add(parameter.Key, parameter.Value);
        }

        return dynamicParameters;
    }

    private static List<Dictionary<string, object?>> NormalizeRows(IEnumerable<dynamic> rows)
    {
        var result = new List<Dictionary<string, object?>>();

        foreach (var row in rows)
        {
            object rowObject = row;

            if (rowObject is IDictionary<string, object?> nullableDictionary)
            {
                result.Add(nullableDictionary.ToDictionary(item => item.Key, item => item.Value, StringComparer.OrdinalIgnoreCase));
                continue;
            }

            if (rowObject is IDictionary<string, object> dictionary)
            {
                result.Add(dictionary.ToDictionary(item => item.Key, item => (object?)item.Value, StringComparer.OrdinalIgnoreCase));
                continue;
            }

            var reflected = rowObject.GetType()
                .GetProperties()
                .Where(property => property.CanRead)
                .ToDictionary(
                    property => property.Name,
                    property => property.GetValue(rowObject),
                    StringComparer.OrdinalIgnoreCase);

            result.Add(reflected);
        }

        return result;
    }

    private static void EnsureQueryableDataSource(BusinessDatabaseConnectionInfo database)
    {
        if (!database.IsEnabled)
        {
            throw new InvalidOperationException($"Data source '{database.Name}' is disabled (已被禁用).");
        }

        if (!database.IsReadOnly)
        {
            throw new InvalidOperationException($"Data source '{database.Name}' is not configured as read-only (只读模式).");
        }
    }
}

