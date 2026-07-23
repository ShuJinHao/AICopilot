using AICopilot.Core.AiGateway.Aggregates.AgentTasks;
using AICopilot.Core.AiGateway.Ids;
using AICopilot.Core.AiGateway.Runtime.AgentExecution;
using AICopilot.EntityFrameworkCore.Transactions;
using AICopilot.Services.Contracts;
using Microsoft.EntityFrameworkCore;

namespace AICopilot.EntityFrameworkCore.Repository;

internal sealed class AgentNodeOutcomeReconciliationStore(
    AgentExecutionTransactionRunner transactionRunner)
    : IAgentNodeOutcomeReconciliationStore
{
    public Task<AgentOutcomeReconciliationClaim?> TryClaimNextAsync(
        string owner, TimeSpan leaseDuration, DateTimeOffset nowUtc, CancellationToken cancellationToken = default)
    {
        ValidateClaimRequest(owner, leaseDuration);

        return TryClaimCoreAsync(
            "Agent.NodeOutcomeReconciliationClaim",
            owner,
            leaseDuration,
            nowUtc,
            ignoreSchedule: false,
            (context, token) => context.AgentNodeRuns
                    .FromSqlInterpolated($$"""
                        SELECT node.*, node.xmin
                        FROM aigateway.agent_node_runs AS node
                        WHERE status = 'OutcomeUnknown'
                          AND next_attempt_at <= {{nowUtc}}
                          AND (reconciliation_lease_expires_at IS NULL OR reconciliation_lease_expires_at <= {{nowUtc}})
                        ORDER BY requires_manual_resolution, next_attempt_at, created_at, id
                        FOR UPDATE SKIP LOCKED
                        LIMIT 1
                        """)
                    .SingleOrDefaultAsync(token),
            cancellationToken);
    }

    public Task<AgentOutcomeReconciliationClaim?> TryClaimAsync(
        AgentNodeRunId nodeRunId, string owner, TimeSpan leaseDuration, DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default)
    {
        ValidateClaimRequest(owner, leaseDuration);

        return TryClaimCoreAsync(
            "Agent.NodeOutcomeReconciliationClaimById",
            owner,
            leaseDuration,
            nowUtc,
            ignoreSchedule: true,
            (context, token) => context.AgentNodeRuns
                    .FromSqlInterpolated($$"""
                        SELECT node.*, node.xmin
                        FROM aigateway.agent_node_runs AS node
                        WHERE id = {{nodeRunId.Value}}
                          AND status = 'OutcomeUnknown'
                          AND (reconciliation_lease_expires_at IS NULL OR reconciliation_lease_expires_at <= {{nowUtc}})
                        FOR UPDATE SKIP LOCKED
                        """)
                    .SingleOrDefaultAsync(token),
            cancellationToken);
    }

    private Task<AgentOutcomeReconciliationClaim?> TryClaimCoreAsync(
        string operationName,
        string owner,
        TimeSpan leaseDuration,
        DateTimeOffset nowUtc,
        bool ignoreSchedule,
        Func<AiGatewayDbContext, CancellationToken, Task<AgentNodeRun?>> loadNode,
        CancellationToken cancellationToken)
    {
        return transactionRunner.ExecuteAsync(
            operationName,
            async (context, token) =>
            {
                var node = await loadNode(context, token);
                return node is null
                    ? Attempt<AgentOutcomeReconciliationClaim?>(null)
                    : await ClaimNodeAsync(context, node, owner, leaseDuration, nowUtc, ignoreSchedule, token);
            },
            cancellationToken);
    }

    public Task<AgentFencedWriteResult> TryRenewLeaseAsync(
        AgentOutcomeReconciliationClaim claim,
        TimeSpan leaseDuration,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default)
    {
        return transactionRunner.ExecuteAsync(
            "Agent.NodeOutcomeReconciliationRenew",
            async (context, token) =>
            {
                var node = await LockReconciliationNodeAsync(context, claim, token);
                if (node is null)
                {
                    return Attempt(AgentFencedWriteResult.StaleFence);
                }

                node.RenewReconciliationLease(
                    claim.TaskFencingToken,
                    claim.NodeFencingToken,
                    claim.ReconciliationFencingToken,
                    claim.ReconciliationLeaseId,
                    nowUtc,
                    leaseDuration);
                return Attempt(AgentFencedWriteResult.Succeeded);
            },
            cancellationToken);
    }

    public Task<AgentFencedWriteResult> CommitSucceededAsync(
        AgentOutcomeReconciliationSuccessCheckpoint checkpoint,
        CancellationToken cancellationToken = default)
    {
        return ExecuteLockedAsync(
            "Agent.NodeOutcomeReconciliationSucceeded",
            checkpoint.Claim,
            async (context, locked, token) =>
            {
                if (!MatchesSuccessAuthority(checkpoint) ||
                    !MatchesDecisionDigest(
                        checkpoint.Claim,
                        AgentOutcomeReconciliationResolution.ConfirmedSucceeded,
                        checkpoint.ReasonCode,
                        checkpoint.ActorType,
                        checkpoint.ActorIdHash,
                        checkpoint.Evidence.EnvelopeDigest,
                        checkpoint.ProviderReceiptHash,
                        checkpoint.DecidedAtUtc,
                        checkpoint.DecisionDigest))
                {
                    return Attempt(AgentFencedWriteResult.StateConflict);
                }

                var duplicate = await context.AgentNodeReconciliationDecisions.AsNoTracking().AnyAsync(
                    decision => decision.NodeRunId == checkpoint.Claim.NodeRun.Id &&
                                decision.ReconciliationFencingToken == checkpoint.Claim.ReconciliationFencingToken,
                    token);
                if (duplicate)
                {
                    return Attempt(AgentFencedWriteResult.Duplicate);
                }

                if (!AgentNodeRunBudgetSettlement.TrySettle(
                        locked.Attempt,
                        locked.Node,
                        checkpoint.Claim.NodeFencingToken,
                        checkpoint.Usage,
                        checkpoint.DecidedAtUtc,
                        conservativelyConsumed: false))
                {
                    return Attempt(AgentFencedWriteResult.StateConflict);
                }

                var evidenceSetDigest = await AgentEvidenceSetDigest.ComputeAsync(
                    context,
                    checkpoint.Claim.RunAttemptId,
                    checkpoint.Evidence,
                    token);
                context.AgentEvidenceRecords.Add(checkpoint.Evidence);
                context.AgentRunUsageLedgerEntries.Add(checkpoint.Usage);
                context.AgentNodeReconciliationDecisions.Add(new AgentNodeReconciliationDecision(
                    checkpoint.Claim.TaskId,
                    checkpoint.Claim.RunAttemptId,
                    checkpoint.Claim.NodeRun.Id,
                    checkpoint.Claim.TaskFencingToken,
                    checkpoint.Claim.NodeFencingToken,
                    checkpoint.Claim.ReconciliationFencingToken,
                    AgentOutcomeReconciliationResolution.ConfirmedSucceeded,
                    checkpoint.ReasonCode,
                    checkpoint.ActorType,
                    checkpoint.ActorIdHash,
                    checkpoint.Evidence.EnvelopeDigest,
                    checkpoint.ProviderReceiptHash,
                    checkpoint.DecisionDigest,
                    checkpoint.DecidedAtUtc));
                var cancellationRequested = locked.Node.ReconciliationPolicy?.StartsWith(
                    "cancellation-",
                    StringComparison.Ordinal) == true;
                locked.Node.CompleteReconciledCheckpoint(
                    checkpoint.Claim.TaskFencingToken,
                    checkpoint.Claim.NodeFencingToken,
                    checkpoint.Claim.ReconciliationFencingToken,
                    checkpoint.Claim.ReconciliationLeaseId,
                    checkpoint.Evidence.Id,
                    checkpoint.OutputDigest,
                    evidenceSetDigest,
                    checkpoint.ProviderReceiptHash,
                    checkpoint.ReasonCode,
                    checkpoint.DecisionDigest,
                    checkpoint.DecidedAtUtc);
                if (cancellationRequested)
                {
                    await CancelWorkflowAfterReconciledSuccessAsync(
                        context,
                        locked,
                        checkpoint.DecidedAtUtc,
                        token);
                    return Attempt(AgentFencedWriteResult.Succeeded);
                }

                await AgentNodeRunDependencyPromoter.PromoteAsync(
                    context,
                    checkpoint.Claim.RunAttemptId,
                    checkpoint.DecidedAtUtc,
                    token);
                ResumeWorkflow(locked, checkpoint.DecidedAtUtc);
                return Attempt(AgentFencedWriteResult.Succeeded);
            },
            cancellationToken);
    }

    public Task<AgentFencedWriteResult> CommitNegativeDecisionAsync(
        AgentOutcomeReconciliationNegativeDecision decision,
        CancellationToken cancellationToken = default)
    {
        return ExecuteLockedAsync(
            "Agent.NodeOutcomeReconciliationNegative",
            decision.Claim,
            async (context, locked, token) =>
            {
                if ((decision.Resolution is not AgentOutcomeReconciliationResolution.ConfirmedNotOccurred
                        and not AgentOutcomeReconciliationResolution.ConfirmedCancelled
                        and not AgentOutcomeReconciliationResolution.ManualAbandonedAsFailed
                        and not AgentOutcomeReconciliationResolution.ManualAbandonedAsCancelled) ||
                    (decision.AllowNodeRetry && decision.Resolution != AgentOutcomeReconciliationResolution.ConfirmedNotOccurred) ||
                    (decision.Resolution is (AgentOutcomeReconciliationResolution.ManualAbandonedAsFailed
                         or AgentOutcomeReconciliationResolution.ManualAbandonedAsCancelled) &&
                     !string.Equals(decision.ActorType, "Human", StringComparison.Ordinal)) ||
                    !MatchesDecisionDigest(
                        decision.Claim,
                        decision.Resolution,
                        decision.ReasonCode,
                        decision.ActorType,
                        decision.ActorIdHash,
                        decision.EvidenceDigest,
                        decision.ProviderReceiptHash,
                        decision.DecidedAtUtc,
                        decision.DecisionDigest))
                {
                    return Attempt(AgentFencedWriteResult.StateConflict);
                }

                if (!AgentNodeRunBudgetSettlement.TrySettle(
                        locked.Attempt,
                        locked.Node,
                        decision.Claim.NodeFencingToken,
                        usage: null,
                        nowUtc: decision.DecidedAtUtc,
                        conservativelyConsumed: true))
                {
                    return Attempt(AgentFencedWriteResult.StateConflict);
                }

                context.AgentNodeReconciliationDecisions.Add(new AgentNodeReconciliationDecision(
                    decision.Claim.TaskId,
                    decision.Claim.RunAttemptId,
                    decision.Claim.NodeRun.Id,
                    decision.Claim.TaskFencingToken,
                    decision.Claim.NodeFencingToken,
                    decision.Claim.ReconciliationFencingToken,
                    decision.Resolution,
                    decision.ReasonCode,
                    decision.ActorType,
                    decision.ActorIdHash,
                    decision.EvidenceDigest,
                    decision.ProviderReceiptHash,
                    decision.DecisionDigest,
                    decision.DecidedAtUtc));

                if (decision.Resolution is AgentOutcomeReconciliationResolution.ConfirmedCancelled
                    or AgentOutcomeReconciliationResolution.ManualAbandonedAsCancelled)
                {
                    locked.Node.ResolveReconciledCancelled(CreateResolutionInput(decision));
                    locked.Queue.Cancel(decision.DecidedAtUtc, decision.SafeMessage);
                    locked.Attempt.Cancel(decision.DecidedAtUtc, decision.SafeMessage);
                    locked.Task.Cancel(decision.DecidedAtUtc);
                    return Attempt(AgentFencedWriteResult.Succeeded);
                }

                locked.Node.ResolveReconciledNotOccurred(
                    CreateResolutionInput(decision),
                    decision.AllowNodeRetry,
                    decision.RetryAtUtc);
                if (decision.AllowNodeRetry)
                {
                    ResumeWorkflow(locked, decision.DecidedAtUtc);
                }
                else
                {
                    locked.Queue.MarkFailed(decision.ReasonCode, decision.SafeMessage, decision.DecidedAtUtc);
                    locked.Attempt.MarkFailed(decision.ReasonCode, decision.SafeMessage, decision.DecidedAtUtc);
                    locked.Task.Fail(decision.SafeMessage, decision.DecidedAtUtc);
                    locked.Task.ReleaseRunLease(decision.DecidedAtUtc, clearActiveAttempt: true);
                }

                return Attempt(AgentFencedWriteResult.Succeeded);
            },
            cancellationToken);
    }

    public Task<AgentFencedWriteResult> DeferAsync(
        AgentOutcomeReconciliationDeferral deferral,
        CancellationToken cancellationToken = default)
    {
        return ExecuteLockedAsync(
            "Agent.NodeOutcomeReconciliationDeferred",
            deferral.Claim,
            async (context, locked, token) =>
            {
                if (deferral.Resolution is not AgentOutcomeReconciliationResolution.StillUnknown
                    and not AgentOutcomeReconciliationResolution.ConflictingEvidence ||
                    !MatchesDecisionDigest(
                        deferral.Claim,
                        deferral.Resolution,
                        deferral.ReasonCode,
                        deferral.ActorType,
                        deferral.ActorIdHash,
                        deferral.EvidenceDigest,
                        deferral.ProviderReceiptHash,
                        deferral.DecidedAtUtc,
                        deferral.DecisionDigest))
                {
                    return Attempt(AgentFencedWriteResult.StateConflict);
                }

                context.AgentNodeReconciliationDecisions.Add(new AgentNodeReconciliationDecision(
                    deferral.Claim.TaskId,
                    deferral.Claim.RunAttemptId,
                    deferral.Claim.NodeRun.Id,
                    deferral.Claim.TaskFencingToken,
                    deferral.Claim.NodeFencingToken,
                    deferral.Claim.ReconciliationFencingToken,
                    deferral.Resolution,
                    deferral.ReasonCode,
                    deferral.ActorType,
                    deferral.ActorIdHash,
                    deferral.EvidenceDigest,
                    deferral.ProviderReceiptHash,
                    deferral.DecisionDigest,
                    deferral.DecidedAtUtc));
                locked.Node.DeferReconciliation(
                    deferral.Claim.TaskFencingToken,
                    deferral.Claim.NodeFencingToken,
                    deferral.Claim.ReconciliationFencingToken,
                    deferral.Claim.ReconciliationLeaseId,
                    deferral.DecidedAtUtc,
                    deferral.NextCheckAtUtc,
                    deferral.SafeMessage,
                    deferral.Resolution == AgentOutcomeReconciliationResolution.ConflictingEvidence);
                return Attempt(AgentFencedWriteResult.Succeeded);
            },
            cancellationToken);
    }

    private Task<AgentFencedWriteResult> ExecuteLockedAsync(
        string operationName,
        AgentOutcomeReconciliationClaim claim,
        Func<AiGatewayDbContext, LockedAuthority, CancellationToken,
            Task<AgentExecutionTransactionAttempt<AgentFencedWriteResult>>> action,
        CancellationToken cancellationToken)
    {
        return transactionRunner.ExecuteAsync(
            operationName,
            async (context, token) =>
            {
                var locked = await LockAllAsync(context, claim, token);
                return locked is null
                    ? Attempt(AgentFencedWriteResult.StaleFence)
                    : await action(context, locked, token);
            },
            cancellationToken);
    }

    private static async Task<LockedAuthority?> LockAllAsync(
        AiGatewayDbContext context,
        AgentOutcomeReconciliationClaim claim,
        CancellationToken cancellationToken)
    {
        var authority = await LockAuthorityAsync(
            context,
            claim.TaskId,
            claim.RunAttemptId,
            claim.QueueItemId,
            claim.TaskFencingToken,
            cancellationToken);
        if (authority is null)
        {
            return null;
        }

        var node = await LockReconciliationNodeAsync(context, claim, cancellationToken);
        return node is null
            ? null
            : new LockedAuthority(authority.Value.Task, authority.Value.Attempt, authority.Value.Queue, node);
    }

    private static async Task<AgentExecutionTransactionAttempt<AgentOutcomeReconciliationClaim?>> ClaimNodeAsync(
        AiGatewayDbContext context,
        AgentNodeRun node,
        string owner,
        TimeSpan leaseDuration,
        DateTimeOffset nowUtc,
        bool ignoreSchedule,
        CancellationToken cancellationToken)
    {
        var authority = await LockAuthorityAsync(
            context,
            node.TaskId,
            node.RunAttemptId,
            node.QueueItemId,
            node.TaskFencingToken,
            cancellationToken);
        if (authority is null)
        {
            return Attempt<AgentOutcomeReconciliationClaim?>(null);
        }

        node.ClaimReconciliation(owner, nowUtc, leaseDuration, ignoreSchedule);
        return Attempt<AgentOutcomeReconciliationClaim?>(new AgentOutcomeReconciliationClaim(
            node,
            node.TaskId,
            node.RunAttemptId,
            node.QueueItemId,
            node.TaskFencingToken,
            node.NodeFencingToken,
            node.ReconciliationFencingToken,
            node.ReconciliationLeaseId!.Value,
            node.ReconciliationLeaseExpiresAt!.Value,
            node.ReconciliationDeadlineAt!.Value));
    }

    private static async Task<(AgentTask Task, AgentTaskRunAttempt Attempt, AgentTaskRunQueueItem Queue)?> LockAuthorityAsync(
        AiGatewayDbContext context,
        AgentTaskId taskId,
        AgentTaskRunAttemptId runAttemptId,
        AgentTaskRunQueueItemId queueItemId,
        long taskFencingToken,
        CancellationToken cancellationToken)
    {
        var task = await AgentExecutionRowLock.ByIdAsync<AgentTask>(context, taskId.Value, cancellationToken);
        if (task is null || task.ActiveRunAttemptId != runAttemptId ||
            task.RunFencingToken != taskFencingToken || task.Status != AgentTaskStatus.ReconciliationRequired)
        {
            return null;
        }

        await context.Entry(task).Collection(candidate => candidate.Steps).LoadAsync(cancellationToken);
        var attempt = await AgentExecutionRowLock.ByIdAsync<AgentTaskRunAttempt>(
            context, runAttemptId.Value, cancellationToken);
        if (attempt is null || attempt.TaskId != taskId || attempt.TaskFencingToken != taskFencingToken ||
            attempt.Status != AgentTaskRunAttemptStatus.ReconciliationRequired)
        {
            return null;
        }

        var queue = await AgentExecutionRowLock.ByIdAsync<AgentTaskRunQueueItem>(
            context, queueItemId.Value, cancellationToken);
        return queue is null || queue.TaskId != taskId || queue.RunAttemptId != runAttemptId ||
               queue.TaskFencingToken != taskFencingToken || queue.Status != AgentTaskRunQueueStatus.Started
            ? null
            : (task, attempt, queue);
    }

    private static Task<AgentNodeRun?> LockReconciliationNodeAsync(
        AiGatewayDbContext context,
        AgentOutcomeReconciliationClaim claim,
        CancellationToken cancellationToken)
    {
        return context.AgentNodeRuns
            .FromSqlInterpolated($$"""
                SELECT node.*, node.xmin FROM aigateway.agent_node_runs AS node
                WHERE id = {{claim.NodeRun.Id.Value}}
                  AND task_id = {{claim.TaskId.Value}}
                  AND run_attempt_id = {{claim.RunAttemptId.Value}}
                  AND queue_item_id = {{claim.QueueItemId.Value}}
                  AND task_fencing_token = {{claim.TaskFencingToken}}
                  AND node_fencing_token = {{claim.NodeFencingToken}}
                  AND reconciliation_fencing_token = {{claim.ReconciliationFencingToken}}
                  AND reconciliation_lease_id = {{claim.ReconciliationLeaseId}}
                  AND status = 'OutcomeUnknown'
                FOR UPDATE
                """)
            .SingleOrDefaultAsync(cancellationToken);
    }

    private static bool MatchesSuccessAuthority(AgentOutcomeReconciliationSuccessCheckpoint checkpoint)
    {
        var evidence = checkpoint.Evidence;
        var usage = checkpoint.Usage;
        return evidence.TaskId == checkpoint.Claim.TaskId &&
               evidence.RunAttemptId == checkpoint.Claim.RunAttemptId &&
               evidence.NodeRunId == checkpoint.Claim.NodeRun.Id &&
               evidence.TaskFencingToken == checkpoint.Claim.TaskFencingToken &&
               evidence.NodeFencingToken == checkpoint.Claim.NodeFencingToken &&
               evidence.OutputDigest == checkpoint.OutputDigest &&
               usage.TaskId == checkpoint.Claim.TaskId &&
               usage.RunAttemptId == checkpoint.Claim.RunAttemptId &&
               usage.NodeRunId == checkpoint.Claim.NodeRun.Id &&
               usage.TaskFencingToken == checkpoint.Claim.TaskFencingToken &&
               usage.NodeFencingToken == checkpoint.Claim.NodeFencingToken;
    }

    private static bool MatchesDecisionDigest(
        AgentOutcomeReconciliationClaim claim,
        AgentOutcomeReconciliationResolution resolution,
        string reasonCode,
        string actorType,
        string actorIdHash,
        string? evidenceDigest,
        string? providerReceiptHash,
        DateTimeOffset decidedAtUtc,
        string decisionDigest)
    {
        var computed = AgentOutcomeReconciliationDecisionDigest.Compute(
            claim,
            resolution,
            reasonCode,
            actorType,
            actorIdHash,
            evidenceDigest,
            providerReceiptHash,
            decidedAtUtc);
        return string.Equals(computed, decisionDigest, StringComparison.Ordinal);
    }

    private static async Task CancelWorkflowAfterReconciledSuccessAsync(
        AiGatewayDbContext context,
        LockedAuthority authority,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        var nodes = await context.AgentNodeRuns
            .Where(node => node.RunAttemptId == authority.Attempt.Id && node.Id != authority.Node.Id)
            .ToListAsync(cancellationToken);
        foreach (var node in nodes.Where(node => !node.IsTerminal))
        {
            if (node.Status is not AgentNodeRunStatus.Pending
                and not AgentNodeRunStatus.Runnable
                and not AgentNodeRunStatus.WaitingApproval)
            {
                throw new InvalidOperationException(
                    "Cancellation reconciliation found another active or unknown NodeRun.");
            }

            node.CancelBeforeExecution(
                "Agent task cancellation completed after side-effect reconciliation.",
                nowUtc);
        }

        foreach (var step in authority.Task.Steps.Where(step =>
                     step.Status is not AgentStepStatus.Completed
                         and not AgentStepStatus.Failed
                         and not AgentStepStatus.Cancelled))
        {
            step.Cancel(nowUtc);
        }

        authority.Queue.Cancel(
            nowUtc,
            "Agent task cancellation completed after side-effect reconciliation.");
        authority.Attempt.Cancel(
            nowUtc,
            "Agent task cancellation completed after side-effect reconciliation.");
        authority.Task.Cancel(nowUtc);
    }

    private static void ResumeWorkflow(LockedAuthority authority, DateTimeOffset nowUtc)
    {
        authority.Queue.ResumeAfterReconciliationForReclaim(nowUtc);
        authority.Attempt.ResumeFromReconciliationForReclaim(nowUtc);
        authority.Task.ResumeFromReconciliation(nowUtc);
    }

    private static AgentExecutionTransactionAttempt<T> Attempt<T>(T result) => new(result);

    private static AgentNodeReconciliationResolutionInput CreateResolutionInput(
        AgentOutcomeReconciliationDecisionCommand decision)
    {
        return new AgentNodeReconciliationResolutionInput(
            decision.Claim.TaskFencingToken,
            decision.Claim.NodeFencingToken,
            decision.Claim.ReconciliationFencingToken,
            decision.Claim.ReconciliationLeaseId,
            decision.ReasonCode,
            decision.DecisionDigest,
            decision.SafeMessage,
            decision.DecidedAtUtc);
    }

    private static void ValidateClaimRequest(string owner, TimeSpan leaseDuration)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);
        if (leaseDuration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(leaseDuration));
        }
    }

    private sealed record LockedAuthority(
        AgentTask Task,
        AgentTaskRunAttempt Attempt,
        AgentTaskRunQueueItem Queue,
        AgentNodeRun Node);
}
