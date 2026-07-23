using AICopilot.Core.AiGateway.Runtime.AgentExecution;
using AICopilot.Core.AiGateway.Ids;
using AICopilot.EntityFrameworkCore.Transactions;
using AICopilot.Services.Contracts;
using Microsoft.EntityFrameworkCore;

namespace AICopilot.EntityFrameworkCore.Repository;

internal sealed class AgentNodeRunStore(
    AiGatewayDbContext dbContext,
    AgentExecutionTransactionRunner transactionRunner)
    : IAgentNodeRunStore
{
    public Task<IReadOnlyCollection<AgentNodeRun>> EnsureMaterializedAsync(
        DurableTaskClaim claim,
        AgentRunBudgetLimits taskBudget,
        IReadOnlyCollection<AgentNodeRunSeed> seeds,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default)
    {
        if (seeds.Count == 0)
        {
            throw new InvalidOperationException("Executable Plan v2 must materialize at least one NodeRun.");
        }

        if (seeds.Count > taskBudget.MaxNodes)
        {
            throw new InvalidOperationException("Plan v2 node count exceeds the immutable task budget.");
        }

        if (seeds.Sum(seed => (long)seed.Budget.MaxToolCalls) > taskBudget.MaxToolCalls ||
            seeds.Sum(seed => (long)seed.Budget.MaxModelCalls) > taskBudget.MaxModelCalls ||
            seeds.Sum(seed => (long)seed.Budget.MaxInputTokens) > taskBudget.MaxInputTokens ||
            seeds.Sum(seed => (long)seed.Budget.MaxOutputTokens) > taskBudget.MaxOutputTokens ||
            seeds.Sum(seed => (long)Math.Max(0, seed.MaxAttempts - 1)) > taskBudget.MaxRetries ||
            seeds.Sum(seed => (long)seed.Budget.MaxArtifactCount) > taskBudget.MaxArtifactCount ||
            seeds.Sum(seed => seed.Budget.MaxArtifactBytes) > taskBudget.MaxArtifactBytes ||
            seeds.Sum(seed => seed.Budget.MaxCostAmount) > taskBudget.MaxCostAmount)
        {
            throw new InvalidOperationException("Node upper-bound budgets exceed the immutable task budget.");
        }

        return transactionRunner.ExecuteAsync<IReadOnlyCollection<AgentNodeRun>>(
            "Agent.NodeRunMaterialize",
            async (context, token) =>
            {
                var taskFenceValid = await context.AgentTasks.AnyAsync(task =>
                    task.Id == claim.Task.Id &&
                    task.ActiveRunAttemptId == claim.RunAttempt.Id &&
                    task.RunFencingToken == claim.TaskFencingToken,
                    token);
                var attempt = await context.AgentTaskRunAttempts
                    .FromSqlInterpolated($$"""
                        SELECT attempt.*, attempt.xmin FROM aigateway.agent_task_run_attempts AS attempt
                        WHERE id = {{claim.RunAttempt.Id.Value}}
                          AND task_id = {{claim.Task.Id.Value}}
                          AND task_fencing_token = {{claim.TaskFencingToken}}
                        FOR UPDATE
                        """)
                    .SingleOrDefaultAsync(token);
                if (!taskFenceValid || attempt is null)
                {
                    throw new InvalidOperationException("NodeRun materialization rejected a stale task fence.");
                }

                attempt.InitializeBudget(taskBudget);

                var existing = await context.AgentNodeRuns
                    .Where(node => node.RunAttemptId == claim.RunAttempt.Id)
                    .OrderBy(node => node.CreatedAt)
                    .ThenBy(node => node.NodeId)
                    .ToListAsync(token);
                if (existing.Count > 0)
                {
                    var seedIds = seeds.Select(seed => seed.NodeId).Order(StringComparer.Ordinal).ToArray();
                    var existingIds = existing.Select(node => node.NodeId).Order(StringComparer.Ordinal).ToArray();
                    var seedsById = seeds.ToDictionary(seed => seed.NodeId, StringComparer.Ordinal);
                    if (!seedIds.SequenceEqual(existingIds, StringComparer.Ordinal) ||
                        existing.Any(node =>
                        {
                            var seed = seedsById[node.NodeId];
                            return node.PlanDigest != seed.PlanDigest ||
                                   node.ExecutionSnapshotDigest != seed.ExecutionSnapshotDigest ||
                                   node.NodeKind != seed.NodeKind ||
                                   node.ToolCode != seed.ToolCode ||
                                   node.DependenciesJson != seed.DependenciesJson ||
                                   node.InputJson != seed.InputJson ||
                                   node.InputDigest != seed.InputDigest ||
                                   node.OutputSchemaRef != seed.OutputSchemaRef ||
                                   node.IsRequired != seed.IsRequired ||
                                   node.RequiresApproval != seed.RequiresApproval ||
                                   node.SideEffectClass != seed.SideEffectClass ||
                                   node.IdempotencyKeyHash != seed.IdempotencyKeyHash ||
                                   node.MaxAttempts != seed.MaxAttempts ||
                                   node.TimeoutSeconds != seed.TimeoutSeconds ||
                                   node.MaxToolCalls != seed.Budget.MaxToolCalls ||
                                   node.MaxModelCalls != seed.Budget.MaxModelCalls ||
                                   node.MaxInputTokens != seed.Budget.MaxInputTokens ||
                                   node.MaxOutputTokens != seed.Budget.MaxOutputTokens ||
                                   node.MaxCostAmount != seed.Budget.MaxCostAmount ||
                                   node.MaxArtifactCount != seed.Budget.MaxArtifactCount ||
                                   node.MaxArtifactBytes != seed.Budget.MaxArtifactBytes ||
                                   node.JoinPolicy != seed.JoinPolicy;
                        }))
                    {
                        throw new InvalidOperationException(
                            "Existing NodeRun topology does not match the immutable Plan v2 snapshot.");
                    }

                    foreach (var node in existing.Where(node => !node.IsTerminal))
                    {
                        node.BindTaskClaim(
                            claim.QueueItem.Id,
                            claim.TaskFencingToken,
                            nowUtc);
                    }

                    return new AgentExecutionTransactionAttempt<IReadOnlyCollection<AgentNodeRun>>(existing);
                }

                var materialized = new List<AgentNodeRun>(seeds.Count);
                foreach (var seed in seeds.OrderBy(seed => seed.NodeId, StringComparer.Ordinal))
                {
                    if (seed.RequiresApproval && seed.IsInitiallyRunnable)
                    {
                        throw new InvalidOperationException("Approval-gated NodeRun cannot be initially runnable.");
                    }

                    var node = new AgentNodeRun(
                        claim.Task.Id,
                        claim.RunAttempt.Id,
                        claim.QueueItem.Id,
                        seed.PlanDigest,
                        seed.ExecutionSnapshotDigest,
                        seed.NodeId,
                        seed.NodeKind,
                        seed.ToolCode,
                        seed.DependenciesJson,
                        seed.InputJson,
                        seed.InputDigest,
                        seed.OutputSchemaRef,
                        seed.IsRequired,
                        seed.RequiresApproval,
                        seed.SideEffectClass,
                        seed.IdempotencyKeyHash,
                        seed.MaxAttempts,
                        seed.TimeoutSeconds,
                        seed.Budget,
                        seed.JoinPolicy,
                        nowUtc);
                    if (seed.IsInitiallyRunnable)
                    {
                        node.MakeRunnable(nowUtc);
                    }

                    node.BindTaskClaim(
                        claim.QueueItem.Id,
                        claim.TaskFencingToken,
                        nowUtc);

                    context.AgentNodeRuns.Add(node);
                    materialized.Add(node);
                }

                return new AgentExecutionTransactionAttempt<IReadOnlyCollection<AgentNodeRun>>(materialized);
            },
            cancellationToken);
    }

    public async Task<IReadOnlyCollection<AgentNodeRun>> ListByAttemptAsync(
        AgentTaskRunAttemptId runAttemptId,
        CancellationToken cancellationToken = default)
    {
        return await dbContext.AgentNodeRuns
            .AsNoTracking()
            .Where(node => node.RunAttemptId == runAttemptId)
            .OrderBy(node => node.CreatedAt)
            .ThenBy(node => node.NodeId)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyCollection<AgentEvidenceRecord>> ListEvidenceByAttemptAsync(
        AgentTaskRunAttemptId runAttemptId,
        CancellationToken cancellationToken = default)
    {
        return await dbContext.AgentEvidenceRecords
            .AsNoTracking()
            .Where(evidence => evidence.RunAttemptId == runAttemptId && !evidence.IsRevoked)
            .OrderBy(evidence => evidence.CreatedAt)
            .ThenBy(evidence => evidence.NodeId)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyCollection<AgentRunUsageLedgerEntry>> ListUsageByAttemptAsync(
        AgentTaskRunAttemptId runAttemptId,
        CancellationToken cancellationToken = default)
    {
        return await dbContext.AgentRunUsageLedgerEntries
            .AsNoTracking()
            .Where(usage => usage.RunAttemptId == runAttemptId)
            .OrderBy(usage => usage.CreatedAt)
            .ThenBy(usage => usage.Id)
            .ToListAsync(cancellationToken);
    }

    public Task<AgentFencedWriteResult> TryReleaseApprovalAsync(
        AgentNodeRunId nodeRunId,
        AgentTaskRunAttemptId runAttemptId,
        long taskFencingToken,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default)
    {
        return transactionRunner.ExecuteAsync(
            "Agent.NodeRunReleaseApproval",
            async (context, token) =>
            {
                var authorityValid = await context.AgentTasks.AnyAsync(task =>
                    task.ActiveRunAttemptId == runAttemptId &&
                    task.RunFencingToken == taskFencingToken,
                    token);
                if (!authorityValid)
                {
                    return new AgentExecutionTransactionAttempt<AgentFencedWriteResult>(
                        AgentFencedWriteResult.StaleFence);
                }

                var node = await context.AgentNodeRuns
                    .FromSqlInterpolated($$"""
                        SELECT node.*, node.xmin FROM aigateway.agent_node_runs AS node
                        WHERE id = {{nodeRunId.Value}}
                          AND run_attempt_id = {{runAttemptId.Value}}
                          AND status = 'WaitingApproval'
                        FOR UPDATE
                        """)
                    .SingleOrDefaultAsync(token);
                if (node is null)
                {
                    return new AgentExecutionTransactionAttempt<AgentFencedWriteResult>(
                        AgentFencedWriteResult.StateConflict);
                }

                node.MakeRunnable(nowUtc);
                return new AgentExecutionTransactionAttempt<AgentFencedWriteResult>(
                    AgentFencedWriteResult.Succeeded);
            },
            cancellationToken);
    }
}
