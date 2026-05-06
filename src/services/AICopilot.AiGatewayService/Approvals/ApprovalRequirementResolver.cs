using AICopilot.Core.AiGateway.Aggregates.ApprovalPolicy;
using AICopilot.Core.AiGateway.Specifications.ApprovalPolicy;
using AICopilot.SharedKernel.Ai;
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

    public async Task<ApprovalRequirement> GetMergedRequirementByIdentityAsync(
        AiToolIdentity? identity,
        CancellationToken cancellationToken = default)
    {
        if (identity is null || string.IsNullOrWhiteSpace(identity.ToolName) || string.IsNullOrWhiteSpace(identity.TargetName))
        {
            return ApprovalRequirement.None;
        }

        var targetType = identity.TargetType switch
        {
            AiToolTargetType.McpServer => ApprovalTargetType.McpServer,
            _ => ApprovalTargetType.Plugin
        };

        var policies = await repository.ListAsync(
            new EnabledApprovalPoliciesByTargetTypeSpec(targetType),
            cancellationToken);

        ApprovalRequirement? requirement = null;
        foreach (var policy in policies.Where(policy => PolicyTargetMatches(policy, identity)))
        {
            if (!policy.ToolNames.Contains(identity.ToolName, StringComparer.OrdinalIgnoreCase)
                && !policy.ToolNames.Contains(identity.RuntimeName, StringComparer.OrdinalIgnoreCase)
                && !policy.ToolNames.Any(toolName =>
                    string.Equals(
                        AiToolIdentity.CreateRuntimeName(identity.TargetType, policy.TargetName, toolName),
                        identity.RuntimeName,
                        StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            requirement = Merge(requirement, policy);
        }

        return requirement ?? ApprovalRequirement.None;
    }

    private static bool PolicyTargetMatches(ApprovalPolicy policy, AiToolIdentity identity)
    {
        return string.Equals(policy.TargetName, identity.TargetName, StringComparison.OrdinalIgnoreCase)
               || policy.ToolNames.Any(toolName =>
                   string.Equals(
                       AiToolIdentity.CreateRuntimeName(identity.TargetType, policy.TargetName, toolName),
                       identity.RuntimeName,
                       StringComparison.OrdinalIgnoreCase));
    }

    private static ApprovalRequirement Merge(ApprovalRequirement? current, ApprovalPolicy policy)
    {
        return new ApprovalRequirement(
            RequiresApproval: true,
            RequiresOnsiteAttestation: (current?.RequiresOnsiteAttestation ?? false) || policy.RequiresOnsiteAttestation);
    }
}
