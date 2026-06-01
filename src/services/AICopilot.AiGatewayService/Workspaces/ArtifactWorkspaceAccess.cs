using AICopilot.AiGatewayService.AgentTasks;
using AICopilot.Core.AiGateway.Aggregates.AgentTasks;
using AICopilot.Core.AiGateway.Aggregates.Approvals;
using AICopilot.Core.AiGateway.Aggregates.Artifacts;
using AICopilot.Core.AiGateway.Ids;
using AICopilot.Core.AiGateway.Specifications.AgentTasks;
using AICopilot.Core.AiGateway.Specifications.Approvals;
using AICopilot.Core.AiGateway.Specifications.Artifacts;
using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Repository;
using AICopilot.SharedKernel.Result;

namespace AICopilot.AiGatewayService.Workspaces;

internal sealed record WorkspaceAccess(ArtifactWorkspace Workspace, AgentTask Task)
{
    public static async Task<Result<WorkspaceAccess>> LoadByCodeAsync(
        IReadRepository<ArtifactWorkspace> workspaceRepository,
        IReadRepository<AgentTask> taskRepository,
        ICurrentUser currentUser,
        string code,
        bool includeArtifacts,
        CancellationToken cancellationToken)
    {
        if (currentUser.Id is not { } userId)
        {
            return Result.Unauthorized(new ApiProblemDescriptor(
                AuthProblemCodes.Unauthorized,
                "Current user id is missing or invalid."));
        }

        if (string.IsNullOrWhiteSpace(code))
        {
            return Result.Invalid("Workspace code is required.");
        }

        var workspace = await workspaceRepository.FirstOrDefaultAsync(
            new ArtifactWorkspaceByCodeSpec(code.Trim(), includeArtifacts),
            cancellationToken);
        if (workspace is null)
        {
            return Result.NotFound();
        }

        var task = await taskRepository.FirstOrDefaultAsync(
            new AgentTaskByIdForUserSpec(workspace.TaskId, userId, includeSteps: true),
            cancellationToken);
        return task is null
            ? Result.NotFound()
            : Result.Success(new WorkspaceAccess(workspace, task));
    }

    public static async Task<Result<WorkspaceAccess>> LoadByCodeForOwnerOrPermissionAsync(
        IReadRepository<ArtifactWorkspace> workspaceRepository,
        IReadRepository<AgentTask> taskRepository,
        ICurrentUser currentUser,
        IIdentityAccessService identityAccessService,
        string code,
        bool includeArtifacts,
        string ownerPermission,
        IReadOnlyCollection<string> privilegedPermissions,
        IReadRepository<ApprovalRequest>? approvalRepository,
        bool requireFinalOutputApprovalForPrivilegedAccess,
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

        if (string.IsNullOrWhiteSpace(code))
        {
            return Result.Invalid("Workspace code is required.");
        }

        var workspace = await workspaceRepository.FirstOrDefaultAsync(
            new ArtifactWorkspaceByCodeSpec(code.Trim(), includeArtifacts),
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

        var currentAccess = currentAccessResult.Value!;
        if (task.UserId == currentAccess.UserId)
        {
            return AgentApprovalPermissions.HasPermission(currentAccess, ownerPermission)
                ? Result.Success(new WorkspaceAccess(workspace, task))
                : AgentApprovalPermissions.ForbiddenMissing(ownerPermission);
        }

        if (!privilegedPermissions.Any(permission => AgentApprovalPermissions.HasPermission(currentAccess, permission)))
        {
            return Result.NotFound();
        }

        if (requireFinalOutputApprovalForPrivilegedAccess &&
            (approvalRepository is null ||
             !await HasFinalOutputApprovalAsync(approvalRepository, task.Id, workspace.WorkspaceCode, cancellationToken)))
        {
            return Result.NotFound();
        }

        return Result.Success(new WorkspaceAccess(workspace, task));
    }

    public static async Task<bool> HasFinalOutputApprovalAsync(
        IReadRepository<ApprovalRequest> approvalRepository,
        AgentTaskId taskId,
        string workspaceCode,
        CancellationToken cancellationToken)
    {
        var approvals = await approvalRepository.ListAsync(
            new ApprovalRequestsByTaskSpec(taskId),
            cancellationToken);
        return approvals.Any(item =>
            item.ApprovalType == AgentApprovalType.FinalOutput &&
            string.Equals(item.TargetId, workspaceCode, StringComparison.Ordinal));
    }
}
