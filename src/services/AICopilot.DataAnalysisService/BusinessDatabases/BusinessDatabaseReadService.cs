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
        var databases = await repository.ListAsync(new EnabledBusinessDatabasesSpec(), cancellationToken);
        var authorized = await accessService.FilterQueryAuthorizedAsync(databases, cancellationToken);
        return authorized
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
