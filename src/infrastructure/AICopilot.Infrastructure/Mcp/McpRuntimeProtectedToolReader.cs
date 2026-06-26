using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Ai;

namespace AICopilot.Infrastructure.Mcp;

internal sealed class McpRuntimeProtectedToolReader(
    IApprovalRequirementReadService approvalRequirementReadService)
{
    public async Task<HashSet<string>> LoadProtectedToolNamesAsync(
        string serverName,
        CancellationToken cancellationToken)
    {
        var approvalPolicies = await LoadApprovalPoliciesAsync([serverName], cancellationToken);
        return approvalPolicies.GetValueOrDefault(serverName)
               ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    }

    private async Task<Dictionary<string, HashSet<string>>> LoadApprovalPoliciesAsync(
        string[] serverNames,
        CancellationToken cancellationToken)
    {
        if (serverNames.Length == 0)
        {
            return [];
        }

        var requirements = await approvalRequirementReadService.GetToolRequirementsAsync(
            AiToolTargetType.McpServer,
            serverNames,
            cancellationToken);

        var result = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var requirement in requirements.Where(requirement => requirement.RequiresApproval))
        {
            if (!result.TryGetValue(requirement.TargetName, out var names))
            {
                names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                result[requirement.TargetName] = names;
            }

            names.Add(requirement.ToolName);
        }

        return result;
    }
}
