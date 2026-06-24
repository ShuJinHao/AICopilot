using AICopilot.Core.AiGateway.Aggregates.AgentTasks;
using AICopilot.Core.AiGateway.Aggregates.Approvals;
using AICopilot.Core.AiGateway.Aggregates.Artifacts;
using AICopilot.AiGatewayService.Sessions;
using AICopilot.Core.AiGateway.Specifications.AgentTasks;
using AICopilot.Core.AiGateway.Specifications.Approvals;
using AICopilot.Core.AiGateway.Specifications.Artifacts;
using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Messaging;
using AICopilot.SharedKernel.Repository;
using AICopilot.SharedKernel.Result;
using Microsoft.Extensions.Options;

namespace AICopilot.AiGatewayService.AgentTasks;

public sealed class ApproveAgentTaskPlanCommandHandler(
    IRepository<AgentTask> repository,
    IRepository<ApprovalRequest> approvalRepository,
    IReadRepository<ArtifactWorkspace> workspaceRepository,
    AgentAuditRecorder auditRecorder,
    ICurrentUser currentUser,
    AgentPlanDraftConfirmationService planDraftConfirmationService,
    MessageTimelineProjectionWriter? timelineProjectionWriter = null)
    : ICommandHandler<ApproveAgentTaskPlanCommand, Result<AgentTaskDto>>
{
    public async Task<Result<AgentTaskDto>> Handle(ApproveAgentTaskPlanCommand request, CancellationToken cancellationToken)
    {
        var taskResult = await AgentTaskCommandLoader.LoadTaskAsync(repository, currentUser, request.Id, cancellationToken);
        if (!taskResult.IsSuccess)
        {
            return Result.From(taskResult);
        }

        var task = taskResult.Value!;
        var userId = currentUser.Id!.Value;
        var now = DateTimeOffset.UtcNow;
        var approval = await approvalRepository.FirstOrDefaultAsync(
            new PendingApprovalRequestByTaskAndTargetSpec(
                task.Id,
                AgentApprovalType.Plan,
                task.Id.Value.ToString()),
            cancellationToken);
        if (approval is null && task.Status is AgentTaskStatus.Draft or AgentTaskStatus.WaitingPlanApproval)
        {
            approval = new ApprovalRequest(
                task.Id,
                AgentApprovalType.Plan,
                task.Id.Value.ToString(),
                task.UserId,
                now);
            approvalRepository.Add(approval);
        }

        if (task.Status is AgentTaskStatus.Draft or AgentTaskStatus.WaitingPlanApproval)
        {
            var confirmation = await planDraftConfirmationService.ConfirmAsync(task, now, cancellationToken);
            if (!confirmation.IsSuccess)
            {
                return Result.From(confirmation);
            }

            task.ApprovePlan(now);
        }

        if (approval is not null)
        {
            approval.Approve(userId, "Plan approved.", now);
            approvalRepository.Update(approval);
            if (timelineProjectionWriter is not null)
            {
                await timelineProjectionWriter.StageApprovalDecidedAsync(task, approval, cancellationToken);
            }
        }

        repository.Update(task);
        if (approval is not null)
        {
            await auditRecorder.RecordApprovalDecisionAsync(
                approval,
                task,
                AuditResults.Succeeded,
                "Agent task plan approved.",
                cancellationToken);
        }

        await repository.SaveChangesAsync(cancellationToken);
        return Result.Success(await AgentTaskDtoComposer.MapAsync(task, workspaceRepository, approvalRepository, cancellationToken));
    }
}

public sealed class RunAgentTaskCommandHandler(
    IRepository<AgentTask> repository,
    IReadRepository<ArtifactWorkspace> workspaceRepository,
    IReadRepository<ApprovalRequest> approvalRepository,
    IReadRepository<AgentTaskRunQueueItem> queueRepository,
    IAgentTaskRunQueue runQueue,
    ICurrentUser currentUser)
    : ICommandHandler<RunAgentTaskCommand, Result<AgentTaskDto>>
{
    public async Task<Result<AgentTaskDto>> Handle(RunAgentTaskCommand request, CancellationToken cancellationToken)
    {
        var taskResult = await AgentTaskCommandLoader.LoadTaskAsync(repository, currentUser, request.Id, cancellationToken);
        if (!taskResult.IsSuccess)
        {
            return Result.From(taskResult);
        }

        var task = taskResult.Value!;
        if (task.Status is not AgentTaskStatus.PlanApproved and not AgentTaskStatus.WaitingToolApproval)
        {
            return Result.Invalid("Only approved or waiting-approval agent tasks can be queued for execution.");
        }

        var queued = await runQueue.EnqueueAsync(
            task,
            AgentTaskRunTriggerType.Manual,
            currentUser.Id!.Value,
            cancellationToken);
        return queued.IsSuccess
            ? Result.Success(await AgentTaskDtoComposer.MapAsync(
                task,
                workspaceRepository,
                approvalRepository,
                queueRepository,
                cancellationToken))
            : Result.From(queued);
    }
}

public sealed class RetryAgentTaskCommandHandler(
    IRepository<AgentTask> repository,
    IReadRepository<ArtifactWorkspace> workspaceRepository,
    IRepository<ApprovalRequest> approvalRepository,
    IReadRepository<AgentTaskRunQueueItem> queueReadRepository,
    IAgentTaskRunQueue runQueue,
    ICurrentUser currentUser,
    IOptions<AgentRunQueueOptions>? options = null,
    AgentAuditRecorder? auditRecorder = null)
    : ICommandHandler<RetryAgentTaskCommand, Result<AgentTaskDto>>
{
    public async Task<Result<AgentTaskDto>> Handle(RetryAgentTaskCommand request, CancellationToken cancellationToken)
    {
        var taskResult = await AgentTaskCommandLoader.LoadTaskAsync(repository, currentUser, request.Id, cancellationToken);
        if (!taskResult.IsSuccess)
        {
            return Result.From(taskResult);
        }

        var task = taskResult.Value!;
        var activeQueue = await queueReadRepository.FirstOrDefaultAsync(
            new ActiveAgentTaskRunQueueItemByTaskSpec(task.Id),
            cancellationToken);
        if (activeQueue is not null)
        {
            return Result.Failure(new ApiProblemDescriptor(
                AppProblemCodes.AgentTaskRunInProgress,
                "Agent task already has an active queued or leased run."));
        }

        if (task.Status != AgentTaskStatus.Failed)
        {
            return Result.Failure(new ApiProblemDescriptor(
                AppProblemCodes.AgentTaskRetryNotAllowed,
                "Only failed agent tasks can be retried. Completed, finalized, rejected, and cancelled tasks require a new task."));
        }

        var queueItems = await queueReadRepository.ListAsync(
            new AgentTaskRunQueueItemsByTaskSpec(task.Id),
            cancellationToken);
        var previousRetryCount = queueItems.Count(item => item.TriggerType == AgentTaskRunTriggerType.Retry);
        var runQueueOptions = options?.Value ?? new AgentRunQueueOptions();
        if (previousRetryCount >= runQueueOptions.EffectiveMaxRetryAttempts)
        {
            return Result.Failure(new ApiProblemDescriptor(
                AppProblemCodes.AgentTaskRetryNotAllowed,
                $"Agent task retry limit exceeded. Maximum retry attempts: {runQueueOptions.EffectiveMaxRetryAttempts}."));
        }

        if (task.WorkspaceId is not null)
        {
            var workspace = await workspaceRepository.FirstOrDefaultAsync(
                new ArtifactWorkspaceByIdSpec(task.WorkspaceId.Value, includeArtifacts: false),
                cancellationToken);
            if (workspace?.Status == ArtifactWorkspaceStatus.Finalized)
            {
                return Result.Failure(new ApiProblemDescriptor(
                    AppProblemCodes.AgentTaskRetryNotAllowed,
                    "Finalized workspaces cannot be retried. Create a new agent task instead."));
            }
        }

        var now = DateTimeOffset.UtcNow;
        var retryAttemptNo = previousRetryCount + 1;
        var availableAt = now.Add(runQueueOptions.GetRetryBackoff(retryAttemptNo));
        await CancelPendingApprovalsAsync(task, approvalRepository, now, cancellationToken);
        task.PrepareRetry(now);
        repository.Update(task);
        await repository.SaveChangesAsync(cancellationToken);

        var queued = await runQueue.EnqueueAsync(
            task,
            AgentTaskRunTriggerType.Retry,
            currentUser.Id!.Value,
            cancellationToken,
            availableAt);
        if (!queued.IsSuccess)
        {
            return Result.From(queued);
        }

        if (auditRecorder is not null)
        {
            await auditRecorder.RecordRunQueueOperationAsync(
                "Agent.RunQueueRetry",
                queued.Value!,
                AuditResults.Succeeded,
                "Agent task retry queued with backoff.",
                AgentTaskStatus.Failed.ToString(),
                attempt: null,
                retryAttemptNo,
                cancellationToken);
        }

        return Result.Success(await AgentTaskDtoComposer.MapAsync(
            task,
            workspaceRepository,
            approvalRepository,
            queueReadRepository,
            cancellationToken));
    }

    private static async Task CancelPendingApprovalsAsync(
        AgentTask task,
        IRepository<ApprovalRequest> approvalRepository,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var approvals = await approvalRepository.ListAsync(
            new ApprovalRequestsByTaskSpec(task.Id, pendingOnly: true),
            cancellationToken);
        foreach (var approval in approvals)
        {
            approval.Cancel(now);
            approvalRepository.Update(approval);
        }
    }
}

public sealed class CancelAgentTaskCommandHandler(
    IRepository<AgentTask> repository,
    IReadRepository<ArtifactWorkspace> workspaceRepository,
    IRepository<ApprovalRequest> approvalRepository,
    IRepository<AgentTaskRunAttempt> runAttemptRepository,
    IReadRepository<AgentTaskRunQueueItem> queueReadRepository,
    IAgentTaskRunQueue runQueue,
    ICurrentUser currentUser,
    AgentAuditRecorder? auditRecorder = null)
    : ICommandHandler<CancelAgentTaskCommand, Result<AgentTaskDto>>
{
    public async Task<Result<AgentTaskDto>> Handle(CancelAgentTaskCommand request, CancellationToken cancellationToken)
    {
        var taskResult = await AgentTaskCommandLoader.LoadTaskAsync(repository, currentUser, request.Id, cancellationToken);
        if (!taskResult.IsSuccess)
        {
            return Result.From(taskResult);
        }

        var task = taskResult.Value!;
        if (IsTerminal(task.Status))
        {
            return Result.Success(await AgentTaskDtoComposer.MapAsync(
                task,
                workspaceRepository,
                approvalRepository,
                queueReadRepository,
                cancellationToken));
        }

        var now = DateTimeOffset.UtcNow;
        var activeBeforeCancel = await queueReadRepository.ListAsync(
            new ActiveAgentTaskRunQueueItemByTaskSpec(task.Id),
            cancellationToken);
        var oldStatuses = activeBeforeCancel.ToDictionary(
            item => item.Id,
            item => item.Status.ToString());
        var cancelledItems = await runQueue.CancelActiveAsync(
            task,
            now,
            "Agent task cancellation requested.",
            cancellationToken);
        var approvals = await approvalRepository.ListAsync(
            new ApprovalRequestsByTaskSpec(task.Id, pendingOnly: true),
            cancellationToken);
        foreach (var approval in approvals)
        {
            approval.Cancel(now);
            approvalRepository.Update(approval);
        }

        if (task.ActiveRunAttemptId is not null)
        {
            var attempt = await runAttemptRepository.FirstOrDefaultAsync(
                new AgentTaskRunAttemptByIdSpec(task.ActiveRunAttemptId.Value),
                cancellationToken);
            if (attempt is not null && !attempt.IsTerminal)
            {
                attempt.Cancel(now, "Agent task cancellation requested.");
                runAttemptRepository.Update(attempt);
            }
        }

        task.Cancel(now);
        repository.Update(task);
        await repository.SaveChangesAsync(cancellationToken);
        if (auditRecorder is not null)
        {
            foreach (var item in cancelledItems)
            {
                await auditRecorder.RecordRunQueueOperationAsync(
                    "Agent.RunQueueCancel",
                    item,
                    AuditResults.Succeeded,
                    "Agent task run queue item cancelled.",
                    oldStatuses.GetValueOrDefault(item.Id, AgentTaskRunQueueStatus.Queued.ToString()),
                    attempt: null,
                    retryAttemptNo: null,
                    cancellationToken);
            }
        }

        return Result.Success(await AgentTaskDtoComposer.MapAsync(
            task,
            workspaceRepository,
            approvalRepository,
            queueReadRepository,
            cancellationToken));
    }

    private static bool IsTerminal(AgentTaskStatus status)
    {
        return status is AgentTaskStatus.Completed
            or AgentTaskStatus.Finalized
            or AgentTaskStatus.Failed
            or AgentTaskStatus.Rejected
            or AgentTaskStatus.Cancelled;
    }
}
