using AICopilot.Core.AiGateway.Aggregates.PromptPolicy;
using AICopilot.Core.AiGateway.Ids;
using AICopilot.SharedKernel.Specification;

namespace AICopilot.Core.AiGateway.Specifications.PromptPolicy;

public sealed class PromptPolicyByIdSpec : Specification<Aggregates.PromptPolicy.PromptPolicy>
{
    public PromptPolicyByIdSpec(PromptPolicyId id)
    {
        FilterCondition = policy => policy.Id == id;
    }
}

public sealed class PromptPolicyByCodeSpec : Specification<Aggregates.PromptPolicy.PromptPolicy>
{
    public PromptPolicyByCodeSpec(string code)
    {
        FilterCondition = policy => policy.Code == code;
    }
}

public sealed class PromptPoliciesOrderedSpec : Specification<Aggregates.PromptPolicy.PromptPolicy>
{
    public PromptPoliciesOrderedSpec()
    {
        SetOrderBy(policy => policy.Usage);
    }
}

public sealed class ActivePromptPolicyByUsageSpec : Specification<Aggregates.PromptPolicy.PromptPolicy>
{
    public ActivePromptPolicyByUsageSpec(PromptPolicyUsage usage)
    {
        FilterCondition = policy => policy.Usage == usage && policy.IsEnabled && policy.ActiveVersionNo != null;
    }
}
