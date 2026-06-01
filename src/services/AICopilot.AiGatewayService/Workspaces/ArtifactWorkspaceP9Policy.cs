using System.Security.Cryptography;
using System.Text;
using AICopilot.Core.AiGateway.Aggregates.AgentTasks;
using AICopilot.Core.AiGateway.Aggregates.Approvals;
using AICopilot.Core.AiGateway.Aggregates.Artifacts;
using AICopilot.SharedKernel.Repository;
using AICopilot.SharedKernel.Result;

namespace AICopilot.AiGatewayService.Workspaces;

internal static class ArtifactWorkspaceP9Policy
{
    public static async Task<Result> ValidateDraftMutationAsync(
        IReadRepository<ApprovalRequest> approvalRepository,
        ArtifactVersioningContext context,
        int expectedVersion,
        bool allowBinaryArtifact,
        CancellationToken cancellationToken)
    {
        if (context.Artifact.Status is ArtifactStatus.Final or ArtifactStatus.Deleted or ArtifactStatus.Rejected)
        {
            return Result.Invalid($"Artifact status {context.Artifact.Status} cannot be revised.");
        }

        if (!allowBinaryArtifact && ArtifactVersioningPolicy.ValidateTextArtifact(context.Artifact) is { } textError)
        {
            return Result.Invalid(textError);
        }

        if (expectedVersion != context.Artifact.Version)
        {
            return Result.Invalid("Artifact version has changed. Refresh and retry with the latest expectedVersion.");
        }

        if (context.Workspace.Status == ArtifactWorkspaceStatus.Finalized)
        {
            return Result.Invalid("Finalized workspaces cannot be revised.");
        }

        if (await WorkspaceAccess.HasFinalOutputApprovalAsync(
                approvalRepository,
                context.Task.Id,
                context.Workspace.WorkspaceCode,
                cancellationToken))
        {
            return Result.Invalid("Artifact drafts are locked after final review submission.");
        }

        return context.Task.Status != AgentTaskStatus.WorkspaceReady
            ? Result.Invalid("Artifact drafts can only be revised while the task is WorkspaceReady and before final review submission.")
            : Result.Success();
    }

    public static string ComputeHash(string value)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    }
}
