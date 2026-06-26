using AICopilot.AiGatewayService.AgentTasks;
using AICopilot.Core.AiGateway.Aggregates.AgentTasks;
using AICopilot.Core.AiGateway.Aggregates.Approvals;
using AICopilot.Core.AiGateway.Aggregates.Artifacts;
using AICopilot.Core.AiGateway.Ids;
using AICopilot.Core.AiGateway.Specifications.AgentTasks;
using AICopilot.Core.AiGateway.Specifications.Artifacts;
using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Repository;
using AICopilot.SharedKernel.Result;

namespace AICopilot.AiGatewayService.Workspaces;

internal sealed record ArtifactVersioningContext(
    ArtifactWorkspace Workspace,
    AgentTask Task,
    Artifact Artifact,
    CurrentUserAccess CurrentAccess,
    bool IsOwner);

internal static class ArtifactVersioningAccess
{
    public static async Task<Result<ArtifactVersioningContext>> LoadArtifactForReadAsync(
        IReadRepository<ArtifactWorkspace> workspaceRepository,
        IReadRepository<AgentTask> taskRepository,
        IReadRepository<ApprovalRequest> approvalRepository,
        ICurrentUser currentUser,
        IIdentityAccessService identityAccessService,
        Guid artifactId,
        string ownerPermission,
        CancellationToken cancellationToken)
    {
        var context = await LoadArtifactAsync(
            workspaceRepository,
            taskRepository,
            currentUser,
            identityAccessService,
            artifactId,
            cancellationToken);
        if (!context.IsSuccess)
        {
            return Result.From(context);
        }

        var value = context.Value!;
        if (value.IsOwner)
        {
            return AgentApprovalPermissions.HasPermission(value.CurrentAccess, ownerPermission)
                ? Result.Success(value)
                : AgentApprovalPermissions.ForbiddenMissing(ownerPermission);
        }

        if (!AgentApprovalPermissions.CanReadFinalReviewWorkspace(value.CurrentAccess))
        {
            return Result.NotFound();
        }

        var hasFinalOutputApproval = await WorkspaceAccess.HasFinalOutputApprovalAsync(
            approvalRepository,
            value.Task.Id,
            value.Workspace.WorkspaceCode,
            cancellationToken);
        return hasFinalOutputApproval ? Result.Success(value) : Result.NotFound();
    }

    public static async Task<Result<ArtifactVersioningContext>> LoadArtifactForOwnerEditAsync(
        IReadRepository<ArtifactWorkspace> workspaceRepository,
        IReadRepository<AgentTask> taskRepository,
        ICurrentUser currentUser,
        IIdentityAccessService identityAccessService,
        Guid artifactId,
        CancellationToken cancellationToken)
    {
        var context = await LoadArtifactAsync(
            workspaceRepository,
            taskRepository,
            currentUser,
            identityAccessService,
            artifactId,
            cancellationToken);
        if (!context.IsSuccess)
        {
            return Result.From(context);
        }

        var value = context.Value!;
        if (!value.IsOwner)
        {
            return Result.NotFound();
        }

        return AgentApprovalPermissions.HasPermission(value.CurrentAccess, AgentApprovalPermissions.EditArtifact)
            ? Result.Success(value)
            : AgentApprovalPermissions.ForbiddenMissing(AgentApprovalPermissions.EditArtifact);
    }

    private static async Task<Result<ArtifactVersioningContext>> LoadArtifactAsync(
        IReadRepository<ArtifactWorkspace> workspaceRepository,
        IReadRepository<AgentTask> taskRepository,
        ICurrentUser currentUser,
        IIdentityAccessService identityAccessService,
        Guid artifactId,
        CancellationToken cancellationToken)
    {
        var currentAccessResult = await AgentApprovalPermissions.LoadCurrentUserAccessAsync(
            currentUser,
            identityAccessService,
            cancellationToken);
        if (!currentAccessResult.IsSuccess)
        {
            return Result.From(currentAccessResult);
        }

        var workspace = await workspaceRepository.FirstOrDefaultAsync(
            new ArtifactWorkspaceByArtifactIdSpec(new ArtifactId(artifactId)),
            cancellationToken);
        if (workspace is null)
        {
            return Result.NotFound();
        }

        var task = await taskRepository.FirstOrDefaultAsync(
            new AgentTaskByIdSpec(workspace.TaskId, includeSteps: true),
            cancellationToken);
        if (task is null)
        {
            return Result.NotFound();
        }

        var artifact = workspace.Artifacts.FirstOrDefault(item => item.Id == new ArtifactId(artifactId));
        if (artifact is null)
        {
            return Result.NotFound();
        }

        var currentAccess = currentAccessResult.Value!;
        return Result.Success(new ArtifactVersioningContext(
            workspace,
            task,
            artifact,
            currentAccess,
            task.UserId == currentAccess.UserId));
    }
}
