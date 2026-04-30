using AICopilot.Core.AiGateway.Aggregates.ApprovalPolicy;
using AICopilot.Core.AiGateway.Specifications.ApprovalPolicy;
using AICopilot.SharedKernel.Repository;

namespace AICopilot.AiGatewayService.Approvals;

public sealed record ApprovalRequirement(
    bool RequiresApproval,
    bool RequiresOnsiteAttestation)
{
    public static ApprovalRequirement None { get; } = new(false, false);
}

public class ApprovalRequirementResolver(IReadRepository<ApprovalPolicy> repository)
{
    public async Task<Dictionary<string, Dictionary<string, ApprovalRequirement>>> GetRequirementsForTargetsAsync(
        ApprovalTargetType targetType,
        string[] targetNames,
        CancellationToken cancellationToken = default)
    {
        if (targetNames.Length == 0)
        {
            return [];
        }

        var targetNameSet = new HashSet<string>(targetNames, StringComparer.OrdinalIgnoreCase);
        var policies = await repository.ListAsync(
            new EnabledApprovalPoliciesByTargetTypeSpec(targetType),
            cancellationToken);

        var result = new Dictionary<string, Dictionary<string, ApprovalRequirement>>(StringComparer.OrdinalIgnoreCase);

        foreach (var policy in policies.Where(policy => targetNameSet.Contains(policy.TargetName)))
        {
            if (!result.TryGetValue(policy.TargetName, out var toolMap))
            {
                toolMap = new Dictionary<string, ApprovalRequirement>(StringComparer.OrdinalIgnoreCase);
                result[policy.TargetName] = toolMap;
            }

            foreach (var toolName in policy.ToolNames.Where(toolName => !string.IsNullOrWhiteSpace(toolName)))
            {
                toolMap[toolName] = Merge(toolMap.GetValueOrDefault(toolName), policy);
            }
        }

        return result;
    }

    public async Task<ApprovalRequirement> GetMergedRequirementByToolNameAsync(
        string toolName,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(toolName))
        {
            return ApprovalRequirement.None;
        }

        var policies = await repository.ListAsync(new EnabledApprovalPoliciesSpec(), cancellationToken);

        ApprovalRequirement? requirement = null;
        foreach (var policy in policies)
        {
            if (!policy.ToolNames.Contains(toolName, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            requirement = Merge(requirement, policy);
        }

        return requirement ?? ApprovalRequirement.None;
    }

    private static ApprovalRequirement Merge(ApprovalRequirement? current, ApprovalPolicy policy)
    {
        return new ApprovalRequirement(
            RequiresApproval: true,
            RequiresOnsiteAttestation: (current?.RequiresOnsiteAttestation ?? false) || policy.RequiresOnsiteAttestation);
    }
}
