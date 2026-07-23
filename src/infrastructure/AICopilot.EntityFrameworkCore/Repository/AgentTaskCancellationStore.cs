using AICopilot.Core.AiGateway.Aggregates.AgentTasks;
using AICopilot.Core.AiGateway.Aggregates.Approvals;
using AICopilot.Core.AiGateway.Ids;
using AICopilot.Core.AiGateway.Runtime.AgentExecution;
using AICopilot.EntityFrameworkCore.Transactions;
using AICopilot.Services.Contracts;
using Microsoft.EntityFrameworkCore;

namespace AICopilot.EntityFrameworkCore.Repository;

internal sealed class AgentTaskCancellationStore(
    AgentExecutionTransactionRunner transactionRunner)
    : IAgentTaskCancellationStore
{
    public async Task<AgentTaskCancellationCheckpoint> RequestAsync(
        AgentTaskId taskId,
        DateTimeOffset requestedAtUtc,
        string safeMessage,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(safeMessage);
        try
        {
            return await transactionRunner.ExecuteAsync(
                "Agent.TaskCancellation",
                async (context, token) =>
            {
                var task = await context.AgentTasks
                    .FromSqlInterpolated($$"""
                        SELECT task.*, task.xmin FROM aigateway.agent_tasks AS task
                        WHERE id = {{taskId.Value}}
                        FOR UPDATE
                        """)
                    .SingleOrDefaultAsync(token);
                if (task is null)
                {
                    return Attempt(
                        AgentTaskCancellationDisposition.StateConflict,
                        [],
                        "Agent task no longer exists.");
                }

                var queues = await context.AgentTaskRunQueueItems
                    .FromSqlInterpolated($$"""
                        SELECT queue_item.*, queue_item.xmin FROM aigateway.agent_task_run_queue_items AS queue_item
                        WHERE task_id = {{taskId.Value}}
                          AND status IN ('Queued', 'Claimed', 'Started')
                        ORDER BY created_at, id
                        FOR UPDATE
                        """)
                    .ToListAsync(token);
                var queueSnapshots = queues
                    .Select(queue => new AgentTaskCancellationQueueItem(queue, queue.Status))
                    .ToArray();
                if (IsTerminal(task.Status))
                {
                    return Attempt(
                        AgentTaskCancellationDisposition.AlreadyTerminal,
                        queueSnapshots,
                        "Agent task is already terminal.");
                }

                var steps = await context.Set<AgentStep>()
                    .FromSqlInterpolated($$"""
                        SELECT step.*, step.xmin FROM aigateway.agent_steps AS step
                        WHERE task_id = {{taskId.Value}}
                        ORDER BY step_index
                        FOR UPDATE
                        """)
                    .ToListAsync(token);
                var pendingApprovals = await context.ApprovalRequests
                    .FromSqlInterpolated($$"""
                        SELECT approval.*, approval.xmin FROM aigateway.approval_requests AS approval
                        WHERE task_id = {{taskId.Value}}
                          AND status = 'Pending'
                        ORDER BY created_at, id
                        FOR UPDATE
                        """)
                    .ToListAsync(token);
                AgentTaskRunAttempt? attempt = null;
                List<AgentNodeRun> nodes = [];
                if (task.ActiveRunAttemptId is not null)
                {
                    attempt = await context.AgentTaskRunAttempts
                        .FromSqlInterpolated($$"""
                            SELECT attempt.*, attempt.xmin FROM aigateway.agent_task_run_attempts AS attempt
                            WHERE id = {{task.ActiveRunAttemptId.Value.Value}}
                              AND task_id = {{taskId.Value}}
                            FOR UPDATE
                            """)
                        .SingleOrDefaultAsync(token);
                    if (attempt is null)
                    {
                        return Attempt(
                            AgentTaskCancellationDisposition.StateConflict,
                            queueSnapshots,
                            "Active run attempt is missing; cancellation did not guess a terminal state.");
                    }

                    nodes = await context.AgentNodeRuns
                        .FromSqlInterpolated($$"""
                            SELECT node.*, node.xmin FROM aigateway.agent_node_runs AS node
                            WHERE run_attempt_id = {{attempt.Id.Value}}
                            ORDER BY created_at, node_id, id
                            FOR UPDATE
                            """)
                        .ToListAsync(token);
                }

                foreach (var approval in pendingApprovals)
                {
                    approval.Cancel(requestedAtUtc);
                }

                var hasUnknownSideEffect = nodes.Any(node =>
                    node.Status == AgentNodeRunStatus.OutcomeUnknown ||
                    node.Status == AgentNodeRunStatus.Running &&
                    node.SideEffectClass is not (AgentNodeSideEffectClass.ReadOnly or AgentNodeSideEffectClass.DeterministicInternal));
                if (hasUnknownSideEffect)
                {
                    foreach (var node in nodes.Where(node =>
                                 node.Status == AgentNodeRunStatus.Running &&
                                 node.SideEffectClass is not (AgentNodeSideEffectClass.ReadOnly or AgentNodeSideEffectClass.DeterministicInternal)))
                    {
                        AgentNodeOutcomeUnknownMarker.Mark(
                            node,
                            "cancellation-receipt-or-manual-v1",
                            "user-cancelled-after-side-effect-dispatch",
                            "Cancellation was requested after a side-effecting node began; outcome reconciliation is required.",
                            requestedAtUtc);
                    }

                    if (task.Status != AgentTaskStatus.ReconciliationRequired)
                    {
                        task.RequireReconciliation(requestedAtUtc);
                    }

                    if (attempt is not null &&
                        attempt.Status != AgentTaskRunAttemptStatus.ReconciliationRequired)
                    {
                        attempt.RequireReconciliation(
                            requestedAtUtc,
                            "Cancellation is waiting for side-effect outcome reconciliation.");
                    }

                    return Attempt(
                        AgentTaskCancellationDisposition.ReconciliationRequired,
                        queueSnapshots,
                        "Cancellation is pending authoritative side-effect reconciliation.");
                }

                if (attempt is not null)
                {
                    foreach (var node in nodes.Where(node => !node.IsTerminal))
                    {
                        switch (node.Status)
                        {
                            case AgentNodeRunStatus.Pending:
                            case AgentNodeRunStatus.Runnable:
                            case AgentNodeRunStatus.WaitingApproval:
                                node.CancelBeforeExecution(safeMessage, requestedAtUtc);
                                break;
                            case AgentNodeRunStatus.Claimed:
                            {
                                var reservation = node.GetActiveBudgetReservation(node.NodeFencingToken);
                                if (!attempt.TryReleaseBudget(reservation, consumeRetry: false))
                                {
                                    throw new CancellationStateConflictException(
                                        "Claimed NodeRun budget reservation could not be released safely.");
                                }

                                node.CloseBudgetReservation(
                                    node.NodeFencingToken,
                                    AgentBudgetReservationStatus.Released,
                                    requestedAtUtc);
                                node.CancelActiveKnownNoSideEffect(
                                    node.TaskFencingToken,
                                    node.NodeFencingToken,
                                    safeMessage,
                                    requestedAtUtc);
                                break;
                            }
                            case AgentNodeRunStatus.Running:
                            {
                                var reservation = node.GetActiveBudgetReservation(node.NodeFencingToken);
                                if (!attempt.TrySettleBudget(
                                        reservation,
                                        AgentRunBudgetCharge.Zero,
                                        conservativelyConsumed: true))
                                {
                                    throw new CancellationStateConflictException(
                                        "Running NodeRun budget reservation could not be closed safely.");
                                }

                                node.CloseBudgetReservation(
                                    node.NodeFencingToken,
                                    AgentBudgetReservationStatus.ConservativelyConsumed,
                                    requestedAtUtc);
                                node.CancelActiveKnownNoSideEffect(
                                    node.TaskFencingToken,
                                    node.NodeFencingToken,
                                    safeMessage,
                                    requestedAtUtc);
                                break;
                            }
                            default:
                                throw new CancellationStateConflictException(
                                    "NodeRun state is not cancellable without reconciliation.");
                        }
                    }

                    if (!attempt.IsTerminal)
                    {
                        attempt.Cancel(requestedAtUtc, safeMessage);
                    }
                }

                foreach (var queue in queues)
                {
                    queue.Cancel(requestedAtUtc, safeMessage);
                }

                foreach (var step in steps.Where(step =>
                             step.Status is not AgentStepStatus.Completed
                                 and not AgentStepStatus.Failed
                                 and not AgentStepStatus.Cancelled))
                {
                    step.Cancel(requestedAtUtc);
                }

                task.Cancel(requestedAtUtc);
                return Attempt(
                    AgentTaskCancellationDisposition.Cancelled,
                    queueSnapshots,
                    safeMessage);
                },
                cancellationToken);
        }
        catch (CancellationStateConflictException exception)
        {
            return new AgentTaskCancellationCheckpoint(
                AgentTaskCancellationDisposition.StateConflict,
                [],
                exception.Message);
        }
    }

    private static bool IsTerminal(AgentTaskStatus status) =>
        status is AgentTaskStatus.Completed
            or AgentTaskStatus.Finalized
            or AgentTaskStatus.Failed
            or AgentTaskStatus.Rejected
            or AgentTaskStatus.Cancelled;

    private static AgentExecutionTransactionAttempt<AgentTaskCancellationCheckpoint> Attempt(
        AgentTaskCancellationDisposition disposition,
        IReadOnlyCollection<AgentTaskCancellationQueueItem> queues,
        string safeMessage) =>
        new(new AgentTaskCancellationCheckpoint(disposition, queues, safeMessage));

    private sealed class CancellationStateConflictException(string message) : Exception(message);
}
