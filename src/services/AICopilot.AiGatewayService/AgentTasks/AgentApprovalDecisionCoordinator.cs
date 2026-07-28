using AICopilot.Core.AiGateway.Aggregates.AgentTasks;
using AICopilot.Core.AiGateway.Aggregates.Approvals;
using AICopilot.Core.AiGateway.Aggregates.Artifacts;
using AICopilot.Core.AiGateway.Ids;
using AICopilot.Core.AiGateway.Specifications.AgentTasks;
using AICopilot.Core.AiGateway.Specifications.Approvals;
using AICopilot.AiGatewayService.Sessions;
using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Repository;
using AICopilot.SharedKernel.Result;

namespace AICopilot.AiGatewayService.AgentTasks;

public sealed class AgentApprovalDecisionCoordinator(
    IRepository<ApprovalRequest> approvalRepository,
    IRepository<AgentTask> taskRepository,
    IRepository<ArtifactWorkspace> workspaceRepository,
    IAgentTaskRunAttemptStore runAttemptStore,
    AgentAuditRecorder auditRecorder,
    IAgentTaskRunQueue runQueue,
    ICurrentUser currentUser,
    IIdentityAccessService identityAccessService,
    AgentPlanDraftConfirmationService planDraftConfirmationService,
    MessageTimelineProjectionWriter? timelineProjectionWriter = null)
{
    public Task<Result<AgentApprovalRequestDto>> ApproveAsync(
        Guid approvalId,
        string? comment,
        CancellationToken cancellationToken)
    {
        return DecideAsync(approvalId, comment, isApproved: true, cancellationToken);
    }

    public Task<Result<AgentApprovalRequestDto>> RejectAsync(
        Guid approvalId,
        string? comment,
        CancellationToken cancellationToken)
    {
        return DecideAsync(approvalId, comment, isApproved: false, cancellationToken);
    }

    public async Task<Result<AgentTask>> ApprovePlanForTaskAsync(
        Guid taskId,
        string? comment,
        CancellationToken cancellationToken)
    {
        if (currentUser.Id is not { } userId)
        {
            return AgentApprovalAccess.MissingUser();
        }

        if (taskId == Guid.Empty)
        {
            return Result.Invalid("Agent task id is required.");
        }

        var access = await AgentApprovalPermissions.LoadCurrentUserAccessAsync(
            currentUser,
            identityAccessService,
            cancellationToken);
        if (!access.IsSuccess)
        {
            return Result.From(access);
        }

        if (!AgentApprovalPermissions.HasPermission(
                access.Value,
                AgentApprovalPermissions.ApproveAgentTaskPlan))
        {
            return AgentApprovalPermissions.ForbiddenMissing(
                AgentApprovalPermissions.ApproveAgentTaskPlan);
        }

        var task = await taskRepository.FirstOrDefaultAsync(
            new AgentTaskByIdSpec(new AgentTaskId(taskId), includeSteps: true),
            cancellationToken);
        if (task is null || task.UserId != userId)
        {
            return Result.NotFound();
        }

        var targetId = task.Id.Value.ToString();
        var approvals = await approvalRepository.ListAsync(
            new ApprovalRequestsByTaskSpec(task.Id),
            cancellationToken);
        var matching = approvals.Where(item =>
                item.ApprovalType == AgentApprovalType.Plan &&
                string.Equals(item.TargetId, targetId, StringComparison.Ordinal))
            .ToArray();
        if (matching.Length > 1)
        {
            return ApprovalStateConflict(
                "Plan approval requires at most one exact pending approval request.");
        }

        var approval = matching.SingleOrDefault();
        var isNewApproval = approval is null;
        if (approval is null)
        {
            approval = new ApprovalRequest(
                task.Id,
                AgentApprovalType.Plan,
                targetId,
                userId,
                DateTimeOffset.UtcNow);
        }

        var decision = await DecideLoadedAsync(
            task,
            workspace: null,
            approval,
            userId,
            comment,
            isApproved: true,
            cancellationToken: cancellationToken,
            addApprovalOnSuccess: isNewApproval);
        return decision.IsSuccess
            ? Result.Success(task)
            : Result.From(decision);
    }

    private async Task<Result<AgentApprovalRequestDto>> DecideAsync(
        Guid approvalId,
        string? comment,
        bool isApproved,
        CancellationToken cancellationToken)
    {
        if (currentUser.Id is not { } userId)
        {
            return AgentApprovalAccess.MissingUser();
        }

        if (approvalId == Guid.Empty)
        {
            return Result.Invalid("Approval request id is required.");
        }

        var approval = await approvalRepository.FirstOrDefaultAsync(
            new ApprovalRequestByIdSpec(new ApprovalRequestId(approvalId)),
            cancellationToken);
        if (approval is null)
        {
            return Result.NotFound();
        }

        var taskResult = await LoadDecisionTaskAsync(
            approval,
            userId,
            taskRepository,
            currentUser,
            identityAccessService,
            cancellationToken);
        if (!taskResult.IsSuccess)
        {
            return Result.From(taskResult);
        }

        var task = taskResult.Value!;
        var workspace = await AgentApprovalAccess.LoadWorkspaceAsync(workspaceRepository, task, cancellationToken);
        return await DecideLoadedAsync(
            task,
            workspace,
            approval,
            userId,
            comment,
            isApproved,
            cancellationToken);
    }

    private async Task<Result<AgentApprovalRequestDto>> DecideLoadedAsync(
        AgentTask task,
        ArtifactWorkspace? workspace,
        ApprovalRequest approval,
        Guid userId,
        string? comment,
        bool isApproved,
        CancellationToken cancellationToken,
        bool addApprovalOnSuccess = false)
    {
        if (approval.Status != AgentApprovalStatus.Pending)
        {
            return Result.Invalid(new ApiProblemDescriptor(
                AppProblemCodes.ApprovalAlreadyProcessed,
                "Approval request has already been processed."));
        }

        IReadOnlyCollection<ApprovalRequest> lifecycleApprovals = Array.Empty<ApprovalRequest>();
        if (approval.ApprovalType is AgentApprovalType.Plan or AgentApprovalType.ToolCall or AgentApprovalType.FinalOutput)
        {
            var taskApprovals = await approvalRepository.ListAsync(
                new ApprovalRequestsByTaskSpec(task.Id),
                cancellationToken);
            lifecycleApprovals = taskApprovals;
            var pendingApprovals = taskApprovals
                .Where(item => item.Status == AgentApprovalStatus.Pending)
                .ToArray();
            var hasExactPendingSet = addApprovalOnSuccess
                ? pendingApprovals.Length == 0
                : pendingApprovals.Length == 1 && pendingApprovals[0].Id == approval.Id;
            if (!hasExactPendingSet)
            {
                return ApprovalStateConflict(
                    "Task lifecycle approval requires the current request to be the only pending approval.");
            }

            if (approval.ApprovalType == AgentApprovalType.FinalOutput)
            {
                var matchingFinalApprovals = taskApprovals.Where(item =>
                        item.ApprovalType == AgentApprovalType.FinalOutput &&
                        string.Equals(item.TargetId, approval.TargetId, StringComparison.Ordinal))
                    .ToArray();
                if (matchingFinalApprovals.Length != 1 ||
                    matchingFinalApprovals[0].Id != approval.Id)
                {
                    return ApprovalStateConflict(
                        "Final-output decision requires exactly one approval for the workspace checkpoint.");
                }
            }
        }

        AgentTaskRunAttempt? checkpointAttempt = null;
        if (approval.ApprovalType is AgentApprovalType.ToolCall or AgentApprovalType.FinalOutput)
        {
            var checkpointState = await LoadApprovalCheckpointStateAsync(
                task,
                workspace,
                approval,
                lifecycleApprovals,
                runAttemptStore,
                cancellationToken);
            if (!checkpointState.IsSuccess)
            {
                return Result.From(checkpointState);
            }

            checkpointAttempt = checkpointState.Value;
        }
        else
        {
            var approvalState = ValidateNonCheckpointApprovalState(task, workspace, approval);
            if (!approvalState.IsSuccess)
            {
                return Result.From(approvalState);
            }
        }

        if (isApproved && approval.ApprovalType == AgentApprovalType.ToolCall)
        {
            var staging = await runQueue.StageEnqueueAsync(
                task,
                AgentTaskRunTriggerType.ApprovalResume,
                userId,
                cancellationToken);
            if (!staging.IsSuccess)
            {
                return Result.From(staging);
            }
        }

        var now = DateTimeOffset.UtcNow;
        if (isApproved)
        {
            var approvalResult = await ApplyApprovalAsync(
                task,
                workspace,
                approval,
                now,
                planDraftConfirmationService,
                cancellationToken);
            if (!approvalResult.IsSuccess)
            {
                return Result.From(approvalResult);
            }

            approval.Approve(userId, comment, now);
        }
        else
        {
            approval.Reject(userId, comment, now);
            ApplyRejection(task, approval, comment, now);
            if (checkpointAttempt is not null)
            {
                checkpointAttempt.MarkFailed(
                    AppProblemCodes.AgentApprovalRejected,
                    "Agent run stopped because its approval request was rejected.",
                    now);
                runAttemptStore.Update(checkpointAttempt);
            }
        }

        if (addApprovalOnSuccess)
        {
            approvalRepository.Add(approval);
        }
        else
        {
            approvalRepository.Update(approval);
        }
        taskRepository.Update(task);
        if (workspace is not null)
        {
            workspaceRepository.Update(workspace);
        }

        await auditRecorder.RecordApprovalDecisionAsync(
            approval,
            task,
            isApproved ? AuditResults.Succeeded : AuditResults.Rejected,
            BuildDecisionSummary(approval, isApproved, comment),
            cancellationToken);
        if (timelineProjectionWriter is not null)
        {
            await timelineProjectionWriter.StageApprovalDecidedAsync(task, approval, cancellationToken);
        }

        // Approval/task/attempt/audit/timeline/resume queue share the scoped persistence context
        // and are committed by this single SaveChanges call.
        await approvalRepository.SaveChangesAsync(cancellationToken);

        return Result.Success(AgentApprovalDtoMapper.Map(approval, task, workspace));
    }

    private static async Task<Result<AgentTaskRunAttempt?>> LoadApprovalCheckpointStateAsync(
        AgentTask task,
        ArtifactWorkspace? workspace,
        ApprovalRequest approval,
        IReadOnlyCollection<ApprovalRequest> taskApprovals,
        IAgentTaskRunAttemptStore runAttemptStore,
        CancellationToken cancellationToken)
    {
        if (approval.ApprovalType == AgentApprovalType.FinalOutput)
        {
            var finalAttempts = await runAttemptStore.ListByTaskAsync(task.Id, cancellationToken);
            var finalState = AgentFinalizationCheckpointStateValidator.ValidatePaused(
                task,
                workspace,
                taskApprovals,
                finalAttempts);
            if (!finalState.IsSuccess ||
                finalState.Value!.Phase != AgentFinalizationCheckpointPhase.PendingApproval ||
                finalState.Value.Approval.Id != approval.Id)
            {
                return ApprovalStateConflict(
                    "Final-output approval requires the exact pending workspace checkpoint state.");
            }

            return Result.Success<AgentTaskRunAttempt?>(finalState.Value.ActiveAttempt);
        }

        AgentStep? waitingStep;
        if (approval.ApprovalType == AgentApprovalType.ToolCall)
        {
            waitingStep = AgentApprovalDtoMapper.FindStep(task, approval.TargetId);
            if (task.Status != AgentTaskStatus.WaitingToolApproval)
            {
                return ApprovalStateConflict(
                    "Tool-call approval requires the task to be waiting for tool approval.");
            }
        }
        else
        {
            return ApprovalStateConflict("Unsupported checkpoint approval type.");
        }

        if (waitingStep is null ||
            waitingStep.Status != AgentStepStatus.WaitingApproval ||
            task.ActiveRunAttemptId is null ||
            task.RunLeaseId is not null ||
            task.RunLeaseOwner is not null ||
            task.RunLeaseExpiresAt is not null)
        {
            return ApprovalStateConflict(
                "Approval requires a matching waiting step and lease-free active run attempt.");
        }

        var attempts = await runAttemptStore.ListByTaskAsync(task.Id, cancellationToken);
        var attempt = attempts.SingleOrDefault(item => item.Id == task.ActiveRunAttemptId.Value);
        if (attempt is null ||
            attempt.TaskId != task.Id ||
            attempt.Status != AgentTaskRunAttemptStatus.WaitingApproval ||
            attempt.LeaseId is not null ||
            attempt.LeaseOwner is not null ||
            attempt.LeaseExpiresAt is not null ||
            task.RunAttemptCount != attempt.AttemptNo ||
            attempts.Any(item =>
                item.LeaseId is not null ||
                item.LeaseOwner is not null ||
                item.LeaseExpiresAt is not null ||
                (item.Id != attempt.Id && !item.IsTerminal)))
        {
            return ApprovalStateConflict(
                "Approval run-attempt state is missing or inconsistent.");
        }

        return Result.Success<AgentTaskRunAttempt?>(attempt);
    }

    private static Result ValidateNonCheckpointApprovalState(
        AgentTask task,
        ArtifactWorkspace? workspace,
        ApprovalRequest approval)
    {
        if (approval.ApprovalType == AgentApprovalType.Plan)
        {
            return task.Status is AgentTaskStatus.Draft or AgentTaskStatus.WaitingPlanApproval &&
                   string.Equals(approval.TargetId, task.Id.Value.ToString(), StringComparison.Ordinal)
                ? Result.Success()
                : ApprovalStateConflict(
                    "Plan approval requires the exact task target and a confirmable plan state.");
        }

        if (approval.ApprovalType == AgentApprovalType.Artifact &&
            workspace is not null &&
            task.WorkspaceId == workspace.Id &&
            task.Status is AgentTaskStatus.WorkspaceReady or AgentTaskStatus.WaitingFinalApproval &&
            Guid.TryParse(approval.TargetId, out var artifactId) &&
            workspace.Artifacts.Count(item => item.Id == new ArtifactId(artifactId)) == 1)
        {
            var artifact = workspace.Artifacts.Single(item => item.Id == new ArtifactId(artifactId));
            if (artifact.Status is ArtifactStatus.Draft or ArtifactStatus.Reviewing)
            {
                return Result.Success();
            }
        }

        return ApprovalStateConflict(
            "Artifact approval requires the exact workspace artifact target and a reviewable artifact state.");
    }

    private static async Task<Result<AgentTask>> LoadDecisionTaskAsync(
        ApprovalRequest approval,
        Guid userId,
        IReadRepository<AgentTask> taskRepository,
        ICurrentUser currentUser,
        IIdentityAccessService identityAccessService,
        CancellationToken cancellationToken)
    {
        var accessResult = await AgentApprovalPermissions.LoadCurrentUserAccessAsync(
            currentUser,
            identityAccessService,
            cancellationToken);
        if (!accessResult.IsSuccess)
        {
            return Result.From(accessResult);
        }

        var requiredPermission = AgentApprovalPermissions.GetRequiredDecisionPermission(approval.ApprovalType);
        if (!AgentApprovalPermissions.HasPermission(accessResult.Value, requiredPermission))
        {
            return AgentApprovalPermissions.ForbiddenMissing(requiredPermission);
        }

        var task = await taskRepository.FirstOrDefaultAsync(
            new AgentTaskByIdSpec(approval.TaskId, includeSteps: true),
            cancellationToken);
        if (task is null)
        {
            return Result.NotFound();
        }

        if (task.UserId == userId)
        {
            return Result.Success(task);
        }

        return AgentApprovalPermissions.AllowsCrossUserDecision(approval.ApprovalType)
            ? Result.Success(task)
            : Result.NotFound();
    }

    private static async Task<Result> ApplyApprovalAsync(
        AgentTask task,
        ArtifactWorkspace? workspace,
        ApprovalRequest approval,
        DateTimeOffset now,
        AgentPlanDraftConfirmationService planDraftConfirmationService,
        CancellationToken cancellationToken)
    {
        switch (approval.ApprovalType)
        {
            case AgentApprovalType.Plan:
                var confirmation = await planDraftConfirmationService.ConfirmAsync(task, now, cancellationToken);
                if (!confirmation.IsSuccess)
                {
                    return Result.From(confirmation);
                }

                task.ApprovePlan(now);
                break;
            case AgentApprovalType.ToolCall:
                var step = AgentApprovalDtoMapper.FindStep(task, approval.TargetId)!;
                step.Approve();
                task.Start(now);

                break;
            case AgentApprovalType.Artifact:
                var artifactId = Guid.Parse(approval.TargetId);
                var artifact = workspace!.Artifacts.Single(item => item.Id == new ArtifactId(artifactId));
                artifact.Approve(now);

                break;
            case AgentApprovalType.FinalOutput:
                var finalStep = task.Steps
                    .OrderByDescending(step => step.StepIndex)
                    .First(step => string.Equals(step.ToolCode, "finalize_artifacts", StringComparison.Ordinal));
                finalStep.Approve();

                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(approval.ApprovalType), approval.ApprovalType, "Unknown approval type.");
        }

        return Result.Success();
    }

    private static void ApplyRejection(
        AgentTask task,
        ApprovalRequest approval,
        string? comment,
        DateTimeOffset now)
    {
        var reason = string.IsNullOrWhiteSpace(comment)
            ? $"Approval request {approval.Id.Value} was rejected."
            : comment.Trim();
        if (approval.ApprovalType == AgentApprovalType.ToolCall)
        {
            var step = AgentApprovalDtoMapper.FindStep(task, approval.TargetId);
            if (step is not null && step.Status != AgentStepStatus.Completed)
            {
                step.Fail(reason, now);
            }
        }
        else if (approval.ApprovalType == AgentApprovalType.FinalOutput)
        {
            var finalStep = task.Steps
                .OrderByDescending(step => step.StepIndex)
                .FirstOrDefault(step => string.Equals(
                    step.ToolCode,
                    "finalize_artifacts",
                    StringComparison.Ordinal));
            finalStep?.Fail(reason, now);
        }

        task.Reject(reason, now);
    }

    private static Result ApprovalStateConflict(string detail) =>
        Result.Invalid(new ApiProblemDescriptor(
            AppProblemCodes.AgentApprovalStateConflict,
            detail));

    private static string BuildDecisionSummary(
        ApprovalRequest approval,
        bool isApproved,
        string? comment)
    {
        var action = isApproved ? "approved" : "rejected";
        return string.IsNullOrWhiteSpace(comment)
            ? $"Agent {approval.ApprovalType} approval {action}."
            : $"Agent {approval.ApprovalType} approval {action}: {comment.Trim()}";
    }
}
