using AICopilot.AiGatewayService.AgentTasks;
using AICopilot.AiGatewayService.Sessions;
using AICopilot.Core.AiGateway.Aggregates.AgentTasks;
using AICopilot.Core.AiGateway.Aggregates.Approvals;
using AICopilot.Core.AiGateway.Aggregates.Artifacts;
using AICopilot.Core.AiGateway.Runtime.AgentExecution;
using AICopilot.Core.AiGateway.Specifications.Approvals;
using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Repository;
using AICopilot.SharedKernel.Result;

namespace AICopilot.AiGatewayService.Workspaces;

public sealed class ArtifactWorkspaceLifecycleCoordinator(
    IRepository<ArtifactWorkspace> workspaceRepository,
    IRepository<AgentTask> taskRepository,
    IRepository<ApprovalRequest> approvalRepository,
    IAgentTaskRunAttemptStore runAttemptStore,
    IArtifactWorkspaceFileStore fileStore,
    IAgentTaskRunQueue runQueue,
    AgentAuditRecorder auditRecorder,
    ICurrentUser currentUser,
    IIdentityAccessService identityAccessService,
    MessageTimelineProjectionWriter? timelineProjectionWriter = null)
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

        var approvals = await approvalRepository.ListAsync(
            new ApprovalRequestsByTaskSpec(task.Id),
            cancellationToken);
        var finalApproval = approvals.FirstOrDefault(item =>
            item.ApprovalType == AgentApprovalType.FinalOutput &&
            string.Equals(item.TargetId, workspace.WorkspaceCode, StringComparison.Ordinal));

        if (finalApproval?.Status == AgentApprovalStatus.Rejected)
        {
            return Result.Invalid("Workspace final output approval was rejected.");
        }

        if (finalApproval?.Status == AgentApprovalStatus.Approved)
        {
            return Result.Invalid("Workspace final output is already approved and can only be published by the durable finalization NodeRun.");
        }

        var now = DateTimeOffset.UtcNow;
        if (finalApproval is null)
        {
            finalApproval = new ApprovalRequest(
                task.Id,
                AgentApprovalType.FinalOutput,
                workspace.WorkspaceCode,
                currentUser.Id!.Value,
                now);
            approvalRepository.Add(finalApproval);
            await auditRecorder.RecordFinalReviewSubmittedAsync(task, workspace, finalApproval, cancellationToken);
            if (timelineProjectionWriter is not null)
            {
                await timelineProjectionWriter.StageApprovalRequestedAsync(task, finalApproval, cancellationToken);
            }
        }

        if (task.Status == AgentTaskStatus.WorkspaceReady)
        {
            task.WaitForFinalApproval(now);
        }

        taskRepository.Update(task);
        workspaceRepository.Update(workspace);
        await workspaceRepository.SaveChangesAsync(cancellationToken);

        var files = await fileStore.ListAsync(workspace.WorkspaceCode, cancellationToken);
        return Result.Success(ArtifactWorkspaceMapper.Map(workspace, task, files));
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

        var approvals = await approvalRepository.ListAsync(
            new ApprovalRequestsByTaskSpec(task.Id),
            cancellationToken);
        if (task.IsRunInProgress(DateTimeOffset.UtcNow))
        {
            var activeFiles = await fileStore.ListAsync(workspace.WorkspaceCode, cancellationToken);
            return Result.Success(ArtifactWorkspaceMapper.Map(workspace, task, activeFiles));
        }

        var attempts = await runAttemptStore.ListByTaskAsync(task.Id, cancellationToken);
        var checkpoint = AgentFinalizationCheckpointStateValidator.ValidatePaused(
            task,
            workspace,
            approvals,
            attempts);
        if (!checkpoint.IsSuccess)
        {
            return Result.From(checkpoint);
        }

        if (checkpoint.Value!.Phase != AgentFinalizationCheckpointPhase.Approved)
        {
            return Result.Invalid("Final output approval is still pending.");
        }

        if (currentUser.Id is not { } requestedBy)
        {
            return Result.Unauthorized(new ApiProblemDescriptor(
                AuthProblemCodes.Unauthorized,
                "Current user id is missing or invalid."));
        }

        var queued = await runQueue.EnqueueAsync(
            task,
            AgentTaskRunTriggerType.ApprovalResume,
            requestedBy,
            cancellationToken);
        var alreadyQueued = queued.Errors?
            .OfType<ApiProblemDescriptor>()
            .Any(error => error.Code == AppProblemCodes.AgentTaskRunInProgress) == true;
        if (!queued.IsSuccess && !alreadyQueued)
        {
            return Result.From(queued);
        }

        var files = await fileStore.ListAsync(workspace.WorkspaceCode, cancellationToken);
        return Result.Success(ArtifactWorkspaceMapper.Map(workspace, task, files));
    }
}
