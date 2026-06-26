using AICopilot.Core.DataAnalysis.Aggregates.BusinessDatabase;
using AICopilot.Services.Contracts;
using AICopilot.Services.CrossCutting.Attributes;
using AICopilot.SharedKernel.Messaging;
using AICopilot.SharedKernel.Result;

namespace AICopilot.DataAnalysisService.BusinessDatabases;

public record BusinessDatabaseDto
{
    public Guid Id { get; init; }
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required DbProviderType Provider { get; init; }
    public bool IsEnabled { get; init; }
    public bool IsReadOnly { get; init; }
    public required DataSourceExternalSystemType ExternalSystemType { get; init; }
    public bool ReadOnlyCredentialVerified { get; init; }
    public DateTime CreatedAt { get; init; }
    public bool HasConnectionString { get; init; }
    public string? ConnectionStringMasked { get; init; }
    public string Category { get; init; } = "General";
    public IReadOnlyCollection<string> Tags { get; init; } = [];
    public string OwnerDepartment { get; init; } = string.Empty;
    public string BusinessDomain { get; init; } = string.Empty;
    public string SensitivityLevel { get; init; } = "Internal";
    public int DefaultQueryLimit { get; init; }
    public int MaxQueryLimit { get; init; }
    public bool IsSelectableInChat { get; init; }
    public bool IsSelectableInAgent { get; init; }
    public bool IsSimulation { get; init; }
    public string SourceLabel { get; init; } = string.Empty;
    public bool IsGovernedQueryEnabled { get; init; }
    public string GovernanceStatus { get; init; } = string.Empty;
}

public record CreatedBusinessDatabaseDto(Guid Id, string Name);

public sealed record DataSourcePermissionGrantDto(
    Guid Id,
    Guid DataSourceId,
    string TargetType,
    string TargetValue,
    bool CanQuery,
    bool CanSchemaView,
    bool IsEnabled,
    DateTime CreatedAt,
    DateTime UpdatedAt);

[AuthorizeRequirement("DataAnalysis.CreateBusinessDatabase")]
public record CreateBusinessDatabaseCommand(
    string Name,
    string Description,
    string ConnectionString,
    DbProviderType Provider,
    bool IsEnabled = true,
    bool IsReadOnly = true,
    DataSourceExternalSystemType ExternalSystemType = DataSourceExternalSystemType.Unknown,
    bool ReadOnlyCredentialVerified = false,
    string? Category = null,
    IReadOnlyCollection<string>? Tags = null,
    string? OwnerDepartment = null,
    string? BusinessDomain = null,
    string? SensitivityLevel = null,
    int DefaultQueryLimit = 200,
    int MaxQueryLimit = 1000,
    bool IsSelectableInChat = true,
    bool IsSelectableInAgent = true) : ICommand<Result<CreatedBusinessDatabaseDto>>;

[AuthorizeRequirement("DataAnalysis.UpdateBusinessDatabase")]
public record UpdateBusinessDatabaseCommand(
    Guid Id,
    string Name,
    string Description,
    string ConnectionString,
    DbProviderType Provider,
    bool IsEnabled,
    bool IsReadOnly,
    DataSourceExternalSystemType ExternalSystemType = DataSourceExternalSystemType.Unknown,
    bool ReadOnlyCredentialVerified = false,
    string? Category = null,
    IReadOnlyCollection<string>? Tags = null,
    string? OwnerDepartment = null,
    string? BusinessDomain = null,
    string? SensitivityLevel = null,
    int DefaultQueryLimit = 200,
    int MaxQueryLimit = 1000,
    bool IsSelectableInChat = true,
    bool IsSelectableInAgent = true) : ICommand<Result>;

[AuthorizeRequirement("DataAnalysis.DeleteBusinessDatabase")]
public record DeleteBusinessDatabaseCommand(Guid Id) : ICommand<Result>;

[AuthorizeRequirement("DataSource.Manage")]
public sealed record GrantDataSourcePermissionCommand(
    Guid DataSourceId,
    string TargetType,
    string TargetValue,
    bool CanQuery = true,
    bool CanSchemaView = false) : ICommand<Result<DataSourcePermissionGrantDto>>;

[AuthorizeRequirement("DataSource.Manage")]
public sealed record RevokeDataSourcePermissionCommand(Guid GrantId)
    : ICommand<Result>;

[AuthorizeRequirement("DataAnalysis.GetBusinessDatabase")]
public record GetBusinessDatabaseQuery(Guid Id) : IQuery<Result<BusinessDatabaseDto>>;

[AuthorizeRequirement("DataAnalysis.GetListBusinessDatabases")]
public record GetListBusinessDatabasesQuery : IQuery<Result<IList<BusinessDatabaseDto>>>;

[AuthorizeRequirement("DataSource.Read")]
public sealed record GetMyAuthorizedDataSourcesQuery(
    DataSourceSelectionMode SelectionMode = DataSourceSelectionMode.Chat)
    : IQuery<Result<IList<BusinessDatabaseDto>>>;
