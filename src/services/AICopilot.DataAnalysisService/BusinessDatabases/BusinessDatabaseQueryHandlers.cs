using AICopilot.Core.DataAnalysis.Aggregates.BusinessDatabase;
using AICopilot.Core.DataAnalysis.Ids;
using AICopilot.Core.DataAnalysis.Specifications.BusinessDatabase;
using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Messaging;
using AICopilot.SharedKernel.Repository;
using AICopilot.SharedKernel.Result;

namespace AICopilot.DataAnalysisService.BusinessDatabases;

public class GetBusinessDatabaseQueryHandler(
    IReadRepository<BusinessDatabase> repository,
    BusinessDatabaseAccessService accessService,
    IBusinessDataSourceProfileRegistry profileRegistry)
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
            ? Result.Success(BusinessDatabaseDtoMapper.Map(entity, profileRegistry))
            : Result.Forbidden(new ApiProblemDescriptor(
                "data_source_forbidden",
                "Current user is not authorized to view this business data source."));
    }
}

public class GetListBusinessDatabasesQueryHandler(
    IReadRepository<BusinessDatabase> repository,
    BusinessDatabaseAccessService accessService,
    IBusinessDataSourceProfileRegistry profileRegistry)
    : IQueryHandler<GetListBusinessDatabasesQuery, Result<IList<BusinessDatabaseDto>>>
{
    public async Task<Result<IList<BusinessDatabaseDto>>> Handle(
        GetListBusinessDatabasesQuery request,
        CancellationToken cancellationToken)
    {
        var databases = await repository.ListAsync(new BusinessDatabasesOrderedSpec(), cancellationToken);
        var authorized = await accessService.FilterMetadataAuthorizedAsync(databases, cancellationToken);
        IList<BusinessDatabaseDto> result = authorized
            .Select(database => BusinessDatabaseDtoMapper.Map(database, profileRegistry))
            .ToList();
        return Result.Success(result);
    }
}

public sealed class GetMyAuthorizedDataSourcesQueryHandler(
    IReadRepository<BusinessDatabase> repository,
    BusinessDatabaseAccessService accessService,
    IBusinessDataSourceProfileRegistry profileRegistry)
    : IQueryHandler<GetMyAuthorizedDataSourcesQuery, Result<IList<BusinessDatabaseDto>>>
{
    public async Task<Result<IList<BusinessDatabaseDto>>> Handle(
        GetMyAuthorizedDataSourcesQuery request,
        CancellationToken cancellationToken)
    {
        var databases = await repository.ListAsync(new EnabledBusinessDatabasesSpec(), cancellationToken);
        var authorized = await accessService.FilterQueryAuthorizedAsync(databases, cancellationToken);
        IList<BusinessDatabaseDto> result = authorized
            .Where(database => BusinessDataSourceGovernancePolicy.IsSelectableForMode(
                database,
                request.SelectionMode,
                profileRegistry))
            .Select(database => BusinessDatabaseDtoMapper.Map(database, profileRegistry))
            .ToList();
        return Result.Success(result);
    }
}
