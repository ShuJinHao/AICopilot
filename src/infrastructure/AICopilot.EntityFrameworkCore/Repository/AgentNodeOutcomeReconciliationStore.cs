using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
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
        string owner,
        TimeSpan leaseDuration,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);
        if (leaseDuration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(leaseDuration));
        }

        return transactionRunner.ExecuteAsync(
            "Agent.NodeOutcomeReconciliationClaim",
            async (context, token) =>
            {
                var node = await context.AgentNodeRuns
                    .FromSqlInterpolated($$"""
                        SELECT *
                        FROM aigateway.agent_node_runs
                        WHERE status = 'OutcomeUnknown'
                          AND next_attempt_at <= {{nowUtc}}
                          AND (reconciliation_lease_expires_at IS NULL OR reconciliation_lease_expires_at <= {{nowUtc}})
                        ORDER BY requires_manual_resolution, next_attempt_at, created_at, id
                        FOR UPDATE SKIP LOCKED
                        LIMIT 1
                        """)
                    .SingleOrDefaultAsync(token);
                if (node is null)
                {
                    return Attempt<AgentOutcomeReconciliationClaim?>(null);
                }

                return await ClaimNodeAsync(context, node, owner, leaseDuration, nowUtc, ignoreSchedule: false, token);
            },
            cancellationToken);
    }

    public Task<AgentOutcomeReconciliationClaim?> TryClaimAsync(
        AgentNodeRunId nodeRunId,
        string owner,
        TimeSpan leaseDuration,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);
        if (leaseDuration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(leaseDuration));
        }

        return transactionRunner.ExecuteAsync(
            "Agent.NodeOutcomeReconciliationClaimById",
            async (context, token) =>
            {
                var node = await context.AgentNodeRuns
                    .FromSqlInterpolated($$"""
                        SELECT *
                        FROM aigateway.agent_node_runs
                        WHERE id = {{nodeRunId.Value}}
                          AND status = 'OutcomeUnknown'
                          AND (reconciliation_lease_expires_at IS NULL OR reconciliation_lease_expires_at <= {{nowUtc}})
                        FOR UPDATE SKIP LOCKED
                        """)
                    .SingleOrDefaultAsync(token);
                return node is null
                    ? Attempt<AgentOutcomeReconciliationClaim?>(null)
                    : await ClaimNodeAsync(context, node, owner, leaseDuration, nowUtc, ignoreSchedule: true, token);
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
        return transactionRunner.ExecuteAsync(
            "Agent.NodeOutcomeReconciliationSucceeded",
            async (context, token) =>
            {
                var locked = await LockAllAsync(context, checkpoint.Claim, token);
                if (locked is null)
                {
                    return Attempt(AgentFencedWriteResult.StaleFence);
                }

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

                if (!TrySettleBudget(
                        locked.Value.Attempt,
                        locked.Value.Node,
                        checkpoint.Usage,
                        checkpoint.DecidedAtUtc,
                        conservativelyConsumed: false))
                {
                    return Attempt(AgentFencedWriteResult.StateConflict);
                }

                var evidenceSetDigest = await ComputeEvidenceSetDigestAsync(
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
                var cancellationRequested = locked.Value.Node.ReconciliationPolicy?.StartsWith(
                    "cancellation-",
                    StringComparison.Ordinal) == true;
                locked.Value.Node.CompleteReconciledCheckpoint(
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
                        locked.Value,
                        checkpoint.DecidedAtUtc,
                        token);
                    return Attempt(AgentFencedWriteResult.Succeeded);
                }

                await PromoteRunnableDependentsAsync(
                    context,
                    checkpoint.Claim.RunAttemptId,
                    checkpoint.DecidedAtUtc,
                    token);
                ResumeWorkflow(locked.Value, checkpoint.DecidedAtUtc);
                return Attempt(AgentFencedWriteResult.Succeeded);
            },
            cancellationToken);
    }

    public Task<AgentFencedWriteResult> CommitNegativeDecisionAsync(
        AgentOutcomeReconciliationNegativeDecision decision,
        CancellationToken cancellationToken = default)
    {
        return transactionRunner.ExecuteAsync(
            "Agent.NodeOutcomeReconciliationNegative",
            async (context, token) =>
            {
                var locked = await LockAllAsync(context, decision.Claim, token);
                if (locked is null)
                {
                    return Attempt(AgentFencedWriteResult.StaleFence);
                }

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

                if (!TrySettleBudget(
                        locked.Value.Attempt,
                        locked.Value.Node,
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
                    locked.Value.Node.ResolveReconciledCancelled(
                        decision.Claim.TaskFencingToken,
                        decision.Claim.NodeFencingToken,
                        decision.Claim.ReconciliationFencingToken,
                        decision.Claim.ReconciliationLeaseId,
                        decision.ReasonCode,
                        decision.DecisionDigest,
                        decision.SafeMessage,
                        decision.DecidedAtUtc);
                    locked.Value.Queue.Cancel(decision.DecidedAtUtc, decision.SafeMessage);
                    locked.Value.Attempt.Cancel(decision.DecidedAtUtc, decision.SafeMessage);
                    locked.Value.Task.Cancel(decision.DecidedAtUtc);
                    return Attempt(AgentFencedWriteResult.Succeeded);
                }

                locked.Value.Node.ResolveReconciledNotOccurred(
                    decision.Claim.TaskFencingToken,
                    decision.Claim.NodeFencingToken,
                    decision.Claim.ReconciliationFencingToken,
                    decision.Claim.ReconciliationLeaseId,
                    decision.ReasonCode,
                    decision.DecisionDigest,
                    decision.SafeMessage,
                    decision.AllowNodeRetry,
                    decision.DecidedAtUtc,
                    decision.RetryAtUtc);
                if (decision.AllowNodeRetry)
                {
                    ResumeWorkflow(locked.Value, decision.DecidedAtUtc);
                }
                else
                {
                    locked.Value.Queue.MarkFailed(decision.ReasonCode, decision.SafeMessage, decision.DecidedAtUtc);
                    locked.Value.Attempt.MarkFailed(decision.ReasonCode, decision.SafeMessage, decision.DecidedAtUtc);
                    locked.Value.Task.Fail(decision.SafeMessage, decision.DecidedAtUtc);
                    locked.Value.Task.ReleaseRunLease(decision.DecidedAtUtc, clearActiveAttempt: true);
                }

                return Attempt(AgentFencedWriteResult.Succeeded);
            },
            cancellationToken);
    }

    public Task<AgentFencedWriteResult> DeferAsync(
        AgentOutcomeReconciliationDeferral deferral,
        CancellationToken cancellationToken = default)
    {
        return transactionRunner.ExecuteAsync(
            "Agent.NodeOutcomeReconciliationDeferred",
            async (context, token) =>
            {
                var locked = await LockAllAsync(context, deferral.Claim, token);
                if (locked is null)
                {
                    return Attempt(AgentFencedWriteResult.StaleFence);
                }

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
                locked.Value.Node.DeferReconciliation(
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
        var task = await context.AgentTasks
            .FromSqlInterpolated($$"""
                SELECT * FROM aigateway.agent_tasks
                WHERE id = {{taskId.Value}}
                  AND active_run_attempt_id = {{runAttemptId.Value}}
                  AND run_fencing_token = {{taskFencingToken}}
                  AND status = 'ReconciliationRequired'
                FOR UPDATE
                """)
            .SingleOrDefaultAsync(cancellationToken);
        if (task is null)
        {
            return null;
        }

        await context.Entry(task).Collection(candidate => candidate.Steps).LoadAsync(cancellationToken);
        var attempt = await context.AgentTaskRunAttempts
            .FromSqlInterpolated($$"""
                SELECT * FROM aigateway.agent_task_run_attempts
                WHERE id = {{runAttemptId.Value}}
                  AND task_id = {{taskId.Value}}
                  AND task_fencing_token = {{taskFencingToken}}
                  AND status = 'ReconciliationRequired'
                FOR UPDATE
                """)
            .SingleOrDefaultAsync(cancellationToken);
        if (attempt is null)
        {
            return null;
        }

        var queue = await context.AgentTaskRunQueueItems
            .FromSqlInterpolated($$"""
                SELECT * FROM aigateway.agent_task_run_queue_items
                WHERE id = {{queueItemId.Value}}
                  AND task_id = {{taskId.Value}}
                  AND run_attempt_id = {{runAttemptId.Value}}
                  AND task_fencing_token = {{taskFencingToken}}
                  AND status = 'Started'
                FOR UPDATE
                """)
            .SingleOrDefaultAsync(cancellationToken);
        return queue is null ? null : (task, attempt, queue);
    }

    private static Task<AgentNodeRun?> LockReconciliationNodeAsync(
        AiGatewayDbContext context,
        AgentOutcomeReconciliationClaim claim,
        CancellationToken cancellationToken)
    {
        return context.AgentNodeRuns
            .FromSqlInterpolated($$"""
                SELECT * FROM aigateway.agent_node_runs
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
        var canonical = string.Join('|',
            claim.TaskId.Value.ToString("D"),
            claim.RunAttemptId.Value.ToString("D"),
            claim.NodeRun.Id.Value.ToString("D"),
            claim.TaskFencingToken,
            claim.NodeFencingToken,
            claim.ReconciliationFencingToken,
            resolution,
            reasonCode.Trim(),
            actorType.Trim(),
            actorIdHash.Trim(),
            evidenceDigest?.Trim() ?? string.Empty,
            providerReceiptHash?.Trim() ?? string.Empty,
            decidedAtUtc.ToUniversalTime().ToString("O"));
        var computed = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)))
            .ToLowerInvariant();
        return string.Equals(computed, decisionDigest, StringComparison.Ordinal);
    }

    private static async Task<string> ComputeEvidenceSetDigestAsync(
        AiGatewayDbContext context,
        AgentTaskRunAttemptId runAttemptId,
        AgentEvidenceRecord current,
        CancellationToken cancellationToken)
    {
        var components = await context.AgentEvidenceRecords
            .AsNoTracking()
            .Where(evidence => evidence.RunAttemptId == runAttemptId && !evidence.IsRevoked)
            .Select(evidence => new
            {
                evidence.Id,
                evidence.NodeId,
                evidence.EnvelopeDigest,
                evidence.OutputDigest
            })
            .ToListAsync(cancellationToken);
        components.Add(new
        {
            current.Id,
            current.NodeId,
            current.EnvelopeDigest,
            current.OutputDigest
        });
        var canonical = string.Join(
            "\n",
            components
                .OrderBy(component => component.NodeId, StringComparer.Ordinal)
                .ThenBy(component => component.Id.Value)
                .Select(component =>
                    $"{component.Id.Value:D}|{component.NodeId}|{component.EnvelopeDigest}|{component.OutputDigest}"));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)))
            .ToLowerInvariant();
    }

    private static async Task PromoteRunnableDependentsAsync(
        AiGatewayDbContext context,
        AgentTaskRunAttemptId runAttemptId,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        var nodes = await context.AgentNodeRuns
            .Where(node => node.RunAttemptId == runAttemptId)
            .ToListAsync(cancellationToken);
        var statusByNodeId = nodes.ToDictionary(node => node.NodeId, node => node.Status, StringComparer.Ordinal);
        foreach (var candidate in nodes.Where(node => node.Status == AgentNodeRunStatus.Pending))
        {
            var dependencies = JsonSerializer.Deserialize<string[]>(candidate.DependenciesJson) ?? [];
            if (dependencies.All(dependency =>
                    statusByNodeId.TryGetValue(dependency, out var status) &&
                    status == AgentNodeRunStatus.Succeeded))
            {
                candidate.MakeRunnable(nowUtc);
            }
        }
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

    private static bool TrySettleBudget(
        AgentTaskRunAttempt attempt,
        AgentNodeRun node,
        AgentRunUsageLedgerEntry? usage,
        DateTimeOffset nowUtc,
        bool conservativelyConsumed)
    {
        if (usage is not null &&
            !string.Equals(
                usage.CostCurrency,
                attempt.BudgetCostCurrency,
                StringComparison.Ordinal))
        {
            return false;
        }

        AgentRunBudgetCharge reservation;
        try
        {
            reservation = node.GetActiveBudgetReservation(node.NodeFencingToken);
        }
        catch (InvalidOperationException)
        {
            return false;
        }

        var actual = usage is null
            ? AgentRunBudgetCharge.Zero
            : new AgentRunBudgetCharge(
                usage.ToolCalls,
                usage.ModelCalls,
                usage.InputTokens,
                usage.OutputTokens,
                usage.ElapsedMilliseconds,
                usage.CostAmount,
                reservation.RetryCount,
                usage.ArtifactCount,
                usage.ArtifactBytes);
        if (!attempt.TrySettleBudget(reservation, actual, conservativelyConsumed))
        {
            return false;
        }

        node.CloseBudgetReservation(
            node.NodeFencingToken,
            conservativelyConsumed
                ? AgentBudgetReservationStatus.ConservativelyConsumed
                : AgentBudgetReservationStatus.Settled,
            nowUtc);
        return true;
    }

    private static AgentExecutionTransactionAttempt<T> Attempt<T>(T result) => new(result);

    private sealed record LockedAuthority(
        AgentTask Task,
        AgentTaskRunAttempt Attempt,
        AgentTaskRunQueueItem Queue,
        AgentNodeRun Node);
}
