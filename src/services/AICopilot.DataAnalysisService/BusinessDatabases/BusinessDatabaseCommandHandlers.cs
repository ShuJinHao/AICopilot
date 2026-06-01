using AICopilot.Core.DataAnalysis.Aggregates.BusinessDatabase;
using AICopilot.Core.DataAnalysis.Ids;
using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Messaging;
using AICopilot.SharedKernel.Repository;
using AICopilot.SharedKernel.Result;

namespace AICopilot.DataAnalysisService.BusinessDatabases;

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
