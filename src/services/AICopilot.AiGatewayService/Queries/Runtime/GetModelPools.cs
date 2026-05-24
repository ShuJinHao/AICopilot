using AICopilot.Services.Contracts;
using AICopilot.Services.CrossCutting.Attributes;
using AICopilot.SharedKernel.Messaging;
using AICopilot.SharedKernel.Result;

namespace AICopilot.AiGatewayService.Queries.Runtime;

[AuthorizeRequirement("AiGateway.GetModelPools")]
public sealed record GetModelPoolsQuery : IQuery<Result<ModelPoolSnapshotDto>>;

public sealed class GetModelPoolsQueryHandler(
    IModelPoolSnapshotReader snapshotReader)
    : IQueryHandler<GetModelPoolsQuery, Result<ModelPoolSnapshotDto>>
{
    public Task<Result<ModelPoolSnapshotDto>> Handle(
        GetModelPoolsQuery request,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(Result.Success(snapshotReader.GetSnapshot()));
    }
}
