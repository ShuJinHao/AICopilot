using AICopilot.Core.AiGateway.Aggregates.TrialOperations;
using AICopilot.Core.AiGateway.Ids;
using AICopilot.Core.AiGateway.Specifications.TrialOperations;
using AICopilot.SharedKernel.Messaging;
using AICopilot.SharedKernel.Repository;
using AICopilot.SharedKernel.Result;

namespace AICopilot.AiGatewayService.TrialOperations;

public sealed class GetTrialCampaignsQueryHandler(IReadRepository<TrialCampaign> campaignRepository)
    : IQueryHandler<GetTrialCampaignsQuery, Result<IReadOnlyCollection<TrialCampaignDto>>>
{
    public async Task<Result<IReadOnlyCollection<TrialCampaignDto>>> Handle(
        GetTrialCampaignsQuery request,
        CancellationToken cancellationToken)
    {
        var campaigns = await campaignRepository.ListAsync(
            new TrialCampaignsListSpec(includeDetails: true),
            cancellationToken);
        return Result.Success<IReadOnlyCollection<TrialCampaignDto>>(
            campaigns.Select(TrialOperationsMapper.Map).ToArray());
    }
}

public sealed class GetTrialCampaignDetailQueryHandler(IReadRepository<TrialCampaign> campaignRepository)
    : IQueryHandler<GetTrialCampaignDetailQuery, Result<TrialCampaignDto>>
{
    public async Task<Result<TrialCampaignDto>> Handle(
        GetTrialCampaignDetailQuery request,
        CancellationToken cancellationToken)
    {
        var campaign = await campaignRepository.FirstOrDefaultAsync(
            new TrialCampaignByIdSpec(new TrialCampaignId(request.CampaignId), includeDetails: true),
            cancellationToken);
        return campaign is null
            ? Result.NotFound()
            : Result.Success(TrialOperationsMapper.Map(campaign));
    }
}
