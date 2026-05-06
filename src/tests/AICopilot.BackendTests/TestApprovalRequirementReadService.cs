using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Ai;

namespace AICopilot.BackendTests;

internal sealed class TestApprovalRequirementReadService(
    params ApprovalToolRequirementDto[] requirements) : IApprovalRequirementReadService
{
    public Task<IReadOnlyList<ApprovalToolRequirementDto>> GetToolRequirementsAsync(
        AiToolTargetType targetType,
        IReadOnlyCollection<string> targetNames,
        CancellationToken cancellationToken = default)
    {
        var targetNameSet = new HashSet<string>(targetNames, StringComparer.OrdinalIgnoreCase);
        var result = requirements
            .Where(requirement => requirement.TargetType == targetType)
            .Where(requirement => targetNameSet.Contains(requirement.TargetName))
            .ToArray();

        return Task.FromResult<IReadOnlyList<ApprovalToolRequirementDto>>(result);
    }
}
