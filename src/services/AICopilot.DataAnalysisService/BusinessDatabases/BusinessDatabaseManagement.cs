using AICopilot.Core.DataAnalysis.Aggregates.BusinessDatabase;
using AICopilot.Core.DataAnalysis.Ids;
using AICopilot.Core.DataAnalysis.Specifications.BusinessDatabase;
using AICopilot.Services.CrossCutting.Attributes;
using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Messaging;
using AICopilot.SharedKernel.Repository;
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

public class CreateBusinessDatabaseCommandHandler(
    IRepository<BusinessDatabase> repository,
    IAuditLogWriter auditLogWriter)
    : ICommandHandler<CreateBusinessDatabaseCommand, Result<CreatedBusinessDatabaseDto>>
{
    public async Task<Result<CreatedBusinessDatabaseDto>> Handle(
        CreateBusinessDatabaseCommand request,
        CancellationToken cancellationToken)
    {
        var validationError = BusinessDatabaseSafetyValidator.Validate(
            request.Provider,
            request.IsEnabled,
            request.IsReadOnly,
            request.ExternalSystemType,
            request.ReadOnlyCredentialVerified,
            request.DefaultQueryLimit,
            request.MaxQueryLimit);
        if (validationError is not null)
        {
            return Result.Invalid(validationError);
        }

        var entity = new BusinessDatabase(
            request.Name,
            request.Description,
            request.ConnectionString,
            request.Provider,
            request.IsReadOnly,
            BusinessDatabaseContractMapper.ToDomainExternalSystemType(request.ExternalSystemType),
            request.ReadOnlyCredentialVerified,
            request.IsEnabled,
            request.Category,
            request.Tags,
            request.OwnerDepartment,
            request.BusinessDomain,
            request.SensitivityLevel,
            request.DefaultQueryLimit,
            request.MaxQueryLimit,
            request.IsSelectableInChat,
            request.IsSelectableInAgent);

        entity.UpdateSettings(
            request.IsEnabled,
            request.IsReadOnly,
            BusinessDatabaseContractMapper.ToDomainExternalSystemType(request.ExternalSystemType),
            request.ReadOnlyCredentialVerified);
        entity.UpdateGovernance(
            request.Category,
            request.Tags,
            request.OwnerDepartment,
            request.BusinessDomain,
            request.SensitivityLevel,
            request.DefaultQueryLimit,
            request.MaxQueryLimit,
            request.IsSelectableInChat,
            request.IsSelectableInAgent);

        repository.Add(entity);

        await auditLogWriter.WriteAsync(
            new AuditLogWriteRequest(
                AuditActionGroups.Config,
                "DataAnalysis.CreateBusinessDatabase",
                "BusinessDatabase",
                entity.Id.ToString(),
                entity.Name,
                AuditResults.Succeeded,
                $"Created business database: {entity.Name}; readOnly={entity.IsReadOnly}; externalSystem={entity.ExternalSystemType}; readOnlyCredentialVerified={entity.ReadOnlyCredentialVerified}.",
                [
                    "name",
                    "description",
                    "connectionString",
                    "provider",
                    "isEnabled",
                    "isReadOnly",
                    "externalSystemType",
                    "readOnlyCredentialVerified",
                    "category",
                    "tags",
                    "ownerDepartment",
                    "businessDomain",
                    "sensitivityLevel",
                    "queryLimits",
                    "selectability"
                ]),
            cancellationToken);
        await repository.SaveChangesAsync(cancellationToken);

        return Result.Success(new CreatedBusinessDatabaseDto(entity.Id, entity.Name));
    }
}

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

public class UpdateBusinessDatabaseCommandHandler(
    IRepository<BusinessDatabase> repository,
    IAuditLogWriter auditLogWriter)
    : ICommandHandler<UpdateBusinessDatabaseCommand, Result>
{
    public async Task<Result> Handle(UpdateBusinessDatabaseCommand request, CancellationToken cancellationToken)
    {
        var validationError = BusinessDatabaseSafetyValidator.Validate(
            request.Provider,
            request.IsEnabled,
            request.IsReadOnly,
            request.ExternalSystemType,
            request.ReadOnlyCredentialVerified,
            request.DefaultQueryLimit,
            request.MaxQueryLimit);
        if (validationError is not null)
        {
            return Result.Invalid(validationError);
        }

        var entity = await repository.GetByIdAsync(new BusinessDatabaseId(request.Id), cancellationToken);
        if (entity == null)
        {
            return Result.NotFound();
        }

        var changedFields = new List<string>();

        if (!string.Equals(entity.Name, request.Name, StringComparison.Ordinal))
        {
            changedFields.Add("name");
        }

        if (!string.Equals(entity.Description, request.Description, StringComparison.Ordinal))
        {
            changedFields.Add("description");
        }

        var connectionChanged = !string.IsNullOrWhiteSpace(request.ConnectionString);
        if (connectionChanged)
        {
            changedFields.Add("connectionString");
        }

        if (connectionChanged && entity.Provider != request.Provider)
        {
            changedFields.Add("provider");
        }

        if (entity.IsEnabled != request.IsEnabled)
        {
            changedFields.Add("isEnabled");
        }

        if (entity.IsReadOnly != request.IsReadOnly)
        {
            changedFields.Add("isReadOnly");
        }

        var externalSystemType = BusinessDatabaseContractMapper.ToDomainExternalSystemType(request.ExternalSystemType);
        if (entity.ExternalSystemType != externalSystemType)
        {
            changedFields.Add("externalSystemType");
        }

        if (entity.ReadOnlyCredentialVerified != request.ReadOnlyCredentialVerified)
        {
            changedFields.Add("readOnlyCredentialVerified");
        }

        if (!string.Equals(entity.Category, request.Category ?? "General", StringComparison.Ordinal))
        {
            changedFields.Add("category");
        }

        var normalizedTags = string.Join(
            ",",
            (request.Tags ?? [])
                .Where(tag => !string.IsNullOrWhiteSpace(tag))
                .Select(tag => tag.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase));
        if (!string.Equals(entity.Tags, normalizedTags, StringComparison.Ordinal))
        {
            changedFields.Add("tags");
        }

        if (!string.Equals(entity.OwnerDepartment, request.OwnerDepartment ?? string.Empty, StringComparison.Ordinal))
        {
            changedFields.Add("ownerDepartment");
        }

        if (!string.Equals(entity.BusinessDomain, request.BusinessDomain ?? string.Empty, StringComparison.Ordinal))
        {
            changedFields.Add("businessDomain");
        }

        if (!string.Equals(entity.SensitivityLevel, request.SensitivityLevel ?? "Internal", StringComparison.Ordinal))
        {
            changedFields.Add("sensitivityLevel");
        }

        if (entity.DefaultQueryLimit != request.DefaultQueryLimit || entity.MaxQueryLimit != request.MaxQueryLimit)
        {
            changedFields.Add("queryLimits");
        }

        if (entity.IsSelectableInChat != request.IsSelectableInChat ||
            entity.IsSelectableInAgent != request.IsSelectableInAgent)
        {
            changedFields.Add("selectability");
        }

        entity.UpdateInfo(request.Name, request.Description);
        if (connectionChanged)
        {
            entity.UpdateConnection(request.ConnectionString, request.Provider);
        }

        entity.UpdateSettings(
            request.IsEnabled,
            request.IsReadOnly,
            externalSystemType,
            request.ReadOnlyCredentialVerified);
        entity.UpdateGovernance(
            request.Category,
            request.Tags,
            request.OwnerDepartment,
            request.BusinessDomain,
            request.SensitivityLevel,
            request.DefaultQueryLimit,
            request.MaxQueryLimit,
            request.IsSelectableInChat,
            request.IsSelectableInAgent);

        repository.Update(entity);

        var summary = connectionChanged
            ? $"Updated business database: {entity.Name}; connection string replaced; readOnly={entity.IsReadOnly}."
            : $"Updated business database: {entity.Name}; changed={(changedFields.Count == 0 ? "none" : string.Join(", ", changedFields))}.";

        await auditLogWriter.WriteAsync(
            new AuditLogWriteRequest(
                AuditActionGroups.Config,
                "DataAnalysis.UpdateBusinessDatabase",
                "BusinessDatabase",
                entity.Id.ToString(),
                entity.Name,
                AuditResults.Succeeded,
                summary,
                changedFields),
            cancellationToken);
        await repository.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}

[AuthorizeRequirement("DataAnalysis.DeleteBusinessDatabase")]
public record DeleteBusinessDatabaseCommand(Guid Id) : ICommand<Result>;

public class DeleteBusinessDatabaseCommandHandler(
    IRepository<BusinessDatabase> repository,
    IAuditLogWriter auditLogWriter)
    : ICommandHandler<DeleteBusinessDatabaseCommand, Result>
{
    public async Task<Result> Handle(DeleteBusinessDatabaseCommand request, CancellationToken cancellationToken)
    {
        var entity = await repository.GetByIdAsync(new BusinessDatabaseId(request.Id), cancellationToken);
        if (entity == null)
        {
            return Result.Success();
        }

        var targetName = entity.Name;

        repository.Delete(entity);

        await auditLogWriter.WriteAsync(
            new AuditLogWriteRequest(
                AuditActionGroups.Config,
                "DataAnalysis.DeleteBusinessDatabase",
                "BusinessDatabase",
                request.Id.ToString(),
                targetName,
                AuditResults.Succeeded,
                $"Deleted business database: {targetName}."),
            cancellationToken);
        await repository.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}

[AuthorizeRequirement("DataSource.Manage")]
public sealed record GrantDataSourcePermissionCommand(
    Guid DataSourceId,
    string TargetType,
    string TargetValue,
    bool CanQuery = true,
    bool CanSchemaView = false) : ICommand<Result<DataSourcePermissionGrantDto>>;

public sealed class GrantDataSourcePermissionCommandHandler(
    IReadRepository<BusinessDatabase> databaseRepository,
    IRepository<DataSourcePermissionGrant> grantRepository,
    IAuditLogWriter auditLogWriter)
    : ICommandHandler<GrantDataSourcePermissionCommand, Result<DataSourcePermissionGrantDto>>
{
    public async Task<Result<DataSourcePermissionGrantDto>> Handle(
        GrantDataSourcePermissionCommand request,
        CancellationToken cancellationToken)
    {
        var database = await databaseRepository.FirstOrDefaultAsync(
            new BusinessDatabaseByIdSpec(new BusinessDatabaseId(request.DataSourceId)),
            cancellationToken);
        if (database is null)
        {
            return Result.NotFound();
        }

        if (!Enum.TryParse<DataSourcePermissionGrantTargetType>(
                request.TargetType,
                ignoreCase: true,
                out var targetType))
        {
            return Result.Invalid("Data source permission target type is invalid.");
        }

        if (!request.CanQuery && !request.CanSchemaView)
        {
            return Result.Invalid("Data source permission grant must allow query or schema view.");
        }

        var normalizedTarget = NormalizeGrantTargetValue(request.TargetValue);
        if (string.IsNullOrWhiteSpace(normalizedTarget))
        {
            return Result.Invalid("Data source permission target value is required.");
        }

        var existing = (await grantRepository.ListAsync(cancellationToken: cancellationToken))
            .FirstOrDefault(grant =>
                grant.DataSourceId == database.Id &&
                grant.TargetType == targetType &&
                string.Equals(grant.TargetValue, normalizedTarget, StringComparison.OrdinalIgnoreCase));

        var grant = existing ?? new DataSourcePermissionGrant(
            database.Id,
            targetType,
            normalizedTarget,
            request.CanQuery,
            request.CanSchemaView);
        grant.Update(
            database.Id,
            targetType,
            normalizedTarget,
            request.CanQuery,
            request.CanSchemaView,
            isEnabled: true);

        if (existing is null)
        {
            grantRepository.Add(grant);
        }
        else
        {
            grantRepository.Update(grant);
        }

        await auditLogWriter.WriteAsync(
            new AuditLogWriteRequest(
                AuditActionGroups.Config,
                "DataSource.GrantPermission",
                "BusinessDatabase",
                database.Id.ToString(),
                database.Name,
                AuditResults.Succeeded,
                $"Granted data source permission; targetType={targetType}; canQuery={grant.CanQuery}; canSchemaView={grant.CanSchemaView}.",
                ["dataSourceId", "targetType", "targetValue", "canQuery", "canSchemaView"]),
            cancellationToken);
        await grantRepository.SaveChangesAsync(cancellationToken);

        return Result.Success(DataSourcePermissionGrantDtoMapper.Map(grant));
    }

    private static string NormalizeGrantTargetValue(string targetValue)
    {
        if (string.IsNullOrWhiteSpace(targetValue))
        {
            return string.Empty;
        }

        var normalized = targetValue.Trim();
        return normalized.Length > 160 ? normalized[..160] : normalized;
    }
}

[AuthorizeRequirement("DataSource.Manage")]
public sealed record RevokeDataSourcePermissionCommand(Guid GrantId)
    : ICommand<Result>;

public sealed class RevokeDataSourcePermissionCommandHandler(
    IRepository<DataSourcePermissionGrant> grantRepository,
    IAuditLogWriter auditLogWriter)
    : ICommandHandler<RevokeDataSourcePermissionCommand, Result>
{
    public async Task<Result> Handle(
        RevokeDataSourcePermissionCommand request,
        CancellationToken cancellationToken)
    {
        var grant = await grantRepository.GetByIdAsync(new DataSourcePermissionGrantId(request.GrantId), cancellationToken);
        if (grant is null)
        {
            return Result.Success();
        }

        grant.Disable();
        grantRepository.Update(grant);

        await auditLogWriter.WriteAsync(
            new AuditLogWriteRequest(
                AuditActionGroups.Config,
                "DataSource.RevokePermission",
                "BusinessDatabase",
                grant.DataSourceId.ToString(),
                grant.TargetValue,
                AuditResults.Succeeded,
                $"Revoked data source permission; grantId={grant.Id.Value}; targetType={grant.TargetType}.",
                ["grantId", "dataSourceId", "targetType", "targetValue"]),
            cancellationToken);
        await grantRepository.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}

[AuthorizeRequirement("DataAnalysis.GetBusinessDatabase")]
public record GetBusinessDatabaseQuery(Guid Id) : IQuery<Result<BusinessDatabaseDto>>;

public class GetBusinessDatabaseQueryHandler(
    IReadRepository<BusinessDatabase> repository,
    BusinessDatabaseAccessService accessService)
    : IQueryHandler<GetBusinessDatabaseQuery, Result<BusinessDatabaseDto>>
{
    public async Task<Result<BusinessDatabaseDto>> Handle(
        GetBusinessDatabaseQuery request,
        CancellationToken cancellationToken)
    {
        var entity = await repository.FirstOrDefaultAsync(
            new BusinessDatabaseByIdSpec(new BusinessDatabaseId(request.Id)),
            cancellationToken);
        if (entity is null)
        {
            return Result.NotFound();
        }

        return await accessService.CanViewMetadataAsync(entity, cancellationToken)
            ? Result.Success(BusinessDatabaseDtoMapper.Map(entity))
            : Result.Forbidden(new ApiProblemDescriptor(
                "data_source_forbidden",
                "Current user is not authorized to view this business data source."));
    }
}

[AuthorizeRequirement("DataAnalysis.GetListBusinessDatabases")]
public record GetListBusinessDatabasesQuery : IQuery<Result<IList<BusinessDatabaseDto>>>;

public class GetListBusinessDatabasesQueryHandler(
    IReadRepository<BusinessDatabase> repository,
    BusinessDatabaseAccessService accessService)
    : IQueryHandler<GetListBusinessDatabasesQuery, Result<IList<BusinessDatabaseDto>>>
{
    public async Task<Result<IList<BusinessDatabaseDto>>> Handle(
        GetListBusinessDatabasesQuery request,
        CancellationToken cancellationToken)
    {
        var databases = await repository.ListAsync(new BusinessDatabasesOrderedSpec(), cancellationToken);
        var authorized = await accessService.FilterMetadataAuthorizedAsync(databases, cancellationToken);
        IList<BusinessDatabaseDto> result = authorized.Select(BusinessDatabaseDtoMapper.Map).ToList();
        return Result.Success(result);
    }
}

[AuthorizeRequirement("DataSource.Read")]
public sealed record GetMyAuthorizedDataSourcesQuery
    : IQuery<Result<IList<BusinessDatabaseDto>>>;

public sealed class GetMyAuthorizedDataSourcesQueryHandler(
    IReadRepository<BusinessDatabase> repository,
    BusinessDatabaseAccessService accessService)
    : IQueryHandler<GetMyAuthorizedDataSourcesQuery, Result<IList<BusinessDatabaseDto>>>
{
    public async Task<Result<IList<BusinessDatabaseDto>>> Handle(
        GetMyAuthorizedDataSourcesQuery request,
        CancellationToken cancellationToken)
    {
        var databases = await repository.ListAsync(new EnabledBusinessDatabasesSpec(), cancellationToken);
        var authorized = await accessService.FilterQueryAuthorizedAsync(databases, cancellationToken);
        IList<BusinessDatabaseDto> result = authorized.Select(BusinessDatabaseDtoMapper.Map).ToList();
        return Result.Success(result);
    }
}

internal static class BusinessDatabaseDtoMapper
{
    public static BusinessDatabaseDto Map(BusinessDatabase db)
    {
        return new BusinessDatabaseDto
        {
            Id = db.Id,
            Name = db.Name,
            Description = db.Description,
            Provider = db.Provider,
            IsEnabled = db.IsEnabled,
            IsReadOnly = db.IsReadOnly,
            ExternalSystemType = BusinessDatabaseContractMapper.ToContractExternalSystemType(db.ExternalSystemType),
            ReadOnlyCredentialVerified = db.ReadOnlyCredentialVerified,
            CreatedAt = db.CreatedAt,
            HasConnectionString = !string.IsNullOrEmpty(db.ConnectionString),
            ConnectionStringMasked = string.IsNullOrEmpty(db.ConnectionString) ? null : "******",
            Category = db.Category,
            Tags = SplitTags(db.Tags),
            OwnerDepartment = db.OwnerDepartment,
            BusinessDomain = db.BusinessDomain,
            SensitivityLevel = db.SensitivityLevel,
            DefaultQueryLimit = db.DefaultQueryLimit,
            MaxQueryLimit = db.MaxQueryLimit,
            IsSelectableInChat = db.IsSelectableInChat,
            IsSelectableInAgent = db.IsSelectableInAgent,
            IsSimulation = db.ExternalSystemType == BusinessDataExternalSystemType.SimulationBusiness,
            SourceLabel = db.ExternalSystemType == BusinessDataExternalSystemType.SimulationBusiness
                ? BusinessQueryResultMapper.SimulationSourceLabel
                : db.Name
        };
    }

    private static IReadOnlyCollection<string> SplitTags(string tags)
    {
        return string.IsNullOrWhiteSpace(tags)
            ? []
            : tags.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }
}

internal static class DataSourcePermissionGrantDtoMapper
{
    public static DataSourcePermissionGrantDto Map(DataSourcePermissionGrant grant)
    {
        return new DataSourcePermissionGrantDto(
            grant.Id,
            grant.DataSourceId,
            grant.TargetType.ToString(),
            grant.TargetValue,
            grant.CanQuery,
            grant.CanSchemaView,
            grant.IsEnabled,
            grant.CreatedAt,
            grant.UpdatedAt);
    }
}

internal static class BusinessDatabaseSafetyValidator
{
    public static string? Validate(
        DbProviderType provider,
        bool isEnabled,
        bool isReadOnly,
        DataSourceExternalSystemType externalSystemType,
        bool readOnlyCredentialVerified,
        int defaultQueryLimit = 200,
        int maxQueryLimit = 1000)
    {
        if (!Enum.IsDefined(typeof(DataSourceExternalSystemType), externalSystemType))
        {
            return "业务库外部系统类型无效。";
        }

        if (!isReadOnly)
        {
            return "业务库必须配置为只读，AICopilot 不允许保存可写业务库。";
        }

        if (defaultQueryLimit <= 0 || maxQueryLimit <= 0)
        {
            return "业务库查询行数限制必须大于 0。";
        }

        if (defaultQueryLimit > maxQueryLimit)
        {
            return "业务库默认查询行数不能大于最大查询行数。";
        }

        if (maxQueryLimit > 10000)
        {
            return "业务库最大查询行数不能超过 10000。";
        }

        if (!isEnabled)
        {
            return null;
        }

        if (externalSystemType == DataSourceExternalSystemType.CloudReadOnly && !readOnlyCredentialVerified)
        {
            return "Cloud 只读数据源启用前必须确认数据库账号已按只读权限验证。";
        }

        if (provider != DbProviderType.PostgreSql && !readOnlyCredentialVerified)
        {
            return "SQL Server/MySQL 数据源启用前必须确认数据库账号已按只读权限验证。";
        }

        return null;
    }
}
