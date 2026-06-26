using AICopilot.Core.AiGateway.Aggregates.AgentTasks;
using AICopilot.Core.AiGateway.Aggregates.Approvals;
using AICopilot.Core.AiGateway.Aggregates.Artifacts;
using AICopilot.SharedKernel.Repository;
using AICopilot.SharedKernel.Result;

namespace AICopilot.AiGatewayService.Workspaces;

internal static class ArtifactVersioningPolicy
{
    public const int MaxContentBytes = 2 * 1024 * 1024;
    public const int MaxDiffBytes = 1024 * 1024;
    public const int MaxDiffLines = 2000;

    private static readonly ArtifactType[] TextArtifactTypes =
    [
        ArtifactType.Markdown,
        ArtifactType.Html,
        ArtifactType.Json,
        ArtifactType.Chart,
        ArtifactType.Csv
    ];

    public static string? ValidateTextArtifact(Artifact artifact)
    {
        if (!TextArtifactTypes.Contains(artifact.ArtifactType))
        {
            return "Only Markdown, HTML, JSON, Chart, and CSV draft artifacts support text content operations.";
        }

        if (artifact.Status == ArtifactStatus.Final)
        {
            return "Final artifacts are immutable and cannot be edited or restored.";
        }

        return artifact.Status is ArtifactStatus.Deleted or ArtifactStatus.Rejected
            ? $"Artifact status {artifact.Status} does not allow text content operations."
            : null;
    }

    public static async Task<Result> ValidateEditWindowAsync(
        IReadRepository<ApprovalRequest> approvalRepository,
        ArtifactWorkspace workspace,
        AgentTask task,
        Artifact artifact,
        int expectedVersion,
        CancellationToken cancellationToken)
    {
        var textError = ValidateTextArtifact(artifact);
        if (textError is not null)
        {
            return Result.Invalid(textError);
        }

        if (expectedVersion != artifact.Version)
        {
            return Result.Invalid("Artifact version has changed. Refresh and retry with the latest expectedVersion.");
        }

        if (workspace.Status == ArtifactWorkspaceStatus.Finalized)
        {
            return Result.Invalid("Finalized workspaces cannot be edited.");
        }

        var hasFinalOutputApproval = await WorkspaceAccess.HasFinalOutputApprovalAsync(
            approvalRepository,
            task.Id,
            workspace.WorkspaceCode,
            cancellationToken);
        if (hasFinalOutputApproval)
        {
            return Result.Invalid("Artifact drafts are locked after final review submission.");
        }

        return task.Status != AgentTaskStatus.WorkspaceReady
            ? Result.Invalid("Artifact drafts can only be edited while the task is WorkspaceReady and before final review submission.")
            : Result.Success();
    }

    public static bool CanEdit(
        ArtifactWorkspace workspace,
        AgentTask task,
        Artifact artifact,
        bool hasEditPermission,
        bool hasFinalOutputApproval)
    {
        return hasEditPermission &&
               ValidateTextArtifact(artifact) is null &&
               workspace.Status != ArtifactWorkspaceStatus.Finalized &&
               task.Status == AgentTaskStatus.WorkspaceReady &&
               !hasFinalOutputApproval;
    }
}
