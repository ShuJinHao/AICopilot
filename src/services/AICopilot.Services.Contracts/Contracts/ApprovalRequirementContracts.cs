using AICopilot.SharedKernel.Ai;

namespace AICopilot.Services.Contracts;

public sealed record ApprovalToolRequirementDto(
    AiToolTargetType TargetType,
    string TargetName,
    string ToolName,
    bool RequiresApproval,
    bool RequiresOnsiteAttestation);

public interface IApprovalRequirementReadService
{
    Task<IReadOnlyList<ApprovalToolRequirementDto>> GetToolRequirementsAsync(
        AiToolTargetType targetType,
        IReadOnlyCollection<string> targetNames,
        CancellationToken cancellationToken = default);
}
