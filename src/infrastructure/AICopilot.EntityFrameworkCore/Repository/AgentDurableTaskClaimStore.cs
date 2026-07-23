using AICopilot.Core.AiGateway.Aggregates.AgentTasks;
using AICopilot.Core.AiGateway.Ids;
using AICopilot.Core.AiGateway.Runtime.AgentExecution;
using AICopilot.EntityFrameworkCore.Transactions;
using AICopilot.Services.Contracts;
using Microsoft.EntityFrameworkCore;

namespace AICopilot.EntityFrameworkCore.Repository;

internal sealed class AgentDurableTaskClaimStore(
    AgentExecutionTransactionRunner transactionRunner)
    : IAgentDurableTaskClaimStore
{
    public Task<DurableTaskClaim?> TryClaimNextAsync(
        string leaseOwner,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(leaseOwner);
        if (leaseDuration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(leaseDuration));
        }

        return transactionRunner.ExecuteAsync(
            "Agent.DurableTaskClaim",
            async (context, token) =>
            {
                var now = DateTimeOffset.UtcNow;
                var queueItem = await context.AgentTaskRunQueueItems
                    .FromSqlInterpolated($$"""
                        SELECT q.*, q.xmin
                        FROM aigateway.agent_task_run_queue_items AS q
                        INNER JOIN aigateway.agent_tasks AS t ON t.id = q.task_id
                        WHERE q.status IN ('Queued', 'Claimed')
                          AND q.available_at <= {{now}}
                          AND (q.status = 'Queued' OR q.lease_expires_at IS NULL OR q.lease_expires_at <= {{now}})
                          AND t.status IN ('Queued', 'WaitingToolApproval', 'WaitingFinalApproval', 'Running', 'GeneratingArtifacts')
                        ORDER BY q.available_at, q.created_at, q.id
                        FOR UPDATE OF q SKIP LOCKED
                        LIMIT 1
                        """)
                    .SingleOrDefaultAsync(token);
                if (queueItem is null)
                {
                    return Attempt<DurableTaskClaim?>(null);
                }

                var task = await AgentExecutionRowLock.ByIdAsync<AgentTask>(
                    context, queueItem.TaskId.Value, token);
                if (task is null || task.IsRunInProgress(now))
                {
                    return Attempt<DurableTaskClaim?>(null);
                }

                await context.Entry(task)
                    .Collection(candidate => candidate.Steps)
                    .LoadAsync(token);

                AgentTaskRunAttempt? attempt = null;
                if (task.ActiveRunAttemptId is not null)
                {
                    attempt = await AgentExecutionRowLock.ByIdAsync<AgentTaskRunAttempt>(
                        context, task.ActiveRunAttemptId.Value.Value, token);
                    if (attempt is not null && attempt.IsTerminal)
                    {
                        attempt = null;
                    }
                    else if (attempt?.HasActiveLease(now) == true)
                    {
                        return Attempt<DurableTaskClaim?>(null);
                    }
                }

                if (attempt is null)
                {
                    attempt = new AgentTaskRunAttempt(
                        task.Id,
                        task.RunAttemptCount + 1,
                        queueItem.TriggerType,
                        leaseOwner,
                        now,
                        leaseDuration);
                    context.AgentTaskRunAttempts.Add(attempt);
                    task.BeginRunAttempt(
                        attempt.Id,
                        attempt.AttemptNo,
                        attempt.LeaseId!.Value,
                        leaseOwner,
                        attempt.LeaseExpiresAt!.Value,
                        now);
                }
                else
                {
                    attempt.AcquireLease(Guid.NewGuid(), leaseOwner, now, leaseDuration);
                    task.AdvanceRunFencingToken(now);
                    task.AcquireRunLease(
                        attempt.LeaseId!.Value,
                        leaseOwner,
                        attempt.LeaseExpiresAt!.Value,
                        now);
                }

                attempt.BindTaskFencingToken(task.RunFencingToken);
                queueItem.AcquireLease(
                    attempt.LeaseId!.Value,
                    leaseOwner,
                    now,
                    leaseDuration,
                    task.RunFencingToken);
                queueItem.LinkRunAttempt(attempt.Id, now);

                var claim = new DurableTaskClaim(
                    queueItem,
                    task,
                    attempt,
                    task.RunFencingToken,
                    attempt.LeaseId.Value,
                    attempt.LeaseExpiresAt!.Value);
                return Attempt<DurableTaskClaim?>(claim);
            },
            cancellationToken);
    }

    public Task<AgentFencedWriteResult> TryMarkStartedAsync(
        DurableTaskClaim claim,
        DateTimeOffset startedAtUtc,
        CancellationToken cancellationToken = default)
    {
        return transactionRunner.ExecuteAsync(
            "Agent.DurableTaskStart",
            async (context, token) =>
            {
                var queueItem = await AgentExecutionRowLock.ByIdAsync<AgentTaskRunQueueItem>(
                    context, claim.QueueItem.Id.Value, token);
                if (queueItem is null || queueItem.RunAttemptId != claim.RunAttempt.Id ||
                    queueItem.TaskFencingToken != claim.TaskFencingToken || queueItem.LeaseId != claim.LeaseId ||
                    queueItem.Status != AgentTaskRunQueueStatus.Claimed)
                {
                    return Attempt(AgentFencedWriteResult.StaleFence);
                }

                if (!await ClaimFenceMatchesAsync(context, claim, startedAtUtc, token))
                {
                    return Attempt(AgentFencedWriteResult.StaleFence);
                }

                queueItem.MarkStarted(claim.RunAttempt.Id, startedAtUtc);
                return Attempt(AgentFencedWriteResult.Succeeded);
            },
            cancellationToken);
    }

    private static async Task<bool> ClaimFenceMatchesAsync(
        AiGatewayDbContext context,
        DurableTaskClaim claim,
        DateTimeOffset startedAtUtc,
        CancellationToken cancellationToken)
    {
        var task = await AgentExecutionRowLock.ByIdAsync<AgentTask>(
            context, claim.Task.Id.Value, cancellationToken);
        if (task is null || task.ActiveRunAttemptId != claim.RunAttempt.Id ||
            task.RunFencingToken != claim.TaskFencingToken || task.RunLeaseId != claim.LeaseId ||
            task.RunLeaseExpiresAt <= startedAtUtc)
        {
            return false;
        }

        var attempt = await AgentExecutionRowLock.ByIdAsync<AgentTaskRunAttempt>(
            context, claim.RunAttempt.Id.Value, cancellationToken);
        return attempt is not null && attempt.TaskId == claim.Task.Id &&
               attempt.TaskFencingToken == claim.TaskFencingToken && attempt.LeaseId == claim.LeaseId &&
               attempt.LeaseExpiresAt > startedAtUtc;
    }

    public Task<AgentFencedWriteResult> TryCompleteAsync(
        DurableTaskClaim claim,
        AgentTaskRunQueueStatus terminalStatus,
        string? failureCode,
        string safeMessage,
        DateTimeOffset completedAtUtc,
        CancellationToken cancellationToken = default)
    {
        if (terminalStatus is not AgentTaskRunQueueStatus.Succeeded
            and not AgentTaskRunQueueStatus.Failed
            and not AgentTaskRunQueueStatus.Cancelled)
        {
            throw new ArgumentOutOfRangeException(nameof(terminalStatus));
        }

        return transactionRunner.ExecuteAsync(
            "Agent.DurableTaskComplete",
            async (context, token) =>
            {
                var task = await AgentExecutionRowLock.ByIdAsync<AgentTask>(
                    context, claim.Task.Id.Value, token);
                if (task is null || task.RunFencingToken != claim.TaskFencingToken ||
                    task.ActiveRunAttemptId is not null && task.ActiveRunAttemptId != claim.RunAttempt.Id ||
                    task.Status == AgentTaskStatus.ReconciliationRequired)
                {
                    return Attempt(AgentFencedWriteResult.StaleFence);
                }

                var queueItem = await AgentExecutionRowLock.ByIdAsync<AgentTaskRunQueueItem>(
                    context, claim.QueueItem.Id.Value, token);
                if (queueItem is null || queueItem.RunAttemptId != claim.RunAttempt.Id ||
                    queueItem.TaskFencingToken != claim.TaskFencingToken ||
                    queueItem.Status != AgentTaskRunQueueStatus.Started)
                {
                    return Attempt(AgentFencedWriteResult.StaleFence);
                }

                var attempt = await AgentExecutionRowLock.ByIdAsync<AgentTaskRunAttempt>(
                    context, claim.RunAttempt.Id.Value, token);
                if (attempt is null || attempt.TaskId != claim.Task.Id ||
                    attempt.TaskFencingToken != claim.TaskFencingToken ||
                    attempt.Status == AgentTaskRunAttemptStatus.ReconciliationRequired)
                {
                    return Attempt(AgentFencedWriteResult.StaleFence);
                }

                switch (terminalStatus)
                {
                    case AgentTaskRunQueueStatus.Succeeded:
                        queueItem.MarkSucceeded(completedAtUtc, safeMessage);
                        break;
                    case AgentTaskRunQueueStatus.Failed:
                        queueItem.MarkFailed(
                            string.IsNullOrWhiteSpace(failureCode) ? "agent_task_run_failed" : failureCode,
                            safeMessage,
                            completedAtUtc);
                        if (!attempt.IsTerminal)
                        {
                            attempt.MarkFailed(
                                string.IsNullOrWhiteSpace(failureCode) ? "agent_task_run_failed" : failureCode,
                                safeMessage,
                                completedAtUtc);
                        }

                        if (task.Status != AgentTaskStatus.Failed)
                        {
                            task.Fail(safeMessage, completedAtUtc);
                            task.ReleaseRunLease(completedAtUtc, clearActiveAttempt: true);
                        }
                        break;
                    case AgentTaskRunQueueStatus.Cancelled:
                        queueItem.Cancel(completedAtUtc, safeMessage);
                        if (!attempt.IsTerminal)
                        {
                            attempt.Cancel(completedAtUtc, safeMessage);
                        }

                        if (task.Status != AgentTaskStatus.Cancelled)
                        {
                            task.Cancel(completedAtUtc);
                        }
                        break;
                }

                return Attempt(AgentFencedWriteResult.Succeeded);
            },
            cancellationToken);
    }

    public Task<int> RecoverExpiredStartedAsync(
        DateTimeOffset nowUtc,
        int maxItems,
        CancellationToken cancellationToken = default)
    {
        var take = Math.Clamp(maxItems, 1, 100);
        return transactionRunner.ExecuteAsync(
            "Agent.DurableTaskRecoverExpired",
            async (context, token) =>
            {
                var expired = await context.AgentTaskRunQueueItems
                    .FromSqlInterpolated($$"""
                        SELECT queue_item.*, queue_item.xmin
                        FROM aigateway.agent_task_run_queue_items AS queue_item
                        WHERE status = 'Started'
                          AND lease_expires_at IS NOT NULL
                          AND lease_expires_at <= {{nowUtc}}
                        ORDER BY lease_expires_at, created_at, id
                        FOR UPDATE SKIP LOCKED
                        LIMIT {{take}}
                        """)
                    .ToListAsync(token);

                var recovered = 0;
                foreach (var queueItem in expired)
                {
                    if (queueItem.RunAttemptId is null)
                    {
                        continue;
                    }

                    var attempt = await AgentExecutionRowLock.ByIdAsync<AgentTaskRunAttempt>(
                        context, queueItem.RunAttemptId.Value.Value, token);
                    if (attempt is null || attempt.TaskFencingToken != queueItem.TaskFencingToken)
                    {
                        continue;
                    }

                    if (attempt.IsTerminal)
                    {
                        var terminalTask = await AgentExecutionRowLock.ByIdAsync<AgentTask>(
                            context, queueItem.TaskId.Value, token);
                        if (terminalTask is null || terminalTask.RunFencingToken != queueItem.TaskFencingToken)
                        {
                            continue;
                        }

                        var terminalMatched = false;
                        if (attempt.Status == AgentTaskRunAttemptStatus.Succeeded &&
                            terminalTask.Status == AgentTaskStatus.Completed &&
                            terminalTask.ActiveRunAttemptId is null)
                        {
                            queueItem.MarkSucceeded(
                                nowUtc,
                                attempt.SafeMessage ?? terminalTask.FinalSummary ?? "Agent task completed.");
                            terminalMatched = true;
                        }
                        else if (attempt.Status == AgentTaskRunAttemptStatus.Failed &&
                                 terminalTask.Status == AgentTaskStatus.Failed)
                        {
                            queueItem.MarkFailed(
                                attempt.FailureCode ?? "agent_task_failed",
                                attempt.SafeMessage ?? terminalTask.FinalSummary ?? "Agent task failed.",
                                nowUtc);
                            terminalMatched = true;
                        }
                        else if (attempt.Status == AgentTaskRunAttemptStatus.Cancelled &&
                                 terminalTask.Status == AgentTaskStatus.Cancelled)
                        {
                            queueItem.Cancel(
                                nowUtc,
                                attempt.SafeMessage ?? "Agent task was cancelled.");
                            terminalMatched = true;
                        }

                        if (terminalMatched)
                        {
                            recovered++;
                        }

                        continue;
                    }

                    var nodes = await context.AgentNodeRuns
                        .Where(node => node.RunAttemptId == queueItem.RunAttemptId.Value)
                        .ToListAsync(token);
                    var hasUnexpiredExecution = false;
                    var requiresReconciliation = nodes.Any(node => node.Status == AgentNodeRunStatus.OutcomeUnknown);
                    foreach (var node in nodes.Where(node =>
                                 node.Status is AgentNodeRunStatus.Claimed or AgentNodeRunStatus.Running))
                    {
                        if (node.LeaseExpiresAt is not null && node.LeaseExpiresAt > nowUtc)
                        {
                            hasUnexpiredExecution = true;
                            continue;
                        }

                        if (node.Status == AgentNodeRunStatus.Claimed)
                        {
                            var reservation = node.GetActiveBudgetReservation(node.NodeFencingToken);
                            if (!attempt.TryReleaseBudget(reservation, consumeRetry: false))
                            {
                                throw new InvalidOperationException(
                                    "Expired pre-execution NodeRun budget reservation is inconsistent.");
                            }

                            node.CloseBudgetReservation(
                                node.NodeFencingToken,
                                AgentBudgetReservationStatus.Released,
                                nowUtc);
                            node.RecoverExpiredClaim(nowUtc);
                            continue;
                        }

                        if (node.SideEffectClass is AgentNodeSideEffectClass.ReadOnly
                            or AgentNodeSideEffectClass.DeterministicInternal)
                        {
                            var reservation = node.GetActiveBudgetReservation(node.NodeFencingToken);
                            if (!attempt.TrySettleBudget(
                                    reservation,
                                    AgentRunBudgetCharge.Zero,
                                    conservativelyConsumed: true))
                            {
                                throw new InvalidOperationException(
                                    "Expired running NodeRun budget reservation is inconsistent.");
                            }

                            node.CloseBudgetReservation(
                                node.NodeFencingToken,
                                AgentBudgetReservationStatus.ConservativelyConsumed,
                                nowUtc);
                            node.RecoverExpiredSafeExecution(nowUtc);
                            continue;
                        }

                        AgentNodeOutcomeUnknownMarker.Mark(
                            node,
                            "worker-loss-receipt-or-manual-v1",
                            "worker-lease-expired-after-dispatch",
                            "Worker lease expired after a side-effecting node began; automatic replay is blocked.",
                            nowUtc);
                        requiresReconciliation = true;
                    }

                    if (hasUnexpiredExecution)
                    {
                        continue;
                    }

                    if (requiresReconciliation)
                    {
                        var task = await AgentExecutionRowLock.ByIdAsync<AgentTask>(
                            context, queueItem.TaskId.Value, token);
                        if (task is not null && task.ActiveRunAttemptId == queueItem.RunAttemptId &&
                            task.RunFencingToken == queueItem.TaskFencingToken &&
                            task.Status != AgentTaskStatus.ReconciliationRequired)
                        {
                            await context.Entry(task).Collection(candidate => candidate.Steps).LoadAsync(token);
                            task.RequireReconciliation(nowUtc);
                        }

                        if (attempt.Status != AgentTaskRunAttemptStatus.ReconciliationRequired)
                        {
                            attempt.RequireReconciliation(
                                nowUtc,
                                "Side-effecting node outcome must be reconciled after worker loss.");
                        }

                        continue;
                    }

                    queueItem.RecoverExpiredStartForReclaim(nowUtc);
                    recovered++;
                }

                return Attempt(recovered);
            },
            cancellationToken);
    }

    private static AgentExecutionTransactionAttempt<T> Attempt<T>(T value) => new(value);
}
