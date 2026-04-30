using AICopilot.Core.AiGateway.Aggregates.ApprovalPolicy;
using AICopilot.Core.AiGateway.Ids;
using AICopilot.SharedKernel.Specification;

namespace AICopilot.Core.AiGateway.Specifications.ApprovalPolicy;

public sealed class ApprovalPolicyByIdSpec : Specification<Aggregates.ApprovalPolicy.ApprovalPolicy>
{
    public ApprovalPolicyByIdSpec(ApprovalPolicyId id)
    {
        FilterCondition = policy => policy.Id == id;
    }
}

public sealed class ApprovalPoliciesOrderedSpec : Specification<Aggregates.ApprovalPolicy.ApprovalPolicy>
{
    public ApprovalPoliciesOrderedSpec()
    {
        SetOrderBy(policy => policy.Name);
    }
}

public sealed class EnabledApprovalPoliciesSpec : Specification<Aggregates.ApprovalPolicy.ApprovalPolicy>
{
    public EnabledApprovalPoliciesSpec()
    {
        FilterCondition = policy => policy.IsEnabled;
    }
}

public sealed class EnabledApprovalPoliciesByTargetTypeSpec : Specification<Aggregates.ApprovalPolicy.ApprovalPolicy>
{
    public EnabledApprovalPoliciesByTargetTypeSpec(ApprovalTargetType targetType)
    {
        FilterCondition = policy => policy.IsEnabled && policy.TargetType == targetType;
    }
}
