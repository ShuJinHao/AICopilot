using AICopilot.Core.AiGateway.Aggregates.AgentTasks;
using AICopilot.Core.AiGateway.Aggregates.Artifacts;
using AICopilot.Core.AiGateway.Aggregates.Approvals;
using AICopilot.Services.Contracts;

namespace AICopilot.AiGatewayService.AgentTasks;

public sealed class AgentAuditRecorder(IAuditLogWriter auditLogWriter)
{
    public Task RecordPlanAsync(
        AgentTask task,
        string result,
        string summary,
        int pendingApprovalCount,
        CancellationToken cancellationToken)
    {
        return WriteAsync(
            "Agent.Plan",
            "AgentTask",
            task.Id.Value.ToString(),
            task.Title,
            result,
            summary,
            new Dictionary<string, string>
            {
                ["taskId"] = task.Id.Value.ToString(),
                ["taskCode"] = task.TaskCode,
                ["riskLevel"] = task.RiskLevel.ToString(),
                ["pendingApprovalCount"] = pendingApprovalCount.ToString()
            },
            cancellationToken);
    }

    public Task RecordApprovalDecisionAsync(
        ApprovalRequest approval,
        AgentTask task,
        string result,
        string summary,
        CancellationToken cancellationToken)
    {
        return WriteAsync(
            "Agent.ApprovalDecision",
            "ApprovalRequest",
            approval.Id.Value.ToString(),
            approval.ApprovalType.ToString(),
            result,
            summary,
            new Dictionary<string, string>
            {
                ["taskId"] = task.Id.Value.ToString(),
                ["taskCode"] = task.TaskCode,
                ["workspaceCode"] = string.Empty,
                ["approvalType"] = approval.ApprovalType.ToString(),
                ["targetId"] = approval.TargetId,
                ["approvalStatus"] = approval.Status.ToString()
            },
            cancellationToken);
    }

    public Task RecordToolAsync(
        AgentTask task,
        ArtifactWorkspace workspace,
        AgentStep step,
        string result,
        string summary,
        string? artifactId,
        CancellationToken cancellationToken)
    {
        return WriteAsync(
            "Agent.ToolExecution",
            "AgentStep",
            step.Id.Value.ToString(),
            step.ToolCode ?? step.Title,
            result,
            summary,
            new Dictionary<string, string>
            {
                ["taskId"] = task.Id.Value.ToString(),
                ["taskCode"] = task.TaskCode,
                ["workspaceCode"] = workspace.WorkspaceCode,
                ["stepOrder"] = step.StepIndex.ToString(),
                ["toolName"] = step.ToolCode ?? string.Empty,
                ["artifactId"] = artifactId ?? string.Empty,
                ["failureReason"] = result == AuditResults.Succeeded ? string.Empty : summary
            },
            cancellationToken);
    }

    public Task RecordArtifactDownloadAsync(
        AgentTask task,
        ArtifactWorkspace workspace,
        Artifact artifact,
        CancellationToken cancellationToken)
    {
        return WriteAsync(
            "Agent.ArtifactDownload",
            "Artifact",
            artifact.Id.Value.ToString(),
            artifact.Name,
            AuditResults.Succeeded,
            "Agent artifact downloaded.",
            new Dictionary<string, string>
            {
                ["taskId"] = task.Id.Value.ToString(),
                ["taskCode"] = task.TaskCode,
                ["workspaceCode"] = workspace.WorkspaceCode,
                ["artifactId"] = artifact.Id.Value.ToString(),
                ["artifactStatus"] = artifact.Status.ToString()
            },
            cancellationToken);
    }

    public Task RecordWorkspaceFinalizedAsync(
        AgentTask task,
        ArtifactWorkspace workspace,
        string result,
        string summary,
        CancellationToken cancellationToken)
    {
        return WriteAsync(
            "Agent.WorkspaceFinalize",
            "ArtifactWorkspace",
            workspace.Id.Value.ToString(),
            workspace.WorkspaceCode,
            result,
            summary,
            new Dictionary<string, string>
            {
                ["taskId"] = task.Id.Value.ToString(),
                ["taskCode"] = task.TaskCode,
                ["workspaceCode"] = workspace.WorkspaceCode,
                ["artifactCount"] = workspace.Artifacts.Count.ToString()
            },
            cancellationToken);
    }

    public Task RecordFinalReviewSubmittedAsync(
        AgentTask task,
        ArtifactWorkspace workspace,
        ApprovalRequest approval,
        CancellationToken cancellationToken)
    {
        return WriteAsync(
            "Agent.FinalReviewSubmitted",
            "ApprovalRequest",
            approval.Id.Value.ToString(),
            approval.ApprovalType.ToString(),
            AuditResults.Succeeded,
            "Workspace final review submitted and is waiting for approval.",
            new Dictionary<string, string>
            {
                ["taskId"] = task.Id.Value.ToString(),
                ["taskCode"] = task.TaskCode,
                ["workspaceCode"] = workspace.WorkspaceCode,
                ["approvalType"] = approval.ApprovalType.ToString(),
                ["targetId"] = approval.TargetId
            },
            cancellationToken);
    }

    private Task WriteAsync(
        string actionCode,
        string targetType,
        string? targetId,
        string targetName,
        string result,
        string summary,
        IReadOnlyDictionary<string, string> metadata,
        CancellationToken cancellationToken)
    {
        return auditLogWriter.WriteAsync(
            new AuditLogWriteRequest(
                AuditActionGroups.AiGateway,
                actionCode,
                targetType,
                targetId,
                targetName,
                result,
                summary,
                Metadata: metadata),
            cancellationToken);
    }
}
