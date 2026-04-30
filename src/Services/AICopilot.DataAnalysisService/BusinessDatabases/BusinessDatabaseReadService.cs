using AICopilot.Core.DataAnalysis.Aggregates.BusinessDatabase;
using AICopilot.Core.DataAnalysis.Specifications.BusinessDatabase;
using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Repository;

namespace AICopilot.DataAnalysisService.BusinessDatabases;

public sealed class BusinessDatabaseReadService(IReadRepository<BusinessDatabase> repository)
    : IBusinessDatabaseReadService
{
    public async Task<IReadOnlyList<BusinessDatabaseDescriptor>> ListEnabledAsync(
        CancellationToken cancellationToken = default)
    {
        var databases = await repository.ListAsync(new EnabledBusinessDatabasesSpec(), cancellationToken);
        return databases
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

        return database is null
            ? null
            : BusinessDatabaseContractMapper.ToConnectionInfo(database);
    }
}
