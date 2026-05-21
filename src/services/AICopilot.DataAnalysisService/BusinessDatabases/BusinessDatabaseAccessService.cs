using AICopilot.Core.DataAnalysis.Aggregates.BusinessDatabase;
using AICopilot.Core.DataAnalysis.Ids;
using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Repository;

namespace AICopilot.DataAnalysisService.BusinessDatabases;

public sealed class BusinessDatabaseAccessService(
    IReadRepository<DataSourcePermissionGrant> grantRepository,
    ICurrentUser currentUser)
{
    public async Task<bool> CanQueryAsync(
        BusinessDatabase database,
        CancellationToken cancellationToken = default)
    {
        return await HasGrantAsync(database.Id, grant => grant.CanQuery, cancellationToken);
    }

    public async Task<bool> CanSchemaViewAsync(
        BusinessDatabase database,
        CancellationToken cancellationToken = default)
    {
        return await HasGrantAsync(database.Id, grant => grant.CanSchemaView, cancellationToken);
    }

    public async Task<bool> CanViewMetadataAsync(
        BusinessDatabase database,
        CancellationToken cancellationToken = default)
    {
        return IsAdmin() ||
               await HasGrantAsync(
                   database.Id,
                   grant => grant.CanQuery || grant.CanSchemaView,
                   cancellationToken);
    }

    public async Task<IReadOnlyList<BusinessDatabase>> FilterQueryAuthorizedAsync(
        IEnumerable<BusinessDatabase> databases,
        CancellationToken cancellationToken = default)
    {
        var materialized = databases.ToArray();
        if (IsAdmin())
        {
            return materialized;
        }

        var grants = await LoadMatchingGrantsAsync(cancellationToken);
        return materialized
            .Where(database => grants.Any(grant => grant.DataSourceId == database.Id && grant.CanQuery))
            .ToArray();
    }

    public async Task<IReadOnlyList<BusinessDatabase>> FilterMetadataAuthorizedAsync(
        IEnumerable<BusinessDatabase> databases,
        CancellationToken cancellationToken = default)
    {
        var materialized = databases.ToArray();
        if (IsAdmin())
        {
            return materialized;
        }

        var grants = await LoadMatchingGrantsAsync(cancellationToken);
        return materialized
            .Where(database => grants.Any(grant =>
                grant.DataSourceId == database.Id &&
                (grant.CanQuery || grant.CanSchemaView)))
            .ToArray();
    }

    private async Task<bool> HasGrantAsync(
        BusinessDatabaseId dataSourceId,
        Func<DataSourcePermissionGrant, bool> permissionPredicate,
        CancellationToken cancellationToken)
    {
        if (IsAdmin())
        {
            return true;
        }

        var grants = await LoadMatchingGrantsAsync(cancellationToken);
        return grants.Any(grant => grant.DataSourceId == dataSourceId && permissionPredicate(grant));
    }

    private async Task<IReadOnlyCollection<DataSourcePermissionGrant>> LoadMatchingGrantsAsync(
        CancellationToken cancellationToken)
    {
        if (currentUser.Id is null || !currentUser.IsAuthenticated)
        {
            return [];
        }

        var grants = await grantRepository.ListAsync(cancellationToken: cancellationToken);
        return grants
            .Where(grant => grant.IsEnabled)
            .Where(MatchesCurrentUser)
            .ToArray();
    }

    private bool MatchesCurrentUser(DataSourcePermissionGrant grant)
    {
        return grant.TargetType switch
        {
            DataSourcePermissionGrantTargetType.User => currentUser.Id is { } userId &&
                                                        string.Equals(
                                                            grant.TargetValue,
                                                            userId.ToString("D"),
                                                            StringComparison.OrdinalIgnoreCase),
            DataSourcePermissionGrantTargetType.Role => !string.IsNullOrWhiteSpace(currentUser.Role) &&
                                                        string.Equals(
                                                            grant.TargetValue,
                                                            currentUser.Role,
                                                            StringComparison.OrdinalIgnoreCase),
            DataSourcePermissionGrantTargetType.Department => MatchesDepartment(grant.TargetValue),
            _ => false
        };
    }

    private bool MatchesDepartment(string targetValue)
    {
        return (!string.IsNullOrWhiteSpace(currentUser.CloudDepartmentId) &&
                string.Equals(targetValue, currentUser.CloudDepartmentId, StringComparison.OrdinalIgnoreCase)) ||
               (!string.IsNullOrWhiteSpace(currentUser.CloudDepartmentName) &&
                string.Equals(targetValue, currentUser.CloudDepartmentName, StringComparison.OrdinalIgnoreCase));
    }

    private bool IsAdmin()
    {
        return string.Equals(currentUser.Role, "Admin", StringComparison.OrdinalIgnoreCase);
    }
}
