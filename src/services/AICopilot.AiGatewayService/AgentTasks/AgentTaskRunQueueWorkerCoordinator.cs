using AICopilot.AiGatewayService.Tools;
using AICopilot.Core.AiGateway.Aggregates.AgentTasks;
using AICopilot.Core.AiGateway.Specifications.AgentTasks;
using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Repository;
using AICopilot.SharedKernel.Result;
using Microsoft.Extensions.Options;

namespace AICopilot.AiGatewayService.AgentTasks;

internal sealed class AgentTaskRunQueueWorkerCoordinator(
    IAgentTaskRunQueueStore queueStore,
    IRepository<AgentTask> taskRepository,
    IAgentTaskRunAttemptStore attemptStore,
    IAgentTaskRunQueue runQueue,
    IAgentTaskRuntime runtime,
    IOptions<AgentRunQueueOptions>? options = null,
    AgentAuditRecorder? auditRecorder = null,
    DurableTaskClaimCoordinator? durableTaskClaimCoordinator = null)
{
    private AgentRunQueueOptions QueueOptions => options?.Value ?? new AgentRunQueueOptions();

    public Task<Result<AgentTaskRunQueueItem?>> LeaseNextAsync(
        string leaseOwner,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken)
    {
        return runQueue.LeaseNextAsync(leaseOwner, leaseDuration, cancellationToken);
    }

    public Task<Result<DurableTaskClaim?>> ClaimNextAsync(
        string leaseOwner,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken)
    {
        return durableTaskClaimCoordinator is null
            ? Task.FromResult<Result<DurableTaskClaim?>>(Result.Failure(new ApiProblemDescriptor(
                AppProblemCodes.AgentTaskRunFenceStale,
                "Durable task claim coordinator is unavailable.")))
            : durableTaskClaimCoordinator.ClaimNextAsync(
                leaseOwner,
                leaseDuration,
                cancellationToken);
    }

    public async Task ExecuteClaimAsync(
        DurableTaskClaim claim,
        CancellationToken cancellationToken)
    {
        if (durableTaskClaimCoordinator is null)
        {
            throw new InvalidOperationException("Durable task claim coordinator is unavailable.");
        }

        var startedAtUtc = DateTimeOffset.UtcNow;
        var started = await durableTaskClaimCoordinator.MarkStartedAsync(
            claim,
            startedAtUtc,
            cancellationToken);
        if (!started.IsSuccess)
        {
            return;
        }

        AgentRuntimeTelemetry.RecordQueueWait(startedAtUtc - claim.QueueItem.CreatedAt);

        var result = await runtime.RunClaimedAsync(claim, cancellationToken);
        var now = DateTimeOffset.UtcNow;
        if (claim.Task.Status == AgentTaskStatus.ReconciliationRequired)
        {
            return;
        }

        var terminalStatus = AgentTaskRunQueueStatus.Succeeded;
        string? failureCode = null;
        string safeMessage;
        if (!result.IsSuccess)
        {
            var errors = result.Errors?.ToArray();
            var publicPlanFailure = AgentPlanPublicFailureDisclosurePolicy.ResolveResultErrors(errors);
            var problem = errors?.OfType<ApiProblemDescriptor>().FirstOrDefault();
            terminalStatus = AgentTaskRunQueueStatus.Failed;
            failureCode = publicPlanFailure?.Disclosure.Code
                ?? problem?.Code
                ?? "agent_task_run_failed";
            safeMessage = publicPlanFailure?.Disclosure.Detail
                ?? problem?.Detail
                ?? "Agent task run failed before runtime execution completed.";
        }
        else if (claim.Task.Status == AgentTaskStatus.Cancelled)
        {
            terminalStatus = AgentTaskRunQueueStatus.Cancelled;
            failureCode = AppProblemCodes.AgentTaskCancellationRequested;
            safeMessage = "Agent task cancellation requested.";
        }
        else if (claim.Task.Status == AgentTaskStatus.Failed)
        {
            terminalStatus = AgentTaskRunQueueStatus.Failed;
            failureCode = claim.RunAttempt.FailureCode ?? "agent_task_failed";
            safeMessage = claim.RunAttempt.SafeMessage
                ?? claim.Task.FinalSummary
                ?? "Agent task failed.";
        }
        else
        {
            safeMessage = $"Agent task run reached {claim.Task.Status}.";
        }

        await durableTaskClaimCoordinator.CompleteAsync(
            claim,
            terminalStatus,
            failureCode,
            safeMessage,
            now,
            cancellationToken);
    }

    public async Task FailClaimAsync(
        DurableTaskClaim claim,
        string failureCode,
        string safeMessage,
        CancellationToken cancellationToken)
    {
        if (durableTaskClaimCoordinator is null)
        {
            return;
        }

        var sanitized = ToolExecutionRecordSanitizer.Sanitize(safeMessage, 2000)
            ?? "Agent task run worker failed.";
        await durableTaskClaimCoordinator.CompleteAsync(
            claim,
            AgentTaskRunQueueStatus.Failed,
            failureCode,
            sanitized,
            DateTimeOffset.UtcNow,
            cancellationToken);
    }

    public async Task RecoverExpiredStartedLeasesAsync(CancellationToken cancellationToken)
    {
        if (QueueOptions.StaleLeaseAction == AgentRunQueueStaleLeaseAction.Recover)
        {
            if (durableTaskClaimCoordinator is not null)
            {
                await durableTaskClaimCoordinator.RecoverExpiredStartedAsync(
                    DateTimeOffset.UtcNow,
                    cancellationToken);
            }

            return;
        }

        if (QueueOptions.StaleLeaseAction != AgentRunQueueStaleLeaseAction.Fail)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var activeItems = await queueStore.ListActiveAsync(cancellationToken);
        foreach (var item in activeItems.Where(candidate => candidate.IsExpiredStartedLease(now)))
        {
            var oldStatus = item.Status.ToString();
            var message = "Agent task run queue lease expired during execution. Retry the task before continuing.";
            item.MarkFailed(AppProblemCodes.AgentTaskRunQueueLeaseExpired, message, now);
            queueStore.Update(item);

            var task = await taskRepository.FirstOrDefaultAsync(
                new AgentTaskByIdSpec(item.TaskId, includeSteps: true),
                cancellationToken);
            if (task is not null)
            {
                task.Fail(message, now);
                task.ReleaseRunLease(now, clearActiveAttempt: true);
                taskRepository.Update(task);
            }

            var attempt = await ResolveAttemptAsync(item, task, cancellationToken);
            if (attempt is not null && !attempt.IsTerminal)
            {
                attempt.MarkFailed(AppProblemCodes.AgentTaskRunLeaseExpired, message, now);
                attemptStore.Update(attempt);
            }

            if (auditRecorder is not null)
            {
                await auditRecorder.RecordRunQueueOperationAsync(
                    "Agent.RunQueueStaleLeaseFailed",
                    item,
                    AuditResults.Succeeded,
                    message,
                    oldStatus,
                    attempt,
                    retryAttemptNo: null,
                    cancellationToken);
            }
        }

        await queueStore.SaveChangesAsync(cancellationToken);
    }

    public async Task ExecuteQueueItemAsync(
        AgentTaskRunQueueItem item,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var task = await taskRepository.FirstOrDefaultAsync(
            new AgentTaskByIdSpec(item.TaskId, includeSteps: true),
            cancellationToken);
        if (task is null)
        {
            item.MarkFailed(
                AppProblemCodes.AgentTaskRunQueueNotFound,
                "Agent task was not found for queued run.",
                now);
            queueStore.Update(item);
            await queueStore.SaveChangesAsync(cancellationToken);
            return;
        }

        item.MarkStarted(task.ActiveRunAttemptId, now);
        queueStore.Update(item);
        await queueStore.SaveChangesAsync(cancellationToken);

        var result = await runtime.RunAsync(task, item.TriggerType, cancellationToken);
        now = DateTimeOffset.UtcNow;

        var latestAttempt = await ResolveAttemptAsync(item, task, cancellationToken);
        if (latestAttempt is not null)
        {
            item.LinkRunAttempt(latestAttempt.Id, now);
        }

        if (!result.IsSuccess)
        {
            var errors = result.Errors?.ToArray();
            var publicPlanFailure = AgentPlanPublicFailureDisclosurePolicy.ResolveResultErrors(errors);
            var problem = errors?.OfType<ApiProblemDescriptor>().FirstOrDefault();
            var failureCode = publicPlanFailure?.Disclosure.Code
                ?? problem?.Code
                ?? "agent_task_run_failed";
            item.MarkFailed(
                failureCode,
                publicPlanFailure?.Disclosure.Detail
                    ?? problem?.Detail
                    ?? "Agent task run failed before runtime execution completed.",
                now);
        }
        else if (task.Status == AgentTaskStatus.Cancelled)
        {
            item.Cancel(now, "Agent task cancellation requested.");
        }
        else if (task.Status == AgentTaskStatus.Failed)
        {
            item.MarkFailed(
                latestAttempt?.FailureCode ?? "agent_task_failed",
                latestAttempt?.SafeMessage ?? task.FinalSummary ?? "Agent task failed.",
                now);
        }
        else
        {
            item.MarkSucceeded(now, $"Agent task run reached {task.Status}.");
        }

        queueStore.Update(item);
        await queueStore.SaveChangesAsync(cancellationToken);
    }

    public async Task FailQueueItemAsync(
        AgentTaskRunQueueItem item,
        string failureCode,
        string safeMessage,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var sanitized = ToolExecutionRecordSanitizer.Sanitize(safeMessage, 2000) ?? "Agent task run worker failed.";
        item.MarkFailed(failureCode, sanitized, now);
        queueStore.Update(item);

        var task = await taskRepository.FirstOrDefaultAsync(
            new AgentTaskByIdSpec(item.TaskId, includeSteps: true),
            cancellationToken);
        if (task is not null)
        {
            task.Fail(sanitized, now);
            task.ReleaseRunLease(now, clearActiveAttempt: true);
            taskRepository.Update(task);
        }

        var attempt = await ResolveAttemptAsync(item, task, cancellationToken);
        if (attempt is not null && !attempt.IsTerminal)
        {
            attempt.MarkFailed(failureCode, sanitized, now);
            attemptStore.Update(attempt);
        }

        await queueStore.SaveChangesAsync(cancellationToken);
    }

    private async Task<AgentTaskRunAttempt?> ResolveAttemptAsync(
        AgentTaskRunQueueItem item,
        AgentTask? task,
        CancellationToken cancellationToken)
    {
        if (item.RunAttemptId is not null)
        {
            var attempt = await attemptStore.FirstByIdAsync(item.RunAttemptId.Value, cancellationToken);
            if (attempt is not null)
            {
                return attempt;
            }
        }

        if (task?.ActiveRunAttemptId is not null)
        {
            var attempt = await attemptStore.FirstByIdAsync(task.ActiveRunAttemptId.Value, cancellationToken);
            if (attempt is not null)
            {
                return attempt;
            }
        }

        if (task is null)
        {
            return null;
        }

        var attempts = await attemptStore.ListByTaskAsync(task.Id, cancellationToken);
        return attempts
            .OrderByDescending(attempt => attempt.AttemptNo)
            .ThenByDescending(attempt => attempt.StartedAt)
            .FirstOrDefault();
    }
}
