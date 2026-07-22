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
                    return new AgentExecutionTransactionAttempt<DurableTaskClaim?>(null);
                }

                var task = await context.AgentTasks
                    .FromSqlInterpolated($$"""
                        SELECT task.*, task.xmin
                        FROM aigateway.agent_tasks AS task
                        WHERE id = {{queueItem.TaskId.Value}}
                        FOR UPDATE
                        """)
                    .SingleOrDefaultAsync(token);
                if (task is null || task.IsRunInProgress(now))
                {
                    return new AgentExecutionTransactionAttempt<DurableTaskClaim?>(null);
                }

                await context.Entry(task)
                    .Collection(candidate => candidate.Steps)
                    .LoadAsync(token);

                AgentTaskRunAttempt? attempt = null;
                if (task.ActiveRunAttemptId is not null)
                {
                    attempt = await context.AgentTaskRunAttempts
                        .FromSqlInterpolated($$"""
                            SELECT attempt.*, attempt.xmin
                            FROM aigateway.agent_task_run_attempts AS attempt
                            WHERE id = {{task.ActiveRunAttemptId.Value.Value}}
                            FOR UPDATE
                            """)
                        .SingleOrDefaultAsync(token);
                    if (attempt is not null && attempt.IsTerminal)
                    {
                        attempt = null;
                    }
                    else if (attempt?.HasActiveLease(now) == true)
                    {
                        return new AgentExecutionTransactionAttempt<DurableTaskClaim?>(null);
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
                return new AgentExecutionTransactionAttempt<DurableTaskClaim?>(claim);
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
                var queueItem = await context.AgentTaskRunQueueItems
                    .FromSqlInterpolated($$"""
                        SELECT queue_item.*, queue_item.xmin
                        FROM aigateway.agent_task_run_queue_items AS queue_item
                        WHERE id = {{claim.QueueItem.Id.Value}}
                          AND run_attempt_id = {{claim.RunAttempt.Id.Value}}
                          AND task_fencing_token = {{claim.TaskFencingToken}}
                          AND lease_id = {{claim.LeaseId}}
                          AND status = 'Claimed'
                        FOR UPDATE
                        """)
                    .SingleOrDefaultAsync(token);
                if (queueItem is null)
                {
                    return new AgentExecutionTransactionAttempt<AgentFencedWriteResult>(
                        AgentFencedWriteResult.StaleFence);
                }

                var taskFenceValid = await context.AgentTasks
                    .AnyAsync(task =>
                        task.Id == claim.Task.Id &&
                        task.ActiveRunAttemptId == claim.RunAttempt.Id &&
                        task.RunFencingToken == claim.TaskFencingToken &&
                        task.RunLeaseId == claim.LeaseId &&
                        task.RunLeaseExpiresAt > startedAtUtc,
                        token);
                var attemptFenceValid = await context.AgentTaskRunAttempts
                    .AnyAsync(attempt =>
                        attempt.Id == claim.RunAttempt.Id &&
                        attempt.TaskId == claim.Task.Id &&
                        attempt.TaskFencingToken == claim.TaskFencingToken &&
                        attempt.LeaseId == claim.LeaseId &&
                        attempt.LeaseExpiresAt > startedAtUtc,
                        token);
                if (!taskFenceValid || !attemptFenceValid)
                {
                    return new AgentExecutionTransactionAttempt<AgentFencedWriteResult>(
                        AgentFencedWriteResult.StaleFence);
                }

                queueItem.MarkStarted(claim.RunAttempt.Id, startedAtUtc);
                return new AgentExecutionTransactionAttempt<AgentFencedWriteResult>(
                    AgentFencedWriteResult.Succeeded);
            },
            cancellationToken);
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
                var task = await context.AgentTasks
                    .FromSqlInterpolated($$"""
                        SELECT task.*, task.xmin
                        FROM aigateway.agent_tasks AS task
                        WHERE id = {{claim.Task.Id.Value}}
                          AND run_fencing_token = {{claim.TaskFencingToken}}
                          AND (active_run_attempt_id = {{claim.RunAttempt.Id.Value}} OR active_run_attempt_id IS NULL)
                          AND status <> 'ReconciliationRequired'
                        FOR UPDATE
                        """)
                    .SingleOrDefaultAsync(token);
                if (task is null)
                {
                    return new AgentExecutionTransactionAttempt<AgentFencedWriteResult>(
                        AgentFencedWriteResult.StaleFence);
                }

                var queueItem = await context.AgentTaskRunQueueItems
                    .FromSqlInterpolated($$"""
                        SELECT queue_item.*, queue_item.xmin
                        FROM aigateway.agent_task_run_queue_items AS queue_item
                        WHERE id = {{claim.QueueItem.Id.Value}}
                          AND run_attempt_id = {{claim.RunAttempt.Id.Value}}
                          AND task_fencing_token = {{claim.TaskFencingToken}}
                          AND status = 'Started'
                        FOR UPDATE
                        """)
                    .SingleOrDefaultAsync(token);
                if (queueItem is null)
                {
                    return new AgentExecutionTransactionAttempt<AgentFencedWriteResult>(
                        AgentFencedWriteResult.StaleFence);
                }

                var attempt = await context.AgentTaskRunAttempts
                    .FromSqlInterpolated($$"""
                        SELECT attempt.*, attempt.xmin
                        FROM aigateway.agent_task_run_attempts AS attempt
                        WHERE id = {{claim.RunAttempt.Id.Value}}
                          AND task_id = {{claim.Task.Id.Value}}
                          AND task_fencing_token = {{claim.TaskFencingToken}}
                          AND status <> 'ReconciliationRequired'
                        FOR UPDATE
                        """)
                    .SingleOrDefaultAsync(token);
                if (attempt is null)
                {
                    return new AgentExecutionTransactionAttempt<AgentFencedWriteResult>(
                        AgentFencedWriteResult.StaleFence);
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

                return new AgentExecutionTransactionAttempt<AgentFencedWriteResult>(
                    AgentFencedWriteResult.Succeeded);
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

                    var attempt = await context.AgentTaskRunAttempts
                        .FromSqlInterpolated($$"""
                            SELECT attempt.*, attempt.xmin FROM aigateway.agent_task_run_attempts AS attempt
                            WHERE id = {{queueItem.RunAttemptId.Value.Value}}
                              AND task_fencing_token = {{queueItem.TaskFencingToken}}
                            FOR UPDATE
                            """)
                        .SingleOrDefaultAsync(token);
                    if (attempt is null)
                    {
                        continue;
                    }

                    if (attempt.IsTerminal)
                    {
                        var terminalTask = await context.AgentTasks
                            .FromSqlInterpolated($$"""
                                SELECT task.*, task.xmin FROM aigateway.agent_tasks AS task
                                WHERE id = {{queueItem.TaskId.Value}}
                                  AND run_fencing_token = {{queueItem.TaskFencingToken}}
                                FOR UPDATE
                                """)
                            .SingleOrDefaultAsync(token);
                        if (terminalTask is null)
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

                        node.MarkOutcomeUnknown(
                            node.TaskFencingToken,
                            node.NodeFencingToken,
                            node.ProviderOperationCode ?? node.ToolCode ?? node.NodeKind,
                            node.ProviderReceiptHash,
                            "worker-loss-receipt-or-manual-v1",
                            "worker-lease-expired-after-dispatch",
                            "not-confirmed",
                            "Worker lease expired after a side-effecting node began; automatic replay is blocked.",
                            nowUtc,
                            nowUtc.AddMinutes(1),
                            nowUtc.AddHours(24));
                        requiresReconciliation = true;
                    }

                    if (hasUnexpiredExecution)
                    {
                        continue;
                    }

                    if (requiresReconciliation)
                    {
                        var task = await context.AgentTasks
                            .FromSqlInterpolated($$"""
                                SELECT task.*, task.xmin FROM aigateway.agent_tasks AS task
                                WHERE id = {{queueItem.TaskId.Value}}
                                  AND active_run_attempt_id = {{queueItem.RunAttemptId.Value.Value}}
                                  AND run_fencing_token = {{queueItem.TaskFencingToken}}
                                FOR UPDATE
                                """)
                            .SingleOrDefaultAsync(token);
                        if (task is not null && task.Status != AgentTaskStatus.ReconciliationRequired)
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

                return new AgentExecutionTransactionAttempt<int>(recovered);
            },
            cancellationToken);
    }
}
