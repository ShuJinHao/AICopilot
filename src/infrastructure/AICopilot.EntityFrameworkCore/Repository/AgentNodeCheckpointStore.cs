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
        return ExecuteCheckpointAsync(
            "Agent.NodeCheckpointSuccess",
            checkpoint,
            (context, authority, token) =>
                CommitSuccessCoreAsync(context, authority, checkpoint, token),
            cancellationToken);
    }

    public Task<AgentFencedWriteResult> CommitFailureAsync(
        AgentNodeFailureCheckpoint checkpoint,
        CancellationToken cancellationToken = default)
    {
        return ExecuteCheckpointAsync(
            "Agent.NodeCheckpointFailure",
            checkpoint,
            (context, authority, token) =>
                CommitFailureCoreAsync(context, authority, checkpoint, token),
            cancellationToken);
    }

    public Task<AgentFencedWriteResult> CommitOutcomeUnknownAsync(
        AgentNodeOutcomeUnknownCheckpoint checkpoint,
        CancellationToken cancellationToken = default)
    {
        return ExecuteCheckpointAsync(
            "Agent.NodeCheckpointOutcomeUnknown",
            checkpoint,
            (context, authority, token) =>
                CommitOutcomeUnknownCoreAsync(context, authority, checkpoint, token),
            cancellationToken);
    }

    private static async Task<AgentExecutionTransactionAttempt<AgentFencedWriteResult>> CommitSuccessCoreAsync(
        AiGatewayDbContext context,
        (AgentTask Task, AgentTaskRunAttempt Attempt) authority,
        AgentNodeSuccessCheckpoint checkpoint,
        CancellationToken cancellationToken)
    {
        var node = await LockRunningNodeAsync(context, checkpoint, cancellationToken);
        if (node is null)
        {
            var duplicate = await context.AgentEvidenceRecords.AsNoTracking().AnyAsync(evidence =>
                evidence.NodeRunId == checkpoint.NodeRunId &&
                evidence.NodeFencingToken == checkpoint.NodeFencingToken &&
                evidence.EnvelopeDigest == checkpoint.Evidence.EnvelopeDigest,
                cancellationToken);
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
                authority.Task,
                authority.Attempt,
                node,
                checkpoint,
                cancellationToken);
            if (finalizationAuthority is null)
            {
                return new AgentExecutionTransactionAttempt<AgentFencedWriteResult>(
                    AgentFencedWriteResult.StateConflict);
            }
        }

        if (!AgentNodeRunBudgetSettlement.TrySettle(
                authority.Attempt,
                node,
                checkpoint.NodeFencingToken,
                checkpoint.Usage,
                checkpoint.CompletedAtUtc,
                conservativelyConsumed: false))
        {
            return new AgentExecutionTransactionAttempt<AgentFencedWriteResult>(
                AgentFencedWriteResult.StateConflict);
        }

        var evidenceSetDigest = await AgentEvidenceSetDigest.ComputeAsync(
            context,
            checkpoint.RunAttemptId,
            checkpoint.Evidence,
            cancellationToken);
        context.AgentEvidenceRecords.Add(checkpoint.Evidence);
        context.AgentRunUsageLedgerEntries.Add(checkpoint.Usage);
        if (finalizationAuthority is not null)
        {
            ApplyFinalization(
                context,
                authority.Task,
                authority.Attempt,
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
        return await PromoteAndSucceedAsync(
            context, checkpoint.RunAttemptId, checkpoint.CompletedAtUtc, cancellationToken);
    }

    private static async Task<AgentExecutionTransactionAttempt<AgentFencedWriteResult>> CommitFailureCoreAsync(
        AiGatewayDbContext context,
        (AgentTask Task, AgentTaskRunAttempt Attempt) authority,
        AgentNodeFailureCheckpoint checkpoint,
        CancellationToken cancellationToken)
    {
        var node = await LockNodeAsync(
            context,
            checkpoint.NodeRunId.Value,
            checkpoint.RunAttemptId.Value,
            checkpoint.TaskFencingToken,
            checkpoint.NodeFencingToken,
            expectedStatus: null,
            cancellationToken);
        if (node is null ||
            node.Status is not AgentNodeRunStatus.Claimed and not AgentNodeRunStatus.Running)
        {
            return Stale();
        }

        if (!MatchesUsageAuthority(checkpoint, node) ||
            !AgentNodeRunBudgetSettlement.TrySettle(
                authority.Attempt,
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
        return await PromoteAndSucceedAsync(
            context, checkpoint.RunAttemptId, checkpoint.FailedAtUtc, cancellationToken);
    }

    private static async Task<AgentExecutionTransactionAttempt<AgentFencedWriteResult>> PromoteAndSucceedAsync(
        AiGatewayDbContext context,
        AgentTaskRunAttemptId runAttemptId,
        DateTimeOffset completedAtUtc,
        CancellationToken cancellationToken)
    {
        await AgentNodeRunDependencyPromoter.PromoteAsync(
            context, runAttemptId, completedAtUtc, cancellationToken);
        return new AgentExecutionTransactionAttempt<AgentFencedWriteResult>(
            AgentFencedWriteResult.Succeeded);
    }

    private static async Task<AgentExecutionTransactionAttempt<AgentFencedWriteResult>> CommitOutcomeUnknownCoreAsync(
        AiGatewayDbContext context,
        (AgentTask Task, AgentTaskRunAttempt Attempt) authority,
        AgentNodeOutcomeUnknownCheckpoint checkpoint,
        CancellationToken token)
    {
        var node = await LockRunningNodeAsync(context, checkpoint, token);
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
        authority.Task.RequireReconciliation(checkpoint.RecordedAtUtc);
        authority.Attempt.RequireReconciliation(
            checkpoint.RecordedAtUtc,
            checkpoint.SafeMessage);
        return new AgentExecutionTransactionAttempt<AgentFencedWriteResult>(
            AgentFencedWriteResult.Succeeded);
    }

    private static bool MatchesCheckpointAuthority(
        AgentNodeSuccessCheckpoint checkpoint,
        AgentNodeRun node)
    {
        return node.TaskId == checkpoint.TaskId &&
               MatchesEvidenceAuthority(checkpoint, node) &&
               MatchesUsageAuthority(checkpoint.Usage, checkpoint);
    }

    private static bool MatchesUsageAuthority(
        AgentNodeFailureCheckpoint checkpoint,
        AgentNodeRun node)
    {
        return node.TaskId == checkpoint.TaskId &&
               MatchesUsageAuthority(checkpoint.Usage, checkpoint);
    }

    private static bool MatchesEvidenceAuthority(AgentNodeSuccessCheckpoint checkpoint, AgentNodeRun node)
    {
        var evidence = checkpoint.Evidence;
        return evidence.TaskId == checkpoint.TaskId && evidence.RunAttemptId == checkpoint.RunAttemptId &&
               evidence.NodeRunId == checkpoint.NodeRunId && evidence.NodeId == node.NodeId &&
               evidence.TaskFencingToken == checkpoint.TaskFencingToken && evidence.NodeFencingToken == checkpoint.NodeFencingToken &&
               evidence.OutputDigest == checkpoint.OutputDigest;
    }

    private static bool MatchesUsageAuthority(
        AgentRunUsageLedgerEntry usage,
        IAgentNodeCheckpointAuthority checkpoint) =>
        usage.TaskId == checkpoint.TaskId && usage.RunAttemptId == checkpoint.RunAttemptId &&
        usage.NodeRunId == checkpoint.NodeRunId && usage.TaskFencingToken == checkpoint.TaskFencingToken &&
        usage.NodeFencingToken == checkpoint.NodeFencingToken;

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
                SELECT workspace.*, workspace.xmin FROM aigateway.artifact_workspaces AS workspace
                WHERE id = {{mutation.WorkspaceId.Value}}
                FOR UPDATE
                """)
            .SingleOrDefaultAsync(cancellationToken);
        if (workspace is null)
        {
            return null;
        }

        var artifacts = await AgentExecutionRowLock.ByAggregateOwnerAsync<Artifact>(
            context, mutation.WorkspaceId.Value, cancellationToken);
        var steps = await AgentExecutionRowLock.ByAggregateOwnerAsync<AgentStep>(
            context, task.Id.Value, cancellationToken);
        var approval = await context.ApprovalRequests
            .FromSqlInterpolated($$"""
                SELECT approval.*, approval.xmin FROM aigateway.approval_requests AS approval
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
        var operation = ArtifactFileSetOperationFactory.CreateCompleted(
            stage,
            task.Id,
            authority.Workspace.Id,
            checkpoint.NodeRunId,
            checkpoint.TaskFencingToken,
            checkpoint.NodeFencingToken,
            checkpoint.CompletedAtUtc);
        context.ArtifactFileSetOperations.Add(operation);
    }

    private Task<AgentFencedWriteResult> ExecuteCheckpointAsync(
        string operationName,
        IAgentNodeCheckpointAuthority checkpoint,
        Func<
            AiGatewayDbContext,
            (AgentTask Task, AgentTaskRunAttempt Attempt),
            CancellationToken,
            Task<AgentExecutionTransactionAttempt<AgentFencedWriteResult>>> execute,
        CancellationToken cancellationToken)
    {
        return transactionRunner.ExecuteAsync(
            operationName,
            async (context, token) =>
            {
                var authority = await LockAuthorityAsync(
                    context,
                    checkpoint.TaskId.Value,
                    checkpoint.RunAttemptId.Value,
                    checkpoint.TaskFencingToken,
                    token);
                return authority is null
                    ? Stale()
                    : await execute(context, authority.Value, token);
            },
            cancellationToken);
    }

    private static async Task<(AgentTask Task, AgentTaskRunAttempt Attempt)?> LockAuthorityAsync(
        AiGatewayDbContext context,
        Guid taskId,
        Guid runAttemptId,
        long taskFencingToken,
        CancellationToken cancellationToken)
    {
        var task = await AgentExecutionRowLock.ByIdAsync<AgentTask>(context, taskId, cancellationToken);
        if (task is null ||
            task.ActiveRunAttemptId?.Value != runAttemptId ||
            task.RunFencingToken != taskFencingToken)
        {
            return null;
        }

        var attempt = await AgentExecutionRowLock.ByIdAsync<AgentTaskRunAttempt>(context, runAttemptId, cancellationToken);
        return attempt is null || attempt.TaskId.Value != taskId || attempt.TaskFencingToken != taskFencingToken
            ? null
            : (task, attempt);
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
                SELECT node.*, node.xmin FROM aigateway.agent_node_runs AS node
                WHERE id = {{nodeRunId}}
                  AND run_attempt_id = {{runAttemptId}}
                  AND task_fencing_token = {{taskFencingToken}}
                  AND node_fencing_token = {{nodeFencingToken}}
                  AND ({{status}} IS NULL OR status = {{status}})
                FOR UPDATE
                """)
            .SingleOrDefaultAsync(cancellationToken);
    }

    private static Task<AgentNodeRun?> LockRunningNodeAsync(
        AiGatewayDbContext context,
        IAgentNodeCheckpointAuthority checkpoint,
        CancellationToken cancellationToken)
    {
        return LockNodeAsync(
            context,
            checkpoint.NodeRunId.Value,
            checkpoint.RunAttemptId.Value,
            checkpoint.TaskFencingToken,
            checkpoint.NodeFencingToken,
            AgentNodeRunStatus.Running,
            cancellationToken);
    }

    private static AgentExecutionTransactionAttempt<AgentFencedWriteResult> Stale() =>
        new(AgentFencedWriteResult.StaleFence);

    private sealed record FinalizationAuthority(
        ArtifactWorkspace Workspace,
        IReadOnlyList<Artifact> Artifacts,
        AgentStep FinalStep,
        ApprovalRequest Approval);
}
