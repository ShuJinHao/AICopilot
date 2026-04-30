using AICopilot.Dapper.Security;
using AICopilot.Services.Contracts;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using MySql.Data.MySqlClient;
using Npgsql;
using System.Data;
using System.Data.Common;
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
        var resetPostgreSqlReadOnlySession = false;

        try
        {
            await OpenConnectionAsync(connection, cancellationToken);
            resetPostgreSqlReadOnlySession = await ConfigureReadOnlySessionAsync(
                connection,
                database.Provider,
                cancellationToken);
            using var transaction = await BeginTransactionAsync(connection, cancellationToken);
            await ConfigureReadOnlyTransactionAsync(connection, transaction, database.Provider, cancellationToken);

            var command = new CommandDefinition(
                sql,
                commandParameters,
                transaction,
                commandTimeout: effectiveOptions.CommandTimeoutSeconds,
                cancellationToken: cancellationToken);

            var maxRows = Math.Max(1, effectiveOptions.MaxRows);
            var normalizedRows = new List<Dictionary<string, object?>>(maxRows);
            var isTruncated = false;

            using (var reader = await connection.ExecuteReaderAsync(command))
            {
                while (await ReadAsync(reader, cancellationToken))
                {
                    if (normalizedRows.Count >= maxRows)
                    {
                        isTruncated = true;
                        break;
                    }

                    normalizedRows.Add(NormalizeRecord(reader));
                }
            }

            await CommitTransactionAsync(transaction, cancellationToken);
            stopwatch.Stop();

            var returnedRowCount = normalizedRows.Count + (isTruncated ? 1 : 0);

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
        finally
        {
            if (resetPostgreSqlReadOnlySession)
            {
                await ResetReadOnlySessionAsync(connection, database.Provider, CancellationToken.None);
            }
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

    private static Dictionary<string, object?> NormalizeRecord(IDataRecord record)
    {
        var result = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < record.FieldCount; index++)
        {
            var value = record.GetValue(index);
            result[record.GetName(index)] = value == DBNull.Value ? null : value;
        }

        return result;
    }

    private static async Task OpenConnectionAsync(IDbConnection connection, CancellationToken cancellationToken)
    {
        if (connection is DbConnection dbConnection)
        {
            await dbConnection.OpenAsync(cancellationToken);
            return;
        }

        connection.Open();
    }

    private static async Task<IDbTransaction> BeginTransactionAsync(
        IDbConnection connection,
        CancellationToken cancellationToken)
    {
        if (connection is DbConnection dbConnection)
        {
            return await dbConnection.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        }

        return connection.BeginTransaction(IsolationLevel.ReadCommitted);
    }

    private static async Task<bool> ConfigureReadOnlySessionAsync(
        IDbConnection connection,
        DatabaseProviderType provider,
        CancellationToken cancellationToken)
    {
        if (provider != DatabaseProviderType.PostgreSql)
        {
            return false;
        }

        await ExecuteNonQueryAsync(
            connection,
            transaction: null,
            "SET SESSION CHARACTERISTICS AS TRANSACTION READ ONLY",
            cancellationToken);
        return true;
    }

    private static async Task ConfigureReadOnlyTransactionAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        DatabaseProviderType provider,
        CancellationToken cancellationToken)
    {
        if (provider != DatabaseProviderType.PostgreSql)
        {
            return;
        }

        await ExecuteNonQueryAsync(
            connection,
            transaction,
            "SET TRANSACTION READ ONLY",
            cancellationToken);
    }

    private static async Task ResetReadOnlySessionAsync(
        IDbConnection connection,
        DatabaseProviderType provider,
        CancellationToken cancellationToken)
    {
        if (provider != DatabaseProviderType.PostgreSql || connection.State != ConnectionState.Open)
        {
            return;
        }

        await ExecuteNonQueryAsync(
            connection,
            transaction: null,
            "SET SESSION CHARACTERISTICS AS TRANSACTION READ WRITE",
            cancellationToken);
    }

    private static async Task ExecuteNonQueryAsync(
        IDbConnection connection,
        IDbTransaction? transaction,
        string sql,
        CancellationToken cancellationToken)
    {
        var command = new CommandDefinition(
            sql,
            transaction: transaction,
            cancellationToken: cancellationToken);
        await connection.ExecuteAsync(command);
    }

    private static async Task CommitTransactionAsync(
        IDbTransaction transaction,
        CancellationToken cancellationToken)
    {
        if (transaction is DbTransaction dbTransaction)
        {
            await dbTransaction.CommitAsync(cancellationToken);
            return;
        }

        transaction.Commit();
    }

    private static async Task<bool> ReadAsync(IDataReader reader, CancellationToken cancellationToken)
    {
        if (reader is DbDataReader dbDataReader)
        {
            return await dbDataReader.ReadAsync(cancellationToken);
        }

        return reader.Read();
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

