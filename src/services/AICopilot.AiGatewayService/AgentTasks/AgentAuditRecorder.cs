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

    public Task RecordArtifactUpdatedAsync(
        AgentTask task,
        ArtifactWorkspace workspace,
        Artifact artifact,
        int oldVersion,
        int newVersion,
        string sha256,
        string? comment,
        CancellationToken cancellationToken)
    {
        return WriteAsync(
            "Agent.ArtifactUpdated",
            "Artifact",
            artifact.Id.Value.ToString(),
            artifact.Name,
            AuditResults.Succeeded,
            "Agent draft artifact content updated.",
            ArtifactVersionMetadata(task, workspace, artifact, oldVersion, newVersion, sourceVersion: null, sha256, comment),
            cancellationToken);
    }

    public Task RecordArtifactVersionRestoredAsync(
        AgentTask task,
        ArtifactWorkspace workspace,
        Artifact artifact,
        int sourceVersion,
        int oldVersion,
        int newVersion,
        string sha256,
        string? comment,
        CancellationToken cancellationToken)
    {
        return WriteAsync(
            "Agent.ArtifactVersionRestored",
            "Artifact",
            artifact.Id.Value.ToString(),
            artifact.Name,
            AuditResults.Succeeded,
            "Agent draft artifact version restored.",
            ArtifactVersionMetadata(task, workspace, artifact, oldVersion, newVersion, sourceVersion, sha256, comment),
            cancellationToken);
    }

    public Task RecordArtifactVersionDownloadAsync(
        AgentTask task,
        ArtifactWorkspace workspace,
        Artifact artifact,
        int version,
        CancellationToken cancellationToken)
    {
        return WriteAsync(
            "Agent.ArtifactVersionDownload",
            "Artifact",
            artifact.Id.Value.ToString(),
            artifact.Name,
            AuditResults.Succeeded,
            "Agent artifact version downloaded.",
            new Dictionary<string, string>
            {
                ["taskId"] = task.Id.Value.ToString(),
                ["taskCode"] = task.TaskCode,
                ["workspaceCode"] = workspace.WorkspaceCode,
                ["artifactId"] = artifact.Id.Value.ToString(),
                ["artifactStatus"] = artifact.Status.ToString(),
                ["version"] = version.ToString(),
                ["mimeType"] = artifact.MimeType
            },
            cancellationToken);
    }

    public Task RecordArtifactPreviewedAsync(
        AgentTask task,
        ArtifactWorkspace workspace,
        Artifact artifact,
        string previewKind,
        CancellationToken cancellationToken)
    {
        return WriteAsync(
            "Agent.ArtifactPreviewed",
            "Artifact",
            artifact.Id.Value.ToString(),
            artifact.Name,
            AuditResults.Succeeded,
            "Agent artifact preview generated.",
            new Dictionary<string, string>
            {
                ["taskId"] = task.Id.Value.ToString(),
                ["taskCode"] = task.TaskCode,
                ["workspaceCode"] = workspace.WorkspaceCode,
                ["artifactId"] = artifact.Id.Value.ToString(),
                ["artifactVersion"] = artifact.Version.ToString(),
                ["artifactStatus"] = artifact.Status.ToString(),
                ["previewKind"] = previewKind,
                ["sourceMode"] = artifact.SourceMode ?? string.Empty,
                ["boundary"] = artifact.Boundary ?? string.Empty,
                ["isSimulation"] = artifact.IsSimulation.ToString(),
                ["isSandbox"] = artifact.IsSandbox.ToString(),
                ["queryHash"] = artifact.QueryHash ?? string.Empty,
                ["resultHash"] = artifact.ResultHash ?? string.Empty,
                ["rowCount"] = artifact.RowCount.ToString(),
                ["isTruncated"] = artifact.IsTruncated.ToString()
            },
            cancellationToken);
    }

    public Task RecordArtifactRevisionCommentAsync(
        AgentTask task,
        ArtifactWorkspace workspace,
        Artifact artifact,
        string commentHash,
        CancellationToken cancellationToken)
    {
        return WriteAsync(
            "Agent.ArtifactRevisionCommented",
            "Artifact",
            artifact.Id.Value.ToString(),
            artifact.Name,
            AuditResults.Succeeded,
            "Agent draft artifact revision comment recorded.",
            new Dictionary<string, string>
            {
                ["taskId"] = task.Id.Value.ToString(),
                ["taskCode"] = task.TaskCode,
                ["workspaceCode"] = workspace.WorkspaceCode,
                ["artifactId"] = artifact.Id.Value.ToString(),
                ["artifactVersion"] = artifact.Version.ToString(),
                ["commentHash"] = commentHash,
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

    public Task RecordRunQueueOperationAsync(
        string actionCode,
        AgentTaskRunQueueItem item,
        string result,
        string summary,
        string oldStatus,
        AgentTaskRunAttempt? attempt,
        int? retryAttemptNo,
        CancellationToken cancellationToken)
    {
        return WriteAsync(
            actionCode,
            "AgentTaskRunQueueItem",
            item.Id.Value.ToString(),
            item.TaskId.Value.ToString(),
            result,
            summary,
            new Dictionary<string, string>
            {
                ["queueItemId"] = item.Id.Value.ToString(),
                ["taskId"] = item.TaskId.Value.ToString(),
                ["attemptId"] = (attempt?.Id.Value ?? item.RunAttemptId?.Value)?.ToString() ?? string.Empty,
                ["triggerType"] = item.TriggerType.ToString(),
                ["oldStatus"] = oldStatus,
                ["newStatus"] = item.Status.ToString(),
                ["failureCode"] = item.FailureCode ?? string.Empty,
                ["retryAttemptNo"] = retryAttemptNo?.ToString() ?? string.Empty,
                ["availableAt"] = item.AvailableAt.ToString("O")
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

    private static IReadOnlyDictionary<string, string> ArtifactVersionMetadata(
        AgentTask task,
        ArtifactWorkspace workspace,
        Artifact artifact,
        int oldVersion,
        int newVersion,
        int? sourceVersion,
        string sha256,
        string? comment)
    {
        return new Dictionary<string, string>
        {
            ["taskId"] = task.Id.Value.ToString(),
            ["taskCode"] = task.TaskCode,
            ["artifactId"] = artifact.Id.Value.ToString(),
            ["workspaceCode"] = workspace.WorkspaceCode,
            ["oldVersion"] = oldVersion.ToString(),
            ["newVersion"] = newVersion.ToString(),
            ["sourceVersion"] = sourceVersion?.ToString() ?? string.Empty,
            ["mimeType"] = artifact.MimeType,
            ["sha256"] = sha256,
            ["comment"] = NormalizeComment(comment)
        };
    }

    private static string NormalizeComment(string? comment)
    {
        if (string.IsNullOrWhiteSpace(comment))
        {
            return string.Empty;
        }

        var normalized = comment.Trim();
        return normalized.Length <= 200 ? normalized : normalized[..200];
    }
}
