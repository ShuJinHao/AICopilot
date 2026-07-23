namespace AICopilot.Services.Contracts;

public interface IDatabaseConnector
{
    Task<DatabaseQueryResult> ExecuteQueryWithMetadataAsync(
        BusinessDatabaseConnectionInfo database,
        string sql,
        BusinessQuerySecurityProfile securityProfile,
        object? parameters = null,
        DatabaseQueryOptions? options = null,
        CancellationToken cancellationToken = default);
}
