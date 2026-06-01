using AICopilot.AiGatewayService.AgentTasks;
using AICopilot.AiGatewayService.CloudReadiness;
using AICopilot.Core.AiGateway.Aggregates.AgentTasks;
using AICopilot.Core.AiGateway.Aggregates.Approvals;
using AICopilot.Core.AiGateway.Aggregates.Artifacts;
using AICopilot.Core.AiGateway.Specifications.AgentTasks;
using AICopilot.Core.AiGateway.Specifications.Approvals;
using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Messaging;
using AICopilot.SharedKernel.Repository;
using AICopilot.SharedKernel.Result;

namespace AICopilot.AiGatewayService.Workspaces;

public sealed class SubmitFinalReviewCommandHandler(
    IRepository<ArtifactWorkspace> workspaceRepository,
    IRepository<AgentTask> taskRepository,
    IRepository<ApprovalRequest> approvalRepository,
    IArtifactWorkspaceFileStore fileStore,
    AgentAuditRecorder auditRecorder,
    IAuditLogWriter auditLogWriter,
    ICurrentUser currentUser)
    : ICommandHandler<SubmitFinalReviewCommand, Result<ArtifactWorkspaceDto>>
{
    public async Task<Result<ArtifactWorkspaceDto>> Handle(
        SubmitFinalReviewCommand request,
        CancellationToken cancellationToken)
    {
        var access = await WorkspaceAccess.LoadByCodeAsync(
            workspaceRepository,
            taskRepository,
            currentUser,
            request.Code,
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
        }

        if (task.Status == AgentTaskStatus.WorkspaceReady)
        {
            task.WaitForFinalApproval(now);
        }

        taskRepository.Update(task);
        workspaceRepository.Update(workspace);
        await workspaceRepository.SaveChangesAsync(cancellationToken);
        await auditLogWriter.SaveChangesAsync(cancellationToken);

        var files = await fileStore.ListAsync(workspace.WorkspaceCode, cancellationToken);
        return Result.Success(ArtifactWorkspaceMapper.Map(workspace, task, files));
    }
}

public sealed class FinalizeArtifactWorkspaceCommandHandler(
    IRepository<ArtifactWorkspace> workspaceRepository,
    IRepository<AgentTask> taskRepository,
    IRepository<AgentTaskRunAttempt> runAttemptRepository,
    IRepository<ApprovalRequest> approvalRepository,
    IArtifactWorkspaceFileStore fileStore,
    AgentAuditRecorder auditRecorder,
    IAuditLogWriter auditLogWriter,
    ICurrentUser currentUser,
    IIdentityAccessService identityAccessService,
    CloudReadonlyProductionOperationsService? productionOperationsService = null)
    : ICommandHandler<FinalizeArtifactWorkspaceCommand, Result<ArtifactWorkspaceDto>>
{
    public async Task<Result<ArtifactWorkspaceDto>> Handle(
        FinalizeArtifactWorkspaceCommand request,
        CancellationToken cancellationToken)
    {
        var access = await WorkspaceAccess.LoadByCodeForOwnerOrPermissionAsync(
            workspaceRepository,
            taskRepository,
            currentUser,
            identityAccessService,
            request.Code,
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

        var backfillWarnings = productionOperationsService?.BackfillFinalArtifactRefs(
            task.Id.Value,
            workspace.Artifacts.Where(artifact => artifact.Status == ArtifactStatus.Final).ToArray()) ?? [];

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
            var attempt = await runAttemptRepository.FirstOrDefaultAsync(
                new AgentTaskRunAttemptByIdSpec(activeRunAttemptId.Value),
                cancellationToken);
            if (attempt is not null && !attempt.IsTerminal)
            {
                attempt.MarkSucceeded(now, "Workspace final output approved.");
                runAttemptRepository.Update(attempt);
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
            backfillWarnings.Count == 0
                ? "Workspace artifacts finalized. Production Pilot ledger artifact refs backfilled when applicable."
                : $"Workspace artifacts finalized. Production Pilot ledger backfill warnings: {string.Join(" | ", backfillWarnings)}",
            cancellationToken);
        await workspaceRepository.SaveChangesAsync(cancellationToken);
        await auditLogWriter.SaveChangesAsync(cancellationToken);

        var files = await fileStore.ListAsync(workspace.WorkspaceCode, cancellationToken);
        return Result.Success(ArtifactWorkspaceMapper.Map(workspace, task, files));
    }
}
