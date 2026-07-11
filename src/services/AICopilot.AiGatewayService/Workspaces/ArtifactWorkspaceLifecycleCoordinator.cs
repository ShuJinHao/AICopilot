using AICopilot.AiGatewayService.AgentTasks;
using AICopilot.AiGatewayService.Sessions;
using AICopilot.Core.AiGateway.Aggregates.AgentTasks;
using AICopilot.Core.AiGateway.Aggregates.Approvals;
using AICopilot.Core.AiGateway.Aggregates.Artifacts;
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
            return Result.Invalid("Workspace final output is already approved; call finalize to publish final artifacts.");
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

        var now = DateTimeOffset.UtcNow;
        var approvals = await approvalRepository.ListAsync(
            new ApprovalRequestsByTaskSpec(task.Id),
            cancellationToken);
        var approval = approvals.FirstOrDefault(item =>
            item.ApprovalType == AgentApprovalType.FinalOutput &&
            string.Equals(item.TargetId, workspace.WorkspaceCode, StringComparison.Ordinal));
        if (approval is null)
        {
            return Result.Invalid("Final output approval is required before workspace finalization.");
        }

        if (approval.Status == AgentApprovalStatus.Pending)
        {
            return Result.Invalid("Final output approval is still pending.");
        }

        if (approval.Status == AgentApprovalStatus.Rejected)
        {
            return Result.Invalid("Workspace final output approval was rejected.");
        }

        if (approval.Status is AgentApprovalStatus.Cancelled or AgentApprovalStatus.Expired)
        {
            return Result.Invalid("Workspace final output approval is no longer valid.");
        }

        if (approval.Status != AgentApprovalStatus.Approved)
        {
            return Result.Invalid("Final output approval is not approved.");
        }

        foreach (var artifact in workspace.Artifacts.Where(item => item.Status != ArtifactStatus.Final))
        {
            if (artifact.Status is ArtifactStatus.Rejected or ArtifactStatus.Deleted)
            {
                return Result.Invalid($"Artifact {artifact.Name} cannot be finalized from status {artifact.Status}.");
            }

            if (artifact.Status is ArtifactStatus.Draft or ArtifactStatus.Reviewing)
            {
                artifact.Approve(now);
            }

            var currentPath = ArtifactPathGuard.NormalizeRelativePath(artifact.RelativePath);
            var finalPath = $"final/{currentPath}";
            await fileStore.CopyAsync(workspace.WorkspaceCode, currentPath, finalPath, artifact.MimeType, cancellationToken);
            artifact.MarkFinal(finalPath, now);
        }

        workspace.FinalizeWorkspace(now);
        if (task.Status == AgentTaskStatus.WorkspaceReady)
        {
            task.WaitForFinalApproval(now);
        }

        if (task.Status == AgentTaskStatus.WaitingFinalApproval)
        {
            task.MarkFinalized(now);
        }

        if (task.Status == AgentTaskStatus.Finalized)
        {
            task.Complete("产物已确认并输出到 final 目录。", now);
        }

        var activeRunAttemptId = task.ActiveRunAttemptId;
        var finalStep = task.Steps
            .OrderByDescending(step => step.StepIndex)
            .FirstOrDefault(step => string.Equals(step.ToolCode, "finalize_artifacts", StringComparison.OrdinalIgnoreCase));
        if (finalStep is not null &&
            finalStep.Status is AgentStepStatus.WaitingApproval or AgentStepStatus.Approved)
        {
            finalStep.Complete("""{"status":"finalized"}""", now);
        }

        if (activeRunAttemptId is not null)
        {
            var attempt = await runAttemptStore.FirstByIdAsync(activeRunAttemptId.Value, cancellationToken);
            if (attempt is not null && !attempt.IsTerminal)
            {
                attempt.MarkSucceeded(now, "Workspace final output approved.");
                runAttemptStore.Update(attempt);
                task.ReleaseRunLease(now, clearActiveAttempt: true);
            }
        }

        workspaceRepository.Update(workspace);
        taskRepository.Update(task);
        await auditRecorder.RecordApprovalDecisionAsync(
            approval,
            task,
            AuditResults.Succeeded,
            "Workspace final output approved.",
            cancellationToken);
        await auditRecorder.RecordWorkspaceFinalizedAsync(
            task,
            workspace,
            AuditResults.Succeeded,
            "Workspace artifacts finalized.",
            cancellationToken);
        if (timelineProjectionWriter is not null)
        {
            if (finalStep is not null &&
                finalStep.Status == AgentStepStatus.Completed)
            {
                await timelineProjectionWriter.StageStepCompletedAsync(task, finalStep, cancellationToken);
            }

            await timelineProjectionWriter.StageWorkspaceFinalizedAsync(task, workspace, cancellationToken);
        }

        await workspaceRepository.SaveChangesAsync(cancellationToken);

        var files = await fileStore.ListAsync(workspace.WorkspaceCode, cancellationToken);
        return Result.Success(ArtifactWorkspaceMapper.Map(workspace, task, files));
    }
}
