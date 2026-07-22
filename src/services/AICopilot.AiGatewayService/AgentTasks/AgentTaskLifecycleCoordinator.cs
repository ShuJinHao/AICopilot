using AICopilot.Core.AiGateway.Aggregates.AgentTasks;
using AICopilot.Core.AiGateway.Aggregates.Approvals;
using AICopilot.Core.AiGateway.Aggregates.Artifacts;
using AICopilot.Core.AiGateway.Specifications.Approvals;
using AICopilot.Core.AiGateway.Specifications.Artifacts;
using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Repository;
using AICopilot.SharedKernel.Result;
using Microsoft.Extensions.Options;

namespace AICopilot.AiGatewayService.AgentTasks;

public sealed class AgentTaskLifecycleCoordinator(
    IRepository<AgentTask> taskRepository,
    IRepository<ApprovalRequest> approvalRepository,
    IReadRepository<ArtifactWorkspace> workspaceRepository,
    IAgentTaskRunQueueStore queueStore,
    IAgentTaskCancellationStore cancellationStore,
    IAgentTaskRunQueue runQueue,
    AgentTaskPlanFreshReadGate freshReadGate,
    IOptions<AgentRunQueueOptions>? options = null,
    AgentAuditRecorder? auditRecorder = null)
{
    public async Task<Result<AgentTaskRunQueueItem>> QueueRunAsync(
        AgentTask task,
        Guid requestedBy,
        CancellationToken cancellationToken)
    {
        var integrity = await freshReadGate.VerifyAsync(
            task,
            requireExecutable: true,
            cancellationToken);
        if (!integrity.IsSuccess)
        {
            return Result.From(integrity);
        }

        if (task.Status is not AgentTaskStatus.PlanApproved and not AgentTaskStatus.WaitingToolApproval)
        {
            return Result.Invalid("Only approved or waiting-approval agent tasks can be queued for execution.");
        }

        return await runQueue.EnqueueAsync(
            task,
            AgentTaskRunTriggerType.Manual,
            requestedBy,
            cancellationToken);
    }

    public async Task<Result<AgentTaskRunQueueItem>> RetryAsync(
        AgentTask task,
        Guid requestedBy,
        CancellationToken cancellationToken)
    {
        var integrity = await freshReadGate.VerifyAsync(
            task,
            requireExecutable: true,
            cancellationToken);
        if (!integrity.IsSuccess)
        {
            return Result.From(integrity);
        }

        var activeQueue = await queueStore.FirstActiveByTaskAsync(task.Id, cancellationToken);
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

        var queueItems = await queueStore.ListByTaskAsync(task.Id, cancellationToken);
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
        await CancelPendingApprovalsAsync(task, now, cancellationToken);
        task.PrepareRetry(now);
        taskRepository.Update(task);
        await taskRepository.SaveChangesAsync(cancellationToken);

        var queued = await runQueue.EnqueueAsync(
            task,
            AgentTaskRunTriggerType.Retry,
            requestedBy,
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

        return queued;
    }

    public async Task<Result<IReadOnlyCollection<AgentTaskRunQueueItem>>> CancelAsync(
        AgentTask task,
        CancellationToken cancellationToken)
    {
        if (IsTerminal(task.Status))
        {
            return Result.Success<IReadOnlyCollection<AgentTaskRunQueueItem>>(Array.Empty<AgentTaskRunQueueItem>());
        }

        var now = DateTimeOffset.UtcNow;
        var checkpoint = await cancellationStore.RequestAsync(
            task.Id,
            now,
            "Agent task cancellation requested.",
            cancellationToken);
        if (checkpoint.Disposition == AgentTaskCancellationDisposition.StateConflict)
        {
            return Result.Failure(new ApiProblemDescriptor(
                AppProblemCodes.AgentNodeRunStateConflict,
                checkpoint.SafeMessage));
        }

        var affectedItems = checkpoint.QueueItems.Select(snapshot => snapshot.Item).ToArray();
        if (auditRecorder is not null)
        {
            foreach (var snapshot in checkpoint.QueueItems)
            {
                await auditRecorder.RecordRunQueueOperationAsync(
                    checkpoint.Disposition == AgentTaskCancellationDisposition.ReconciliationRequired
                        ? "Agent.RunQueueCancelReconciliationRequired"
                        : "Agent.RunQueueCancel",
                    snapshot.Item,
                    AuditResults.Succeeded,
                    checkpoint.SafeMessage,
                    snapshot.PreviousStatus.ToString(),
                    attempt: null,
                    retryAttemptNo: null,
                    cancellationToken);
            }

            await taskRepository.SaveChangesAsync(cancellationToken);
        }

        return Result.Success<IReadOnlyCollection<AgentTaskRunQueueItem>>(affectedItems);
    }

    private async Task CancelPendingApprovalsAsync(
        AgentTask task,
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

    private static bool IsTerminal(AgentTaskStatus status)
    {
        return status is AgentTaskStatus.Completed
            or AgentTaskStatus.Finalized
            or AgentTaskStatus.Failed
            or AgentTaskStatus.Rejected
            or AgentTaskStatus.Cancelled;
    }
}
