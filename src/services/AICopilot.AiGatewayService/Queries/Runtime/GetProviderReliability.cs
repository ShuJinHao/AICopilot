using AICopilot.Services.Contracts;
using AICopilot.Services.CrossCutting.Attributes;
using AICopilot.SharedKernel.Messaging;
using AICopilot.SharedKernel.Result;

namespace AICopilot.AiGatewayService.Queries.Runtime;

[AuthorizeRequirement("AiGateway.GetProviderReliability")]
public sealed record GetProviderReliabilityQuery : IQuery<Result<ModelProviderReliabilityDto>>;

public sealed class GetProviderReliabilityQueryHandler(
    IModelProviderReliabilitySnapshotReader snapshotReader)
    : IQueryHandler<GetProviderReliabilityQuery, Result<ModelProviderReliabilityDto>>
{
    public Task<Result<ModelProviderReliabilityDto>> Handle(
        GetProviderReliabilityQuery request,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(Result.Success(snapshotReader.GetSnapshot()));
    }
}
