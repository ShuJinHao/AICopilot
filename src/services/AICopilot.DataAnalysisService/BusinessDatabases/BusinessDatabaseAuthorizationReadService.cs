using AICopilot.Core.DataAnalysis.Aggregates.BusinessDatabase;
using AICopilot.Core.DataAnalysis.Specifications.BusinessDatabase;
using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Repository;

namespace AICopilot.DataAnalysisService.BusinessDatabases;

public sealed class BusinessDatabaseAuthorizationReadService(
    IReadRepository<BusinessDatabase> databaseRepository,
    IReadRepository<DataSourcePermissionGrant> grantRepository,
    IIdentityAccessService identityAccessService,
    IExternalIdentityBindingStore externalIdentityBindingStore,
    IBusinessDataSourceProfileRegistry profileRegistry)
    : IBusinessDatabaseAuthorizationReadService
{
    public async Task<IReadOnlyList<BusinessDatabaseDescriptor>> ListSelectableForUserAsync(
        Guid userId,
        DataSourceSelectionMode selectionMode,
        CancellationToken cancellationToken = default)
    {
        if (userId == Guid.Empty)
        {
            return [];
        }

        var access = await identityAccessService.GetCurrentUserAccessAsync(userId, cancellationToken);
        if (access is null)
        {
            return [];
        }

        var databases = await databaseRepository.ListAsync(
            new EnabledBusinessDatabasesSpec(),
            cancellationToken);
        IEnumerable<BusinessDatabase> authorized = databases;
        if (!string.Equals(access.RoleName, "Admin", StringComparison.OrdinalIgnoreCase))
        {
            var externalIdentity = await externalIdentityBindingStore.FindByUserProviderAsync(
                userId,
                ExternalIdentityProviders.Cloud,
                cancellationToken);
            if (externalIdentity is { AccountEnabledSnapshot: false } or { EmployeeActiveSnapshot: false })
            {
                return [];
            }

            var grants = await grantRepository.ListAsync(cancellationToken: cancellationToken);
            var authorizedDataSourceIds = grants
                .Where(grant => grant.IsEnabled && grant.CanQuery)
                .Where(grant => Matches(grant, userId, access.RoleName, externalIdentity))
                .Select(grant => grant.DataSourceId)
                .ToHashSet();
            authorized = authorized.Where(database => authorizedDataSourceIds.Contains(database.Id));
        }

        return authorized
            .Where(database => BusinessDataSourceGovernancePolicy.IsSelectableForMode(
                database,
                selectionMode,
                profileRegistry))
            .Select(BusinessDatabaseContractMapper.ToDescriptor)
            .ToArray();
    }

    private static bool Matches(
        DataSourcePermissionGrant grant,
        Guid userId,
        string? roleName,
        ExternalIdentityBindingSnapshot? externalIdentity)
    {
        return grant.TargetType switch
        {
            DataSourcePermissionGrantTargetType.User => string.Equals(
                grant.TargetValue,
                userId.ToString("D"),
                StringComparison.OrdinalIgnoreCase),
            DataSourcePermissionGrantTargetType.Role => !string.IsNullOrWhiteSpace(roleName) &&
                                                        string.Equals(
                                                            grant.TargetValue,
                                                            roleName,
                                                            StringComparison.OrdinalIgnoreCase),
            DataSourcePermissionGrantTargetType.Department => externalIdentity is not null &&
                                                              (string.Equals(
                                                                   grant.TargetValue,
                                                                   externalIdentity.DepartmentIdSnapshot,
                                                                   StringComparison.OrdinalIgnoreCase) ||
                                                               string.Equals(
                                                                   grant.TargetValue,
                                                                   externalIdentity.DepartmentNameSnapshot,
                                                                   StringComparison.OrdinalIgnoreCase)),
            _ => false
        };
    }
}
