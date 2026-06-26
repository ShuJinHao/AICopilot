using AICopilot.Core.DataAnalysis.Aggregates.BusinessDatabase;
using AICopilot.Core.DataAnalysis.Ids;
using AICopilot.Core.DataAnalysis.Specifications.BusinessDatabase;
using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Messaging;
using AICopilot.SharedKernel.Repository;
using AICopilot.SharedKernel.Result;

namespace AICopilot.DataAnalysisService.BusinessDatabases;

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
