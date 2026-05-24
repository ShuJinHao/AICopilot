using AICopilot.Core.AiGateway.Aggregates.TrialOperations;
using AICopilot.Core.AiGateway.Ids;
using AICopilot.SharedKernel.Specification;

namespace AICopilot.Core.AiGateway.Specifications.TrialOperations;

public sealed class TrialCampaignByIdSpec : Specification<TrialCampaign>
{
    public TrialCampaignByIdSpec(TrialCampaignId id, bool includeDetails = false)
    {
        FilterCondition = campaign => campaign.Id == id;
        if (includeDetails)
        {
            AddInclude(campaign => campaign.ScenarioRuns);
            AddInclude(campaign => campaign.RiskIssues);
        }
    }
}

public sealed class TrialCampaignsListSpec : Specification<TrialCampaign>
{
    public TrialCampaignsListSpec(bool includeDetails = false)
    {
        SetOrderByDescending(campaign => campaign.CreatedAt);
        if (includeDetails)
        {
            AddInclude(campaign => campaign.ScenarioRuns);
            AddInclude(campaign => campaign.RiskIssues);
        }
    }
}
