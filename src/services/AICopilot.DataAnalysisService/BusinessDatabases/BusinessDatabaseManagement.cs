using AICopilot.Core.DataAnalysis.Aggregates.BusinessDatabase;
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
    public DateTime CreatedAt { get; init; }
    public bool HasConnectionString { get; init; }
    public string? ConnectionStringMasked { get; init; }
}

public record CreatedBusinessDatabaseDto(Guid Id, string Name);

[AuthorizeRequirement("DataAnalysis.CreateBusinessDatabase")]
public record CreateBusinessDatabaseCommand(
    string Name,
    string Description,
    string ConnectionString,
    DbProviderType Provider,
    bool IsEnabled = true,
    bool IsReadOnly = true) : ICommand<Result<CreatedBusinessDatabaseDto>>;

public class CreateBusinessDatabaseCommandHandler(
    IRepository<BusinessDatabase> repository,
    IAuditLogWriter auditLogWriter)
    : ICommandHandler<CreateBusinessDatabaseCommand, Result<CreatedBusinessDatabaseDto>>
{
    public async Task<Result<CreatedBusinessDatabaseDto>> Handle(
        CreateBusinessDatabaseCommand request,
        CancellationToken cancellationToken)
    {
        var entity = new BusinessDatabase(
            request.Name,
            request.Description,
            request.ConnectionString,
            request.Provider,
            request.IsReadOnly);

        entity.UpdateSettings(request.IsEnabled, request.IsReadOnly);

        repository.Add(entity);
        await repository.SaveChangesAsync(cancellationToken);

        await auditLogWriter.WriteAsync(
            new AuditLogWriteRequest(
                AuditActionGroups.Config,
                "DataAnalysis.CreateBusinessDatabase",
                "BusinessDatabase",
                entity.Id.ToString(),
                entity.Name,
                AuditResults.Succeeded,
                $"Created business database: {entity.Name}; readOnly={entity.IsReadOnly}.",
                ["name", "description", "connectionString", "provider", "isEnabled", "isReadOnly"]),
            cancellationToken);
        await auditLogWriter.SaveChangesAsync(cancellationToken);

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
    bool IsReadOnly) : ICommand<Result>;

public class UpdateBusinessDatabaseCommandHandler(
    IRepository<BusinessDatabase> repository,
    IAuditLogWriter auditLogWriter)
    : ICommandHandler<UpdateBusinessDatabaseCommand, Result>
{
    public async Task<Result> Handle(UpdateBusinessDatabaseCommand request, CancellationToken cancellationToken)
    {
        var entity = await repository.GetByIdAsync(request.Id, cancellationToken);
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

        entity.UpdateInfo(request.Name, request.Description);
        if (connectionChanged)
        {
            entity.UpdateConnection(request.ConnectionString, request.Provider);
        }

        entity.UpdateSettings(request.IsEnabled, request.IsReadOnly);

        repository.Update(entity);
        await repository.SaveChangesAsync(cancellationToken);

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
        await auditLogWriter.SaveChangesAsync(cancellationToken);

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
        var entity = await repository.GetByIdAsync(request.Id, cancellationToken);
        if (entity == null)
        {
            return Result.Success();
        }

        var targetName = entity.Name;

        repository.Delete(entity);
        await repository.SaveChangesAsync(cancellationToken);

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
        await auditLogWriter.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}

[AuthorizeRequirement("DataAnalysis.GetBusinessDatabase")]
public record GetBusinessDatabaseQuery(Guid Id) : IQuery<Result<BusinessDatabaseDto>>;

public class GetBusinessDatabaseQueryHandler(IReadRepository<BusinessDatabase> repository)
    : IQueryHandler<GetBusinessDatabaseQuery, Result<BusinessDatabaseDto>>
{
    public async Task<Result<BusinessDatabaseDto>> Handle(
        GetBusinessDatabaseQuery request,
        CancellationToken cancellationToken)
    {
        var entity = await repository.FirstOrDefaultAsync(new BusinessDatabaseByIdSpec(request.Id), cancellationToken);
        return entity == null ? Result.NotFound() : Result.Success(BusinessDatabaseDtoMapper.Map(entity));
    }
}

[AuthorizeRequirement("DataAnalysis.GetListBusinessDatabases")]
public record GetListBusinessDatabasesQuery : IQuery<Result<IList<BusinessDatabaseDto>>>;

public class GetListBusinessDatabasesQueryHandler(IReadRepository<BusinessDatabase> repository)
    : IQueryHandler<GetListBusinessDatabasesQuery, Result<IList<BusinessDatabaseDto>>>
{
    public async Task<Result<IList<BusinessDatabaseDto>>> Handle(
        GetListBusinessDatabasesQuery request,
        CancellationToken cancellationToken)
    {
        var databases = await repository.ListAsync(new BusinessDatabasesOrderedSpec(), cancellationToken);
        IList<BusinessDatabaseDto> result = databases.Select(BusinessDatabaseDtoMapper.Map).ToList();
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
            CreatedAt = db.CreatedAt,
            HasConnectionString = !string.IsNullOrEmpty(db.ConnectionString),
            ConnectionStringMasked = string.IsNullOrEmpty(db.ConnectionString) ? null : "******"
        };
    }
}
