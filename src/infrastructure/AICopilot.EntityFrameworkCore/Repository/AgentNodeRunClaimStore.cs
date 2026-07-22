using AICopilot.Core.AiGateway.Aggregates.AgentTasks;
using AICopilot.Core.AiGateway.Ids;
using AICopilot.Core.AiGateway.Runtime.AgentExecution;
using AICopilot.EntityFrameworkCore.Transactions;
using AICopilot.Services.Contracts;
using Microsoft.EntityFrameworkCore;

namespace AICopilot.EntityFrameworkCore.Repository;

internal sealed class AgentNodeRunClaimStore(
    AgentExecutionTransactionRunner transactionRunner)
    : IAgentNodeRunClaimStore
{
    public Task<AgentNodeRunClaimOutcome> TryClaimNextAsync(
        AgentTaskRunAttemptId runAttemptId,
        long taskFencingToken,
        string leaseOwner,
        TimeSpan leaseDuration,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default)
    {
        if (taskFencingToken <= 0 || leaseDuration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(taskFencingToken));
        }

        return transactionRunner.ExecuteAsync(
            "Agent.NodeRunClaim",
            async (context, token) =>
            {
                var task = await context.AgentTasks
                    .FromSqlInterpolated($$"""
                        SELECT task.*, task.xmin
                        FROM aigateway.agent_tasks AS task
                        WHERE active_run_attempt_id = {{runAttemptId.Value}}
                          AND run_fencing_token = {{taskFencingToken}}
                          AND run_lease_id IS NOT NULL
                          AND run_lease_expires_at > {{nowUtc}}
                        FOR UPDATE
                        """)
                    .SingleOrDefaultAsync(token);
                if (task is null)
                {
                    return Outcome(
                        AgentNodeRunClaimOutcomeCode.StaleTaskFence,
                        "Task claim authority is stale or expired.");
                }

                var attempt = await context.AgentTaskRunAttempts
                    .FromSqlInterpolated($$"""
                        SELECT attempt.*, attempt.xmin
                        FROM aigateway.agent_task_run_attempts AS attempt
                        WHERE id = {{runAttemptId.Value}}
                          AND task_id = {{task.Id.Value}}
                          AND task_fencing_token = {{taskFencingToken}}
                          AND lease_id = {{task.RunLeaseId}}
                          AND lease_expires_at > {{nowUtc}}
                        FOR UPDATE
                        """)
                    .SingleOrDefaultAsync(token);
                if (attempt is null)
                {
                    return Outcome(
                        AgentNodeRunClaimOutcomeCode.StaleTaskFence,
                        "RunAttempt claim authority is stale or expired.");
                }

                var queueItem = await context.AgentTaskRunQueueItems.SingleOrDefaultAsync(item =>
                    item.RunAttemptId == runAttemptId &&
                    item.TaskFencingToken == taskFencingToken &&
                    item.LeaseId == task.RunLeaseId &&
                    item.Status == AgentTaskRunQueueStatus.Started,
                    token);
                if (queueItem is null)
                {
                    return Outcome(
                        AgentNodeRunClaimOutcomeCode.StaleTaskFence,
                        "Queue claim authority is stale or no longer started.");
                }

                var node = await context.AgentNodeRuns
                    .FromSqlInterpolated($$"""
                        SELECT node.*, node.xmin
                        FROM aigateway.agent_node_runs AS node
                        WHERE run_attempt_id = {{runAttemptId.Value}}
                          AND queue_item_id = {{queueItem.Id.Value}}
                          AND task_fencing_token = {{taskFencingToken}}
                          AND status = 'Runnable'
                          AND (next_attempt_at IS NULL OR next_attempt_at <= {{nowUtc}})
                        ORDER BY created_at, node_id, id
                        FOR UPDATE SKIP LOCKED
                        LIMIT 1
                        """)
                    .SingleOrDefaultAsync(token);
                if (node is null)
                {
                    return Outcome(
                        AgentNodeRunClaimOutcomeCode.NoneAvailable,
                        "No runnable NodeRun is available under the current task claim.");
                }

                var reservation = node.CreateBudgetReservationUpperBound();
                var budgetResult = attempt.TryReserveBudget(reservation, nowUtc);
                if (budgetResult != AgentRunBudgetReservationResult.Reserved)
                {
                    return Outcome(MapBudgetResult(budgetResult), BudgetReason(budgetResult));
                }

                node.Claim(taskFencingToken, leaseOwner, nowUtc, leaseDuration);
                node.BindBudgetReservation(
                    taskFencingToken,
                    node.NodeFencingToken,
                    reservation,
                    nowUtc);
                var claim = new AgentNodeRunClaim(
                    node,
                    queueItem.Id,
                    runAttemptId,
                    taskFencingToken,
                    node.NodeFencingToken,
                    task.RunLeaseId!.Value,
                    node.LeaseId!.Value,
                    node.LeaseExpiresAt!.Value);
                return new AgentExecutionTransactionAttempt<AgentNodeRunClaimOutcome>(
                    new AgentNodeRunClaimOutcome(
                        AgentNodeRunClaimOutcomeCode.Claimed,
                        claim,
                        "NodeRun claimed with an atomic task-budget reservation."));
            },
            cancellationToken);
    }

    public Task<AgentFencedWriteResult> TryMarkRunningAsync(
        AgentNodeRunClaim claim,
        DateTimeOffset startedAtUtc,
        CancellationToken cancellationToken = default)
    {
        return transactionRunner.ExecuteAsync(
            "Agent.NodeRunStart",
            async (context, token) =>
            {
                var authorityValid = await HasActiveTaskAuthorityAsync(context, claim, startedAtUtc, token);
                if (!authorityValid)
                {
                    return new AgentExecutionTransactionAttempt<AgentFencedWriteResult>(
                        AgentFencedWriteResult.StaleFence);
                }

                var node = await LockNodeAsync(context, claim, "Claimed", token);
                if (node is null || node.LeaseExpiresAt <= startedAtUtc)
                {
                    return new AgentExecutionTransactionAttempt<AgentFencedWriteResult>(
                        AgentFencedWriteResult.StaleFence);
                }

                node.Start(claim.TaskFencingToken, claim.NodeFencingToken, startedAtUtc);
                return new AgentExecutionTransactionAttempt<AgentFencedWriteResult>(
                    AgentFencedWriteResult.Succeeded);
            },
            cancellationToken);
    }

    public Task<AgentFencedWriteResult> TryRenewTaskAndNodeLeaseAsync(
        AgentNodeRunClaim claim,
        TimeSpan taskLeaseDuration,
        TimeSpan nodeLeaseDuration,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default)
    {
        if (taskLeaseDuration <= TimeSpan.Zero || nodeLeaseDuration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(taskLeaseDuration));
        }

        return transactionRunner.ExecuteAsync(
            "Agent.NodeRunRenewLeases",
            async (context, token) =>
            {
                var task = await context.AgentTasks
                    .FromSqlInterpolated($$"""
                        SELECT task.*, task.xmin FROM aigateway.agent_tasks AS task
                        WHERE id = {{claim.NodeRun.TaskId.Value}}
                          AND active_run_attempt_id = {{claim.RunAttemptId.Value}}
                          AND run_fencing_token = {{claim.TaskFencingToken}}
                          AND run_lease_id = {{claim.TaskLeaseId}}
                        FOR UPDATE
                        """)
                    .SingleOrDefaultAsync(token);
                var attempt = await context.AgentTaskRunAttempts
                    .FromSqlInterpolated($$"""
                        SELECT attempt.*, attempt.xmin FROM aigateway.agent_task_run_attempts AS attempt
                        WHERE id = {{claim.RunAttemptId.Value}}
                          AND task_fencing_token = {{claim.TaskFencingToken}}
                          AND lease_id = {{claim.TaskLeaseId}}
                        FOR UPDATE
                        """)
                    .SingleOrDefaultAsync(token);
                var queueItem = await context.AgentTaskRunQueueItems
                    .FromSqlInterpolated($$"""
                        SELECT queue_item.*, queue_item.xmin FROM aigateway.agent_task_run_queue_items AS queue_item
                        WHERE id = {{claim.QueueItemId.Value}}
                          AND run_attempt_id = {{claim.RunAttemptId.Value}}
                          AND task_fencing_token = {{claim.TaskFencingToken}}
                          AND lease_id = {{claim.TaskLeaseId}}
                          AND status = 'Started'
                        FOR UPDATE
                        """)
                    .SingleOrDefaultAsync(token);
                var node = await LockNodeAsync(context, claim, null, token);
                if (task is null || attempt is null || queueItem is null || node is null ||
                    node.Status is not AgentNodeRunStatus.Claimed and not AgentNodeRunStatus.Running)
                {
                    return new AgentExecutionTransactionAttempt<AgentFencedWriteResult>(
                        AgentFencedWriteResult.StaleFence);
                }

                var taskExpiry = nowUtc.Add(taskLeaseDuration);
                attempt.RefreshLease(nowUtc, taskLeaseDuration);
                task.RefreshRunLease(taskExpiry, nowUtc);
                queueItem.RefreshLease(
                    claim.TaskFencingToken,
                    claim.TaskLeaseId,
                    nowUtc,
                    taskLeaseDuration);
                node.RenewLease(
                    claim.TaskFencingToken,
                    claim.NodeFencingToken,
                    nowUtc,
                    nodeLeaseDuration);
                return new AgentExecutionTransactionAttempt<AgentFencedWriteResult>(
                    AgentFencedWriteResult.Succeeded);
            },
            cancellationToken);
    }

    private static Task<bool> HasActiveTaskAuthorityAsync(
        AiGatewayDbContext context,
        AgentNodeRunClaim claim,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        return context.AgentTasks.AnyAsync(task =>
            task.Id == claim.NodeRun.TaskId &&
            task.ActiveRunAttemptId == claim.RunAttemptId &&
            task.RunFencingToken == claim.TaskFencingToken &&
            task.RunLeaseId == claim.TaskLeaseId &&
            task.RunLeaseExpiresAt > nowUtc,
            cancellationToken);
    }

    private static Task<AgentNodeRun?> LockNodeAsync(
        AiGatewayDbContext context,
        AgentNodeRunClaim claim,
        string? expectedStatus,
        CancellationToken cancellationToken)
    {
        return context.AgentNodeRuns
            .FromSqlInterpolated($$"""
                SELECT node.*, node.xmin FROM aigateway.agent_node_runs AS node
                WHERE id = {{claim.NodeRun.Id.Value}}
                  AND run_attempt_id = {{claim.RunAttemptId.Value}}
                  AND task_fencing_token = {{claim.TaskFencingToken}}
                  AND node_fencing_token = {{claim.NodeFencingToken}}
                  AND lease_id = {{claim.NodeLeaseId}}
                  AND ({{expectedStatus}} IS NULL OR status = {{expectedStatus}})
                FOR UPDATE
                """)
            .SingleOrDefaultAsync(cancellationToken);
    }

    private static AgentExecutionTransactionAttempt<AgentNodeRunClaimOutcome> Outcome(
        AgentNodeRunClaimOutcomeCode code,
        string reason)
    {
        return new AgentExecutionTransactionAttempt<AgentNodeRunClaimOutcome>(
            new AgentNodeRunClaimOutcome(code, Claim: null, SafeReason: reason));
    }

    private static AgentNodeRunClaimOutcomeCode MapBudgetResult(AgentRunBudgetReservationResult result)
    {
        return result switch
        {
            AgentRunBudgetReservationResult.BudgetNotInitialized => AgentNodeRunClaimOutcomeCode.BudgetNotInitialized,
            AgentRunBudgetReservationResult.WorkflowElapsedExceeded => AgentNodeRunClaimOutcomeCode.WorkflowElapsedExceeded,
            AgentRunBudgetReservationResult.ToolCallsExceeded => AgentNodeRunClaimOutcomeCode.ToolCallsExceeded,
            AgentRunBudgetReservationResult.ModelCallsExceeded => AgentNodeRunClaimOutcomeCode.ModelCallsExceeded,
            AgentRunBudgetReservationResult.InputTokensExceeded => AgentNodeRunClaimOutcomeCode.InputTokensExceeded,
            AgentRunBudgetReservationResult.OutputTokensExceeded => AgentNodeRunClaimOutcomeCode.OutputTokensExceeded,
            AgentRunBudgetReservationResult.ElapsedUsageExceeded => AgentNodeRunClaimOutcomeCode.ElapsedUsageExceeded,
            AgentRunBudgetReservationResult.CostExceeded => AgentNodeRunClaimOutcomeCode.CostExceeded,
            AgentRunBudgetReservationResult.RetriesExceeded => AgentNodeRunClaimOutcomeCode.RetriesExceeded,
            AgentRunBudgetReservationResult.ArtifactCountExceeded => AgentNodeRunClaimOutcomeCode.ArtifactCountExceeded,
            AgentRunBudgetReservationResult.ArtifactBytesExceeded => AgentNodeRunClaimOutcomeCode.ArtifactBytesExceeded,
            _ => throw new InvalidOperationException("Unsupported task-budget reservation result.")
        };
    }

    private static string BudgetReason(AgentRunBudgetReservationResult result)
    {
        return $"NodeRun was not claimed because the immutable task budget rejected {result}.";
    }
}
