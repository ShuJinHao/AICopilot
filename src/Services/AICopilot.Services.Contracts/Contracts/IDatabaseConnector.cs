using System.Data;

namespace AICopilot.Services.Contracts;

public interface IDatabaseConnector
{
    IDbConnection GetConnection(BusinessDatabaseConnectionInfo database);

    Task<IEnumerable<dynamic>> ExecuteQueryAsync(
        BusinessDatabaseConnectionInfo database,
        string sql,
        object? parameters = null,
        CancellationToken cancellationToken = default);

    Task<DatabaseQueryResult> ExecuteQueryWithMetadataAsync(
        BusinessDatabaseConnectionInfo database,
        string sql,
        object? parameters = null,
        DatabaseQueryOptions? options = null,
        CancellationToken cancellationToken = default);

    Task<IEnumerable<dynamic>> GetSchemaInfoAsync(
        BusinessDatabaseConnectionInfo database,
        CancellationToken cancellationToken = default);
}
