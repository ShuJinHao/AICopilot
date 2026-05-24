using AICopilot.SharedKernel.Result;

namespace AICopilot.Services.Contracts;

public enum DatabaseProviderType
{
    PostgreSql = 1,
    SqlServer = 2,
    MySql = 3
}

public enum DataSourceExternalSystemType
{
    Unknown = 0,
    CloudReadOnly = 1,
    NonCloud = 2,
    SimulationBusiness = 3
}

public sealed record BusinessDatabaseDescriptor(
    Guid Id,
    string Name,
    string Description,
    DatabaseProviderType Provider,
    bool IsEnabled,
    bool IsReadOnly,
    DataSourceExternalSystemType ExternalSystemType = DataSourceExternalSystemType.Unknown,
    bool ReadOnlyCredentialVerified = false,
    string Category = "General",
    IReadOnlyCollection<string>? Tags = null,
    string OwnerDepartment = "",
    string BusinessDomain = "",
    string SensitivityLevel = "Internal",
    int DefaultQueryLimit = 200,
    int MaxQueryLimit = 1000,
    bool IsSelectableInChat = true,
    bool IsSelectableInAgent = true);

public sealed record BusinessDatabaseConnectionInfo(
    Guid Id,
    string Name,
    string Description,
    string ConnectionString,
    DatabaseProviderType Provider,
    bool IsEnabled,
    bool IsReadOnly,
    DataSourceExternalSystemType ExternalSystemType = DataSourceExternalSystemType.Unknown,
    bool ReadOnlyCredentialVerified = false,
    string Category = "General",
    IReadOnlyCollection<string>? Tags = null,
    string OwnerDepartment = "",
    string BusinessDomain = "",
    string SensitivityLevel = "Internal",
    int DefaultQueryLimit = 200,
    int MaxQueryLimit = 1000,
    bool IsSelectableInChat = true,
    bool IsSelectableInAgent = true);

public sealed record BusinessQueryColumnDto(
    string Name,
    string Type);

public sealed record BusinessQueryResultDto(
    Guid DataSourceId,
    string DataSourceName,
    string SourceType,
    DataSourceExternalSystemType SourceMode,
    bool IsSimulation,
    string SourceLabel,
    string QueryHash,
    int RowCount,
    bool IsTruncated,
    IReadOnlyList<BusinessQueryColumnDto> Columns,
    IReadOnlyList<IReadOnlyDictionary<string, object?>> Rows,
    DateTimeOffset ExecutedAt,
    long DurationMs);

public sealed record BusinessTextToSqlDraftDto(
    Guid DraftId,
    Guid DataSourceId,
    string DataSourceName,
    DataSourceExternalSystemType SourceMode,
    bool IsSimulation,
    string SourceLabel,
    string QuestionHash,
    string SqlHash,
    string SqlPreview,
    string Explanation,
    int DefaultLimit,
    int MaxLimit,
    IReadOnlyList<string> BlockedFields,
    IReadOnlyList<string> Warnings,
    DateTimeOffset CreatedAt);

public sealed record BusinessTextToSqlDraftRequest(
    Guid DataSourceId,
    string Question,
    IReadOnlyCollection<string>? BusinessDomains = null,
    int? RequestedLimit = null,
    bool PreviewOnly = true);

public sealed record BusinessTextToSqlExecuteRequest(
    Guid? DraftId = null,
    Guid? DataSourceId = null,
    string? SqlPreview = null,
    int? RequestedLimit = null);

public interface IBusinessTextToSqlRuntime
{
    Task<Result<BusinessTextToSqlDraftDto>> GenerateDraftAsync(
        BusinessTextToSqlDraftRequest request,
        CancellationToken cancellationToken = default);

    Task<Result<BusinessQueryResultDto>> ExecuteAsync(
        BusinessTextToSqlExecuteRequest request,
        CancellationToken cancellationToken = default);
}

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
