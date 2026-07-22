using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AICopilot.Core.AiGateway.Aggregates.AgentTasks;
using AICopilot.Core.AiGateway.Aggregates.Approvals;
using AICopilot.Core.AiGateway.Aggregates.Artifacts;
using AICopilot.Core.AiGateway.Aggregates.Tools;
using AICopilot.Core.AiGateway.Ids;
using AICopilot.Core.AiGateway.Runtime.AgentExecution;
using AICopilot.EntityFrameworkCore.Transactions;
using AICopilot.Services.Contracts;
using Microsoft.EntityFrameworkCore;

namespace AICopilot.EntityFrameworkCore.Repository;

internal sealed class AgentNodeCheckpointStore(
    AgentExecutionTransactionRunner transactionRunner)
    : IAgentNodeCheckpointStore
{
    public Task<AgentFencedWriteResult> CommitSuccessAsync(
        AgentNodeSuccessCheckpoint checkpoint,
        CancellationToken cancellationToken = default)
    {
        return transactionRunner.ExecuteAsync(
            "Agent.NodeCheckpointSuccess",
            async (context, token) =>
            {
                var authority = await LockAuthorityAsync(
                    context,
                    checkpoint.TaskId.Value,
                    checkpoint.RunAttemptId.Value,
                    checkpoint.TaskFencingToken,
                    token);
                if (authority is null)
                {
                    return Stale();
                }

                var node = await LockNodeAsync(
                    context,
                    checkpoint.NodeRunId.Value,
                    checkpoint.RunAttemptId.Value,
                    checkpoint.TaskFencingToken,
                    checkpoint.NodeFencingToken,
                    AgentNodeRunStatus.Running,
                    token);
                if (node is null)
                {
                    var duplicate = await context.AgentEvidenceRecords.AsNoTracking().AnyAsync(evidence =>
                        evidence.NodeRunId == checkpoint.NodeRunId &&
                        evidence.NodeFencingToken == checkpoint.NodeFencingToken &&
                        evidence.EnvelopeDigest == checkpoint.Evidence.EnvelopeDigest,
                        token);
                    return new AgentExecutionTransactionAttempt<AgentFencedWriteResult>(
                        duplicate ? AgentFencedWriteResult.Duplicate : AgentFencedWriteResult.StaleFence);
                }

                if (!MatchesCheckpointAuthority(checkpoint, node))
                {
                    return new AgentExecutionTransactionAttempt<AgentFencedWriteResult>(
                        AgentFencedWriteResult.StateConflict);
                }

                FinalizationAuthority? finalizationAuthority = null;
                if (checkpoint.Finalization is not null)
                {
                    finalizationAuthority = await LockAndValidateFinalizationAsync(
                        context,
                        authority.Value.Task,
                        authority.Value.Attempt,
                        node,
                        checkpoint,
                        token);
                    if (finalizationAuthority is null)
                    {
                        return new AgentExecutionTransactionAttempt<AgentFencedWriteResult>(
                            AgentFencedWriteResult.StateConflict);
                    }
                }

                if (!TrySettleBudget(
                        authority.Value.Attempt,
                        node,
                        checkpoint.NodeFencingToken,
                        checkpoint.Usage,
                        checkpoint.CompletedAtUtc,
                        conservativelyConsumed: false))
                {
                    return new AgentExecutionTransactionAttempt<AgentFencedWriteResult>(
                        AgentFencedWriteResult.StateConflict);
                }

                var evidenceSetDigest = await ComputeEvidenceSetDigestAsync(
                    context,
                    checkpoint.RunAttemptId,
                    checkpoint.Evidence,
                    token);
                context.AgentEvidenceRecords.Add(checkpoint.Evidence);
                context.AgentRunUsageLedgerEntries.Add(checkpoint.Usage);
                if (finalizationAuthority is not null)
                {
                    ApplyFinalization(
                        context,
                        authority.Value.Task,
                        authority.Value.Attempt,
                        finalizationAuthority,
                        checkpoint);
                }

                node.CompleteCheckpoint(
                    checkpoint.TaskFencingToken,
                    checkpoint.NodeFencingToken,
                    checkpoint.Evidence.Id,
                    checkpoint.OutputDigest,
                    evidenceSetDigest,
                    checkpoint.ProviderOperationCode,
                    checkpoint.ProviderReceiptHash,
                    checkpoint.CompletedAtUtc);

                await PromoteRunnableDependentsAsync(
                    context,
                    checkpoint.RunAttemptId,
                    checkpoint.CompletedAtUtc,
                    token);
                return new AgentExecutionTransactionAttempt<AgentFencedWriteResult>(
                    AgentFencedWriteResult.Succeeded);
            },
            cancellationToken);
    }

    public Task<AgentFencedWriteResult> CommitFailureAsync(
        AgentNodeFailureCheckpoint checkpoint,
        CancellationToken cancellationToken = default)
    {
        return transactionRunner.ExecuteAsync(
            "Agent.NodeCheckpointFailure",
            async (context, token) =>
            {
                var authority = await LockAuthorityAsync(
                    context,
                    checkpoint.TaskId.Value,
                    checkpoint.RunAttemptId.Value,
                    checkpoint.TaskFencingToken,
                    token);
                if (authority is null)
                {
                    return Stale();
                }

                var node = await LockNodeAsync(
                    context,
                    checkpoint.NodeRunId.Value,
                    checkpoint.RunAttemptId.Value,
                    checkpoint.TaskFencingToken,
                    checkpoint.NodeFencingToken,
                    expectedStatus: null,
                    token);
                if (node is null ||
                    node.Status is not AgentNodeRunStatus.Claimed and not AgentNodeRunStatus.Running)
                {
                    return Stale();
                }

                if (!MatchesUsageAuthority(checkpoint, node) ||
                    !TrySettleBudget(
                        authority.Value.Attempt,
                        node,
                        checkpoint.NodeFencingToken,
                        checkpoint.Usage,
                        checkpoint.FailedAtUtc,
                        conservativelyConsumed: false))
                {
                    return new AgentExecutionTransactionAttempt<AgentFencedWriteResult>(
                        AgentFencedWriteResult.StateConflict);
                }

                context.AgentRunUsageLedgerEntries.Add(checkpoint.Usage);
                node.Fail(
                    checkpoint.TaskFencingToken,
                    checkpoint.NodeFencingToken,
                    checkpoint.FailureCode,
                    checkpoint.SafeMessage,
                    checkpoint.FailedAtUtc,
                    checkpoint.RetryAtUtc);
                return new AgentExecutionTransactionAttempt<AgentFencedWriteResult>(
                    AgentFencedWriteResult.Succeeded);
            },
            cancellationToken);
    }

    public Task<AgentFencedWriteResult> CommitOutcomeUnknownAsync(
        AgentNodeOutcomeUnknownCheckpoint checkpoint,
        CancellationToken cancellationToken = default)
    {
        return transactionRunner.ExecuteAsync(
            "Agent.NodeCheckpointOutcomeUnknown",
            async (context, token) =>
            {
                var authority = await LockAuthorityAsync(
                    context,
                    checkpoint.TaskId.Value,
                    checkpoint.RunAttemptId.Value,
                    checkpoint.TaskFencingToken,
                    token);
                if (authority is null)
                {
                    return Stale();
                }

                var node = await LockNodeAsync(
                    context,
                    checkpoint.NodeRunId.Value,
                    checkpoint.RunAttemptId.Value,
                    checkpoint.TaskFencingToken,
                    checkpoint.NodeFencingToken,
                    AgentNodeRunStatus.Running,
                    token);
                if (node is null)
                {
                    return Stale();
                }

                node.MarkOutcomeUnknown(
                    checkpoint.TaskFencingToken,
                    checkpoint.NodeFencingToken,
                    checkpoint.ProviderOperationCode,
                    checkpoint.ProviderReceiptHash,
                    checkpoint.ReconciliationPolicy,
                    checkpoint.LastConfirmedStage,
                    checkpoint.IntegrityStatus,
                    checkpoint.SafeMessage,
                    checkpoint.RecordedAtUtc,
                    checkpoint.NextCheckAtUtc,
                    checkpoint.ReconciliationDeadlineAtUtc);
                authority.Value.Task.RequireReconciliation(checkpoint.RecordedAtUtc);
                authority.Value.Attempt.RequireReconciliation(
                    checkpoint.RecordedAtUtc,
                    checkpoint.SafeMessage);
                return new AgentExecutionTransactionAttempt<AgentFencedWriteResult>(
                    AgentFencedWriteResult.Succeeded);
            },
            cancellationToken);
    }

    private static bool MatchesCheckpointAuthority(
        AgentNodeSuccessCheckpoint checkpoint,
        AgentNodeRun node)
    {
        var evidence = checkpoint.Evidence;
        var usage = checkpoint.Usage;
        return node.TaskId == checkpoint.TaskId &&
               evidence.TaskId == checkpoint.TaskId &&
               evidence.RunAttemptId == checkpoint.RunAttemptId &&
               evidence.NodeRunId == checkpoint.NodeRunId &&
               evidence.NodeId == node.NodeId &&
               evidence.TaskFencingToken == checkpoint.TaskFencingToken &&
               evidence.NodeFencingToken == checkpoint.NodeFencingToken &&
               evidence.OutputDigest == checkpoint.OutputDigest &&
               usage.TaskId == checkpoint.TaskId &&
               usage.RunAttemptId == checkpoint.RunAttemptId &&
               usage.NodeRunId == checkpoint.NodeRunId &&
               usage.TaskFencingToken == checkpoint.TaskFencingToken &&
               usage.NodeFencingToken == checkpoint.NodeFencingToken;
    }

    private static bool MatchesUsageAuthority(
        AgentNodeFailureCheckpoint checkpoint,
        AgentNodeRun node)
    {
        var usage = checkpoint.Usage;
        return node.TaskId == checkpoint.TaskId &&
               usage.TaskId == checkpoint.TaskId &&
               usage.RunAttemptId == checkpoint.RunAttemptId &&
               usage.NodeRunId == checkpoint.NodeRunId &&
               usage.TaskFencingToken == checkpoint.TaskFencingToken &&
               usage.NodeFencingToken == checkpoint.NodeFencingToken;
    }

    private static async Task<FinalizationAuthority?> LockAndValidateFinalizationAsync(
        AiGatewayDbContext context,
        AgentTask task,
        AgentTaskRunAttempt attempt,
        AgentNodeRun node,
        AgentNodeSuccessCheckpoint checkpoint,
        CancellationToken cancellationToken)
    {
        var mutation = checkpoint.Finalization!;
        var workspace = await context.ArtifactWorkspaces
            .FromSqlInterpolated($$"""
                SELECT * FROM aigateway.artifact_workspaces
                WHERE id = {{mutation.WorkspaceId.Value}}
                FOR UPDATE
                """)
            .SingleOrDefaultAsync(cancellationToken);
        if (workspace is null)
        {
            return null;
        }

        var artifacts = await context.Set<Artifact>()
            .FromSqlInterpolated($$"""
                SELECT * FROM aigateway.artifacts
                WHERE workspace_id = {{mutation.WorkspaceId.Value}}
                ORDER BY id
                FOR UPDATE
                """)
            .ToListAsync(cancellationToken);
        var steps = await context.Set<AgentStep>()
            .FromSqlInterpolated($$"""
                SELECT * FROM aigateway.agent_steps
                WHERE task_id = {{task.Id.Value}}
                ORDER BY step_index
                FOR UPDATE
                """)
            .ToListAsync(cancellationToken);
        var approval = await context.ApprovalRequests
            .FromSqlInterpolated($$"""
                SELECT * FROM aigateway.approval_requests
                WHERE id = {{mutation.ApprovalRequestId.Value}}
                FOR UPDATE
                """)
            .SingleOrDefaultAsync(cancellationToken);

        var stage = mutation.FileSetStage;
        if (task.Status != AgentTaskStatus.WaitingFinalApproval ||
            task.WorkspaceId != mutation.WorkspaceId ||
            task.ActiveRunAttemptId != attempt.Id ||
            task.RunLeaseId is null ||
            task.RunLeaseExpiresAt is null ||
            task.RunLeaseExpiresAt <= checkpoint.CompletedAtUtc ||
            attempt.Status != AgentTaskRunAttemptStatus.Running ||
            attempt.LeaseId != task.RunLeaseId ||
            attempt.LeaseExpiresAt is null ||
            attempt.LeaseExpiresAt <= checkpoint.CompletedAtUtc ||
            workspace.TaskId != task.Id ||
            workspace.Status != ArtifactWorkspaceStatus.Active ||
            artifacts.Count == 0 ||
            approval is null ||
            approval.TaskId != task.Id ||
            approval.ApprovalType != AgentApprovalType.FinalOutput ||
            approval.Status != AgentApprovalStatus.Approved ||
            approval.ApprovedBy is null ||
            approval.ApprovedAt is null ||
            approval.ApprovedAt < approval.CreatedAt ||
            !string.Equals(approval.TargetId, workspace.WorkspaceCode, StringComparison.Ordinal) ||
            mutation.ArtifactBindings.Count != artifacts.Count ||
            stage.CommitId == Guid.Empty ||
            !string.Equals(stage.WorkspaceCode, workspace.WorkspaceCode, StringComparison.Ordinal) ||
            !string.Equals(stage.OperationKind, "FinalizeArtifacts", StringComparison.Ordinal) ||
            !stage.PublishedReference.StartsWith("final/", StringComparison.Ordinal) ||
            stage.Files.Count != artifacts.Count ||
            stage.Authority.TaskId != task.Id.Value ||
            stage.Authority.NodeRunId != node.Id.Value ||
            stage.Authority.TaskFencingToken != checkpoint.TaskFencingToken ||
            stage.Authority.NodeFencingToken != checkpoint.NodeFencingToken ||
            !string.Equals(stage.ManifestDigest, checkpoint.OutputDigest, StringComparison.Ordinal) ||
            mutation.ArtifactBindings.Select(binding => binding.ArtifactId).Distinct().Count() != artifacts.Count ||
            mutation.ArtifactBindings.Select(binding => binding.FinalRelativePath)
                .Distinct(StringComparer.OrdinalIgnoreCase).Count() != artifacts.Count)
        {
            return null;
        }

        var orderedSteps = steps.OrderBy(step => step.StepIndex).ToArray();
        var finalStep = orderedSteps.SingleOrDefault(step => step.Id == mutation.FinalStepId);
        if (finalStep is null ||
            finalStep.StepIndex != orderedSteps.Length ||
            finalStep.StepType != AgentStepType.Finalize ||
            finalStep.Status != AgentStepStatus.Approved ||
            !finalStep.RequiresApproval ||
            !string.Equals(
                finalStep.ToolCode,
                BuiltInToolRegistrations.FinalizationCheckpointToolCode,
                StringComparison.Ordinal) ||
            orderedSteps.Length == 0 ||
            !orderedSteps.Select(step => step.StepIndex)
                .SequenceEqual(Enumerable.Range(1, orderedSteps.Length)) ||
            orderedSteps[..^1].Any(step => step.Status != AgentStepStatus.Completed) ||
            string.IsNullOrWhiteSpace(mutation.FinalStepOutputJson) ||
            string.IsNullOrWhiteSpace(mutation.FinalSummary))
        {
            return null;
        }

        foreach (var artifact in artifacts)
        {
            var binding = mutation.ArtifactBindings.SingleOrDefault(candidate => candidate.ArtifactId == artifact.Id);
            if (binding is null ||
                artifact.TaskId != task.Id ||
                artifact.WorkspaceId != workspace.Id ||
                artifact.Status is not (ArtifactStatus.Draft or ArtifactStatus.Reviewing or ArtifactStatus.Approved) ||
                artifact.FinalizedAt is not null ||
                !string.Equals(
                    ArtifactPathGuard.NormalizeRelativePath(artifact.RelativePath),
                    ArtifactPathGuard.NormalizeRelativePath(binding.SourceRelativePath),
                    StringComparison.Ordinal) ||
                artifact.FileSize != binding.FileSize ||
                !string.Equals(artifact.MimeType, binding.MimeType, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            ArtifactFileSetPublishedFile file;
            try
            {
                var normalizedFinalPath = ArtifactPathGuard.NormalizeFinalPath(binding.FinalRelativePath);
                file = stage.Files.Single(candidate => string.Equals(
                    candidate.RelativePath,
                    normalizedFinalPath,
                    StringComparison.Ordinal));
            }
            catch (Exception exception) when (exception is InvalidOperationException or ArgumentException)
            {
                return null;
            }

            if (file.FileSize != binding.FileSize ||
                !string.Equals(file.MimeType, binding.MimeType, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(file.Sha256, binding.Sha256, StringComparison.Ordinal))
            {
                return null;
            }
        }

        return new FinalizationAuthority(workspace, artifacts, finalStep, approval);
    }

    private static void ApplyFinalization(
        AiGatewayDbContext context,
        AgentTask task,
        AgentTaskRunAttempt attempt,
        FinalizationAuthority authority,
        AgentNodeSuccessCheckpoint checkpoint)
    {
        var mutation = checkpoint.Finalization!;
        foreach (var artifact in authority.Artifacts)
        {
            var binding = mutation.ArtifactBindings.Single(candidate => candidate.ArtifactId == artifact.Id);
            if (artifact.Status is ArtifactStatus.Draft or ArtifactStatus.Reviewing)
            {
                artifact.Approve(checkpoint.CompletedAtUtc);
            }

            artifact.MarkFinal(binding.FinalRelativePath, checkpoint.CompletedAtUtc);
        }

        authority.Workspace.FinalizeWorkspace(checkpoint.CompletedAtUtc);
        authority.FinalStep.Start(checkpoint.CompletedAtUtc);
        authority.FinalStep.Complete(mutation.FinalStepOutputJson, checkpoint.CompletedAtUtc);
        task.MarkFinalized(checkpoint.CompletedAtUtc);
        task.Complete(mutation.FinalSummary, checkpoint.CompletedAtUtc);
        attempt.MarkSucceeded(checkpoint.CompletedAtUtc, "Workspace final output approved and committed.");
        task.ReleaseRunLease(checkpoint.CompletedAtUtc, clearActiveAttempt: true);

        var stage = mutation.FileSetStage;
        var operation = new ArtifactFileSetOperation(
            stage.CommitId,
            task.Id,
            authority.Workspace.Id,
            checkpoint.NodeRunId,
            checkpoint.TaskFencingToken,
            checkpoint.NodeFencingToken,
            stage.OperationKind,
            stage.ManifestJson,
            stage.ManifestDigest,
            stage.StagingReference,
            checkpoint.CompletedAtUtc);
        operation.MarkPublished(stage.PublishedReference, stage.ManifestDigest, checkpoint.CompletedAtUtc);
        operation.MarkDatabaseCommitted(checkpoint.CompletedAtUtc);
        operation.Complete(checkpoint.CompletedAtUtc);
        context.ArtifactFileSetOperations.Add(operation);
    }

    private static bool TrySettleBudget(
        AgentTaskRunAttempt attempt,
        AgentNodeRun node,
        long nodeFencingToken,
        AgentRunUsageLedgerEntry usage,
        DateTimeOffset nowUtc,
        bool conservativelyConsumed)
    {
        if (!string.Equals(
                usage.CostCurrency,
                attempt.BudgetCostCurrency,
                StringComparison.Ordinal))
        {
            return false;
        }

        AgentRunBudgetCharge reservation;
        try
        {
            reservation = node.GetActiveBudgetReservation(nodeFencingToken);
        }
        catch (InvalidOperationException)
        {
            return false;
        }

        var actual = new AgentRunBudgetCharge(
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
            nodeFencingToken,
            conservativelyConsumed
                ? AgentBudgetReservationStatus.ConservativelyConsumed
                : AgentBudgetReservationStatus.Settled,
            nowUtc);
        return true;
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
        var statusByNodeId = nodes.ToDictionary(
            node => node.NodeId,
            node => node.Status,
            StringComparer.Ordinal);
        foreach (var candidate in nodes.Where(node => node.Status == AgentNodeRunStatus.Pending))
        {
            string[] dependencies;
            try
            {
                dependencies = JsonSerializer.Deserialize<string[]>(candidate.DependenciesJson) ?? [];
            }
            catch (JsonException exception)
            {
                throw new InvalidOperationException(
                    $"NodeRun '{candidate.NodeId}' contains invalid dependency JSON.",
                    exception);
            }

            if (dependencies.All(dependency =>
                    statusByNodeId.TryGetValue(dependency, out var status) &&
                    status == AgentNodeRunStatus.Succeeded))
            {
                candidate.MakeRunnable(nowUtc);
            }
        }
    }

    private static async Task<(AgentTask Task, AgentTaskRunAttempt Attempt)?> LockAuthorityAsync(
        AiGatewayDbContext context,
        Guid taskId,
        Guid runAttemptId,
        long taskFencingToken,
        CancellationToken cancellationToken)
    {
        var task = await context.AgentTasks
            .FromSqlInterpolated($$"""
                SELECT * FROM aigateway.agent_tasks
                WHERE id = {{taskId}}
                  AND active_run_attempt_id = {{runAttemptId}}
                  AND run_fencing_token = {{taskFencingToken}}
                FOR UPDATE
                """)
            .SingleOrDefaultAsync(cancellationToken);
        if (task is null)
        {
            return null;
        }

        var attempt = await context.AgentTaskRunAttempts
            .FromSqlInterpolated($$"""
                SELECT * FROM aigateway.agent_task_run_attempts
                WHERE id = {{runAttemptId}}
                  AND task_id = {{taskId}}
                  AND task_fencing_token = {{taskFencingToken}}
                FOR UPDATE
                """)
            .SingleOrDefaultAsync(cancellationToken);
        return attempt is null ? null : (task, attempt);
    }

    private static Task<AgentNodeRun?> LockNodeAsync(
        AiGatewayDbContext context,
        Guid nodeRunId,
        Guid runAttemptId,
        long taskFencingToken,
        long nodeFencingToken,
        AgentNodeRunStatus? expectedStatus,
        CancellationToken cancellationToken)
    {
        var status = expectedStatus?.ToString();
        return context.AgentNodeRuns
            .FromSqlInterpolated($$"""
                SELECT * FROM aigateway.agent_node_runs
                WHERE id = {{nodeRunId}}
                  AND run_attempt_id = {{runAttemptId}}
                  AND task_fencing_token = {{taskFencingToken}}
                  AND node_fencing_token = {{nodeFencingToken}}
                  AND ({{status}} IS NULL OR status = {{status}})
                FOR UPDATE
                """)
            .SingleOrDefaultAsync(cancellationToken);
    }

    private static AgentExecutionTransactionAttempt<AgentFencedWriteResult> Stale() =>
        new(AgentFencedWriteResult.StaleFence);

    private sealed record FinalizationAuthority(
        ArtifactWorkspace Workspace,
        IReadOnlyList<Artifact> Artifacts,
        AgentStep FinalStep,
        ApprovalRequest Approval);
}
