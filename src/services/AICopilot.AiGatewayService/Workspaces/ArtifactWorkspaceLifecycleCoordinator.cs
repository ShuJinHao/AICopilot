using AICopilot.AiGatewayService.AgentTasks;
using AICopilot.AiGatewayService.Sessions;
using AICopilot.Core.AiGateway.Aggregates.AgentTasks;
using AICopilot.Core.AiGateway.Aggregates.Approvals;
using AICopilot.Core.AiGateway.Aggregates.Artifacts;
using AICopilot.Core.AiGateway.Aggregates.Tools;
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
    private const string FinalizationCheckpointOutputJson =
        """{"resultType":"finalization-checkpoint","status":"finalized"}""";

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
        var attempts = await runAttemptStore.ListByTaskAsync(task.Id, cancellationToken);
        var approvals = await approvalRepository.ListAsync(
            new ApprovalRequestsByTaskSpec(task.Id),
            cancellationToken);
        var checkpointState = AgentFinalizationCheckpointStateValidator.ValidatePaused(
            task,
            workspace,
            approvals,
            attempts);
        if (!checkpointState.IsSuccess)
        {
            return Result.From(checkpointState);
        }

        if (checkpointState.Value!.Phase != AgentFinalizationCheckpointPhase.PendingApproval)
        {
            return FinalizationConflict(
                "Final review confirmation requires the pending runtime-created final-output approval.");
        }

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
        var approvals = await approvalRepository.ListAsync(
            new ApprovalRequestsByTaskSpec(task.Id),
            cancellationToken);
        var matchingApprovals = approvals.Where(item =>
            item.ApprovalType == AgentApprovalType.FinalOutput &&
            string.Equals(item.TargetId, workspace.WorkspaceCode, StringComparison.Ordinal))
            .ToArray();
        var allFinalApprovals = approvals
            .Where(item => item.ApprovalType == AgentApprovalType.FinalOutput)
            .ToArray();
        if (allFinalApprovals.Length == 0)
        {
            return FinalizationConflict(
                "Final output approval is required before workspace finalization.");
        }

        if (allFinalApprovals.Length != 1 || matchingApprovals.Length != 1)
        {
            return FinalizationConflict("Workspace finalization requires exactly one matching final-output approval.");
        }

        var approval = matchingApprovals[0];
        if (approval.TaskId != task.Id ||
            !string.Equals(approval.TargetId, workspace.WorkspaceCode, StringComparison.Ordinal) ||
            approval.RequestedBy != task.UserId)
        {
            return FinalizationConflict(
                "Final output approval identity is inconsistent.");
        }

        var decisionProof = AgentFinalizationCheckpointStateValidator
            .ValidateApprovalDecisionProof(approval);
        if (!decisionProof.IsSuccess)
        {
            return Result.From(decisionProof);
        }

        var pendingApprovals = approvals
            .Where(item => item.Status == AgentApprovalStatus.Pending)
            .ToArray();
        var isSolePendingFinalApproval =
            approval.Status == AgentApprovalStatus.Pending &&
            pendingApprovals.Length == 1 &&
            pendingApprovals[0].Id == approval.Id;
        if (pendingApprovals.Length > 0 && !isSolePendingFinalApproval)
        {
            return FinalizationConflict(
                "Workspace finalization cannot leave competing pending task approvals behind.");
        }

        if (approval.Status == AgentApprovalStatus.Pending)
        {
            return Result.Invalid(new ApiProblemDescriptor(
                AppProblemCodes.ApprovalPending,
                "Final output approval is still pending."));
        }

        if (approval.Status == AgentApprovalStatus.Rejected)
        {
            return Result.Invalid(new ApiProblemDescriptor(
                AppProblemCodes.AgentApprovalRejected,
                "Workspace final output approval was rejected."));
        }

        if (approval.Status is AgentApprovalStatus.Cancelled or AgentApprovalStatus.Expired)
        {
            return FinalizationConflict(
                "Workspace final output approval is no longer valid.");
        }

        if (approval.Status != AgentApprovalStatus.Approved)
        {
            return FinalizationConflict(
                "Final output approval status is inconsistent.");
        }

        var runAttempts = await runAttemptStore.ListByTaskAsync(task.Id, cancellationToken);
        var files = await fileStore.ListAsync(workspace.WorkspaceCode, cancellationToken);
        var finalStepResult = LoadExactFinalizationCheckpoint(task);
        if (!finalStepResult.IsSuccess)
        {
            return Result.From(finalStepResult);
        }

        var finalStep = finalStepResult.Value!;
        if (workspace.Status == ArtifactWorkspaceStatus.Finalized)
        {
            var terminalState = ValidateFinalizedState(
                task,
                finalStep,
                approval,
                runAttempts,
                workspace,
                files);
            return terminalState.IsSuccess
                ? Result.Success(ArtifactWorkspaceMapper.Map(workspace, task, files))
                : Result.From(terminalState);
        }

        var checkpointState = AgentFinalizationCheckpointStateValidator.ValidatePaused(
            task,
            workspace,
            approvals,
            runAttempts);
        if (!checkpointState.IsSuccess ||
            checkpointState.Value!.Phase != AgentFinalizationCheckpointPhase.Approved)
        {
            return checkpointState.IsSuccess
                ? FinalizationConflict(
                    "Workspace finalization requires the approved final-output checkpoint.")
                : Result.From(checkpointState);
        }

        var activeAttempt = checkpointState.Value.ActiveAttempt;

        var artifactPlansResult = BuildArtifactFinalizationPlans(workspace, files);
        if (!artifactPlansResult.IsSuccess)
        {
            return Result.From(artifactPlansResult);
        }

        var artifactPlans = artifactPlansResult.Value!;
        var now = DateTimeOffset.UtcNow;
        foreach (var plan in artifactPlans)
        {
            await fileStore.CopyAsync(
                workspace.WorkspaceCode,
                plan.SourceRelativePath,
                plan.FinalRelativePath,
                plan.Artifact.MimeType,
                cancellationToken);
        }

        foreach (var plan in artifactPlans)
        {
            if (plan.Artifact.Status is ArtifactStatus.Draft or ArtifactStatus.Reviewing)
            {
                plan.Artifact.Approve(now);
            }

            plan.Artifact.MarkFinal(plan.FinalRelativePath, now);
        }

        workspace.FinalizeWorkspace(now);
        finalStep.Complete(FinalizationCheckpointOutputJson, now);
        task.MarkFinalized(now);
        task.Complete("产物已确认并输出到 final 目录。", now);
        activeAttempt.MarkSucceeded(now, "Workspace final output approved.");
        runAttemptStore.Update(activeAttempt);
        task.ReleaseRunLease(now, clearActiveAttempt: true);

        workspaceRepository.Update(workspace);
        taskRepository.Update(task);
        await auditRecorder.RecordWorkspaceFinalizedAsync(
            task,
            workspace,
            AuditResults.Succeeded,
            "Workspace artifacts finalized.",
            cancellationToken);
        if (timelineProjectionWriter is not null)
        {
            if (finalStep.Status == AgentStepStatus.Completed)
            {
                await timelineProjectionWriter.StageStepCompletedAsync(task, finalStep, cancellationToken);
            }

            await timelineProjectionWriter.StageWorkspaceFinalizedAsync(task, workspace, cancellationToken);
        }

        await workspaceRepository.SaveChangesAsync(cancellationToken);

        files = await fileStore.ListAsync(workspace.WorkspaceCode, cancellationToken);
        return Result.Success(ArtifactWorkspaceMapper.Map(workspace, task, files));
    }

    private static Result<AgentStep> LoadExactFinalizationCheckpoint(AgentTask task)
    {
        return AgentFinalizationCheckpointStateValidator.LoadExactFinalStep(task);
    }

    private static Result ValidateFinalizedState(
        AgentTask task,
        AgentStep finalStep,
        ApprovalRequest approval,
        IReadOnlyCollection<AgentTaskRunAttempt> runAttempts,
        ArtifactWorkspace workspace,
        IReadOnlyCollection<ArtifactWorkspaceFileItem> files)
    {
        if (task.Status != AgentTaskStatus.Completed ||
            task.CompletedAt is null ||
            string.IsNullOrWhiteSpace(task.FinalSummary) ||
            task.WorkspaceId != workspace.Id ||
            workspace.TaskId != task.Id ||
            workspace.Status != ArtifactWorkspaceStatus.Finalized ||
            task.ActiveRunAttemptId is not null ||
            HasTaskLease(task) ||
            finalStep.Status != AgentStepStatus.Completed ||
            finalStep.ErrorMessage is not null ||
            finalStep.StartedAt is not null ||
            finalStep.FinishedAt is null ||
            !string.Equals(finalStep.OutputJson, FinalizationCheckpointOutputJson, StringComparison.Ordinal))
        {
            return FinalizationConflict(
                "Finalized workspace task and checkpoint state is incomplete or inconsistent.");
        }

        var taskCompletedAt = task.CompletedAt.Value;
        var finalStepFinishedAt = finalStep.FinishedAt.Value;
        if (taskCompletedAt < task.CreatedAt ||
            workspace.UpdatedAt > finalStepFinishedAt ||
            finalStepFinishedAt > taskCompletedAt)
        {
            return FinalizationConflict(
                "Finalized workspace task and checkpoint timestamps are causally inconsistent.");
        }

        var provenance = AgentFinalizationCheckpointStateValidator.ValidateArtifactProvenance(
            task,
            workspace);
        if (!provenance.IsSuccess)
        {
            return Result.From(provenance);
        }

        var attemptTimeline = AgentFinalizationCheckpointStateValidator
            .ValidateRunAttemptTimeline(task, runAttempts);
        if (!attemptTimeline.IsSuccess)
        {
            return FinalizationConflict(
                "Finalized workspace run-attempt state is incomplete or inconsistent.");
        }

        var validatedAttempts = attemptTimeline.Value!;
        if (validatedAttempts.Any(attempt =>
                !attempt.IsTerminal ||
                attempt.CompletedAt is null ||
                attempt.CompletedAt.Value < attempt.StartedAt))
        {
            return FinalizationConflict(
                "Finalized workspace run-attempt state is incomplete or inconsistent.");
        }

        var latestAttempt = validatedAttempts[^1];
        if (latestAttempt.TaskId != task.Id ||
            latestAttempt.Status != AgentTaskRunAttemptStatus.Succeeded ||
            latestAttempt.FailureCode is not null ||
            latestAttempt.CompletedAt!.Value < taskCompletedAt ||
            task.RunAttemptCount != latestAttempt.AttemptNo)
        {
            return FinalizationConflict(
                "Finalized workspace does not have a matching successful terminal run attempt.");
        }

        var artifactPaths = workspace.Artifacts
            .Select(artifact => artifact.RelativePath)
            .ToArray();
        var finalFilePaths = files
            .Where(file =>
                !file.IsDirectory &&
                file.RelativePath.StartsWith("final/", StringComparison.OrdinalIgnoreCase))
            .Select(file => file.RelativePath)
            .ToArray();
        if (workspace.Artifacts.Count == 0 ||
            workspace.Artifacts.Any(artifact =>
                artifact.WorkspaceId != workspace.Id ||
                artifact.TaskId != task.Id ||
                artifact.Status != ArtifactStatus.Final ||
                artifact.FinalizedAt is null ||
                !IsCanonicalFinalPath(artifact.RelativePath) ||
                !HasMatchingFile(files, artifact.RelativePath, artifact.FileSize)) ||
            artifactPaths.Distinct(StringComparer.Ordinal).Count() != artifactPaths.Length ||
            artifactPaths.Distinct(StringComparer.OrdinalIgnoreCase).Count() != artifactPaths.Length ||
            finalFilePaths.Any(path => !IsCanonicalFinalPath(path)) ||
            finalFilePaths.Distinct(StringComparer.Ordinal).Count() != finalFilePaths.Length ||
            finalFilePaths.Distinct(StringComparer.OrdinalIgnoreCase).Count() != finalFilePaths.Length ||
            !artifactPaths.ToHashSet(StringComparer.Ordinal)
                .SetEquals(finalFilePaths))
        {
            return FinalizationConflict(
                "Finalized workspace artifact metadata and stored files are inconsistent.");
        }

        var approvedAt = approval.ApprovedAt!.Value;
        if (approval.CreatedAt < task.CreatedAt ||
            approval.CreatedAt < workspace.CreatedAt ||
            workspace.Artifacts.Any(artifact =>
        {
            var producer = task.Steps.Single(step => step.Id == artifact.CreatedByStepId!.Value);
            var finalizedAt = artifact.FinalizedAt!.Value;
            return producer.FinishedAt!.Value > approval.CreatedAt ||
                   approvedAt > finalizedAt ||
                   producer.FinishedAt!.Value > finalizedAt ||
                   finalizedAt > workspace.UpdatedAt;
        }))
        {
            return FinalizationConflict(
                "Finalized workspace approval, producer, artifact, and aggregate timestamps are causally inconsistent.");
        }

        return Result.Success();
    }

    private static Result<IReadOnlyCollection<ArtifactFinalizationPlan>> BuildArtifactFinalizationPlans(
        ArtifactWorkspace workspace,
        IReadOnlyCollection<ArtifactWorkspaceFileItem> files)
    {
        var plans = new List<ArtifactFinalizationPlan>(workspace.Artifacts.Count);
        var finalPaths = new HashSet<string>(StringComparer.Ordinal);
        if (files.Any(file =>
                !file.IsDirectory &&
                file.RelativePath.StartsWith("final/", StringComparison.OrdinalIgnoreCase)))
        {
            return FinalizationConflict(
                "Workspace final folder must be empty before finalization.");
        }

        try
        {
            foreach (var artifact in workspace.Artifacts)
            {
                if (artifact.Status is not ArtifactStatus.Draft and
                    not ArtifactStatus.Reviewing and
                    not ArtifactStatus.Approved)
                {
                    return FinalizationConflict(
                        "Workspace contains an artifact that is not eligible for finalization.");
                }

                var sourcePath = ArtifactPathGuard.NormalizeRelativePath(artifact.RelativePath);
                if (sourcePath.StartsWith("final/", StringComparison.OrdinalIgnoreCase) ||
                    !HasMatchingFile(files, sourcePath, artifact.FileSize))
                {
                    return FinalizationConflict(
                        "Workspace artifact metadata and source files are inconsistent.");
                }

                var finalPath = ArtifactPathGuard.NormalizeFinalPath($"final/{sourcePath}");
                if (!finalPaths.Add(finalPath) ||
                    finalPaths.Count != finalPaths.Distinct(StringComparer.OrdinalIgnoreCase).Count())
                {
                    return FinalizationConflict(
                        "Workspace final artifact paths are duplicated or already occupied.");
                }

                plans.Add(new ArtifactFinalizationPlan(artifact, sourcePath, finalPath));
            }
        }
        catch (ArgumentException)
        {
            return FinalizationConflict("Workspace artifact paths are invalid.");
        }

        return Result.Success<IReadOnlyCollection<ArtifactFinalizationPlan>>(plans);
    }

    private static bool HasMatchingFile(
        IReadOnlyCollection<ArtifactWorkspaceFileItem> files,
        string relativePath,
        long expectedSize) =>
        files.Count(file =>
            !file.IsDirectory &&
            string.Equals(file.RelativePath, relativePath, StringComparison.Ordinal) &&
            file.FileSize == expectedSize) == 1;

    private static bool IsCanonicalFinalPath(string relativePath)
    {
        try
        {
            return relativePath.StartsWith("final/", StringComparison.Ordinal) &&
                string.Equals(
                relativePath,
                ArtifactPathGuard.NormalizeFinalPath(relativePath),
                StringComparison.Ordinal);
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static bool HasTaskLease(AgentTask task) =>
        task.RunLeaseId is not null ||
        task.RunLeaseOwner is not null ||
        task.RunLeaseExpiresAt is not null;

    private static Result FinalizationConflict(string detail) =>
        Result.Invalid(new ApiProblemDescriptor(
            AppProblemCodes.AgentFinalizationStateConflict,
            detail));

    private sealed record ArtifactFinalizationPlan(
        Artifact Artifact,
        string SourceRelativePath,
        string FinalRelativePath);
}
