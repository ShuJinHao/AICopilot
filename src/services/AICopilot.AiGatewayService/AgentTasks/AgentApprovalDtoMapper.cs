using AICopilot.Core.AiGateway.Aggregates.AgentTasks;
using AICopilot.Core.AiGateway.Aggregates.Approvals;
using AICopilot.Core.AiGateway.Aggregates.Artifacts;
using AICopilot.Core.AiGateway.Ids;

namespace AICopilot.AiGatewayService.AgentTasks;

internal static class AgentApprovalDtoMapper
{
    public static AgentApprovalRequestDto Map(
        ApprovalRequest approval,
        AgentTask task,
        ArtifactWorkspace? workspace)
    {
        return new AgentApprovalRequestDto(
            approval.Id,
            task.Id,
            workspace?.WorkspaceCode,
            approval.ApprovalType.ToString(),
            approval.TargetId,
            ResolveTargetName(approval, task, workspace),
            ResolveRiskLevel(approval, task),
            approval.Status.ToString(),
            ResolveReason(approval),
            approval.CreatedAt,
            approval.ApprovedAt,
            approval.ApprovedBy);
    }

    public static AgentStep? FindStep(AgentTask task, string targetId)
    {
        return Guid.TryParse(targetId, out var stepId)
            ? task.Steps.FirstOrDefault(step => step.Id == new AgentStepId(stepId))
            : null;
    }

    public static string ResolveToolRisk(string? toolCode)
    {
        return toolCode switch
        {
            "query_cloud_data_readonly" => AgentTaskRiskLevel.Medium.ToString(),
            "generate_pdf" or "generate_pptx" or "generate_xlsx" or "finalize_artifacts" => AgentTaskRiskLevel.High.ToString(),
            _ => AgentTaskRiskLevel.Low.ToString()
        };
    }

    private static string ResolveTargetName(
        ApprovalRequest approval,
        AgentTask task,
        ArtifactWorkspace? workspace)
    {
        if (approval.ApprovalType == AgentApprovalType.Plan)
        {
            return task.Title;
        }

        if (approval.ApprovalType == AgentApprovalType.ToolCall)
        {
            var step = FindStep(task, approval.TargetId);
            return step is null
                ? approval.TargetId
                : string.IsNullOrWhiteSpace(step.ToolCode)
                    ? step.Title
                    : $"{step.StepIndex}. {step.ToolCode}";
        }

        if (approval.ApprovalType == AgentApprovalType.Artifact &&
            workspace is not null &&
            Guid.TryParse(approval.TargetId, out var artifactId))
        {
            return workspace.Artifacts.FirstOrDefault(item => item.Id == new ArtifactId(artifactId))?.Name
                   ?? approval.TargetId;
        }

        return workspace?.WorkspaceCode ?? task.TaskCode;
    }

    private static string ResolveRiskLevel(ApprovalRequest approval, AgentTask task)
    {
        if (approval.ApprovalType != AgentApprovalType.ToolCall)
        {
            return task.RiskLevel.ToString();
        }

        var step = FindStep(task, approval.TargetId);
        return ResolveToolRisk(step?.ToolCode);
    }

    private static string? ResolveReason(ApprovalRequest approval)
    {
        if (!string.IsNullOrWhiteSpace(approval.ApprovalComment))
        {
            return approval.ApprovalComment;
        }

        return approval.Status == AgentApprovalStatus.Pending
            ? $"Waiting for {approval.ApprovalType} approval."
            : null;
    }
}
