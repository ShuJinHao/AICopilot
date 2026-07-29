using AICopilot.AiGatewayService.AgentTasks;
using AICopilot.Core.AiGateway.Aggregates.AgentTasks;
using AICopilot.Core.AiGateway.Aggregates.Artifacts;
using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Repository;
using AICopilot.SharedKernel.Result;

namespace AICopilot.AiGatewayService.Workspaces;

public sealed class ArtifactWorkspaceLifecycleCoordinator(
    IRepository<ArtifactWorkspace> workspaceRepository,
    IRepository<AgentTask> taskRepository,
    IArtifactWorkspaceFileStore fileStore,
    FinalOutputApprovalCoordinator finalOutputApprovalCoordinator,
    ICurrentUser currentUser,
    IIdentityAccessService identityAccessService)
{
    public async Task<Result<ArtifactWorkspaceDto>> SubmitFinalReviewAsync(
        string code,
        CancellationToken cancellationToken)
    {
        var access = await WorkspaceAccess.LoadByCodeAsync(
            workspaceRepository,
            taskRepository,
            currentUser,
            code,
            includeArtifacts: true,
            cancellationToken);
        if (!access.IsSuccess)
        {
            return Result.From(access);
        }

        var workspace = access.Value!.Workspace;
        var task = access.Value.Task;
        if (workspace.Status == ArtifactWorkspaceStatus.Finalized)
        {
            return Result.Invalid("Workspace is already finalized.");
        }

        if (workspace.Artifacts.Count == 0)
        {
            return Result.Invalid("Workspace has no draft artifacts to submit for final review.");
        }

        if (task.Status is not AgentTaskStatus.WorkspaceReady and not AgentTaskStatus.WaitingFinalApproval)
        {
            return Result.Invalid("Only workspace-ready tasks can submit final review.");
        }

        var prepared = await finalOutputApprovalCoordinator.EnsurePendingAsync(
            task,
            workspace,
            currentUser.Id!.Value,
            cancellationToken);
        if (!prepared.IsSuccess)
        {
            return Result.From(prepared);
        }

        var files = await fileStore.ListAsync(workspace.WorkspaceCode, cancellationToken);
        return Result.Success(ArtifactWorkspaceMapper.Map(
            prepared.Value!.Workspace,
            prepared.Value.Task,
            files));
    }

    public async Task<Result<ArtifactWorkspaceDto>> FinalizeAsync(
        string code,
        CancellationToken cancellationToken)
    {
        var access = await WorkspaceAccess.LoadByCodeForOwnerOrPermissionAsync(
            workspaceRepository,
            taskRepository,
            currentUser,
            identityAccessService,
            code,
            includeArtifacts: true,
            ownerPermission: AgentApprovalPermissions.FinalizeWorkspace,
            privilegedPermissions: [AgentApprovalPermissions.FinalizeWorkspace],
            approvalRepository: null,
            requireFinalOutputApprovalForPrivilegedAccess: false,
            cancellationToken);
        if (!access.IsSuccess)
        {
            return Result.From(access);
        }

        var workspace = access.Value!.Workspace;
        var task = access.Value.Task;
        if (workspace.Status == ArtifactWorkspaceStatus.Finalized)
        {
            var finalizedFiles = await fileStore.ListAsync(workspace.WorkspaceCode, cancellationToken);
            return Result.Success(ArtifactWorkspaceMapper.Map(workspace, task, finalizedFiles));
        }

        if (workspace.Artifacts.Count == 0)
        {
            return Result.Invalid("Workspace has no draft artifacts to finalize.");
        }

        var resume = await finalOutputApprovalCoordinator.ValidateLegacyResumeAsync(
            task,
            workspace,
            cancellationToken);
        if (!resume.IsSuccess)
        {
            return Result.From(resume);
        }

        var files = await fileStore.ListAsync(workspace.WorkspaceCode, cancellationToken);
        return Result.Success(ArtifactWorkspaceMapper.Map(workspace, task, files));
    }
}
