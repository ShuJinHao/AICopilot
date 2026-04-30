namespace AICopilot.Services.Contracts;

public enum DatabaseProviderType
{
    PostgreSql = 1,
    SqlServer = 2,
    MySql = 3
}

public sealed record BusinessDatabaseDescriptor(
    Guid Id,
    string Name,
    string Description,
    DatabaseProviderType Provider,
    bool IsEnabled,
    bool IsReadOnly);

public sealed record BusinessDatabaseConnectionInfo(
    Guid Id,
    string Name,
    string Description,
    string ConnectionString,
    DatabaseProviderType Provider,
    bool IsEnabled,
    bool IsReadOnly);

public interface IBusinessDatabaseReadService
{
    Task<IReadOnlyList<BusinessDatabaseDescriptor>> ListEnabledAsync(
        CancellationToken cancellationToken = default);

    Task<BusinessDatabaseConnectionInfo?> GetByNameAsync(
        string name,
        CancellationToken cancellationToken = default);
}

public sealed record KnowledgeBaseDescriptor(
    Guid Id,
    string Name,
    string Description);

public interface IKnowledgeBaseReadService
{
    Task<IReadOnlyList<KnowledgeBaseDescriptor>> ListAsync(
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<KnowledgeBaseDescriptor>> GetByNamesAsync(
        IReadOnlyCollection<string> names,
        CancellationToken cancellationToken = default);
}
