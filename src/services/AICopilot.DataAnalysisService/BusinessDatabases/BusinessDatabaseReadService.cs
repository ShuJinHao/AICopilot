using AICopilot.Core.DataAnalysis.Aggregates.BusinessDatabase;
using AICopilot.Core.DataAnalysis.Specifications.BusinessDatabase;
using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Repository;

namespace AICopilot.DataAnalysisService.BusinessDatabases;

public sealed class BusinessDatabaseReadService(
    IReadRepository<BusinessDatabase> repository,
    BusinessDatabaseAccessService accessService)
    : IBusinessDatabaseReadService
{
    public async Task<IReadOnlyList<BusinessDatabaseDescriptor>> ListEnabledAsync(
        CancellationToken cancellationToken = default)
    {
        return await ListSelectableAsync(DataSourceSelectionMode.Chat, cancellationToken);
    }

    public async Task<IReadOnlyList<BusinessDatabaseDescriptor>> ListSelectableAsync(
        DataSourceSelectionMode selectionMode,
        CancellationToken cancellationToken = default)
    {
        var databases = await repository.ListAsync(new EnabledBusinessDatabasesSpec(), cancellationToken);
        var authorized = await accessService.FilterQueryAuthorizedAsync(databases, cancellationToken);
        return authorized
            .Where(database => BusinessDataSourceGovernancePolicy.IsSelectableForMode(database, selectionMode))
            .Select(BusinessDatabaseContractMapper.ToDescriptor)
            .ToArray();
    }

    public async Task<BusinessDatabaseConnectionInfo?> GetByNameAsync(
        string name,
        CancellationToken cancellationToken = default)
    {
        var database = await repository.FirstOrDefaultAsync(
            new BusinessDatabaseByNameSpec(name),
            cancellationToken);

        if (database is null ||
            !await accessService.CanQueryAsync(database, cancellationToken))
        {
            return null;
        }

        return BusinessDatabaseContractMapper.ToConnectionInfo(database);
    }
}
