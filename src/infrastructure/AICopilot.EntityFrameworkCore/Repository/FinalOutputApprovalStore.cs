using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AICopilot.Core.AiGateway.Aggregates.AgentTasks;
using AICopilot.Core.AiGateway.Aggregates.Approvals;
using AICopilot.Core.AiGateway.Aggregates.Artifacts;
using AICopilot.Core.AiGateway.Aggregates.Sessions;
using AICopilot.Core.AiGateway.Aggregates.Tools;
using AICopilot.Core.AiGateway.Runtime.AgentExecution;
using AICopilot.EntityFrameworkCore.AuditLogs;
using AICopilot.EntityFrameworkCore.Transactions;
using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Ai;
using AICopilot.SharedKernel.Result;
using Microsoft.EntityFrameworkCore;

namespace AICopilot.EntityFrameworkCore.Repository;

internal sealed class FinalOutputApprovalStore(
    AgentExecutionTransactionRunner transactionRunner,
    IArtifactWorkspaceFileStore fileStore,
    ICurrentUser? currentUser = null)
    : IFinalOutputApprovalStore
{
    public Task<FinalOutputApprovalCommandResult> PrepareAsync(
        FinalOutputApprovalPreparation preparation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(preparation);
        preparation.Proof.Validate();
        return transactionRunner.ExecuteAsync(
            "Agent.FinalOutputApprovalPrepare",
            async (context, token) =>
            {
                // The coordinator performs scoped proof reads before entering this store.
                // Final authority must be reloaded after the transaction starts rather
                // than reusing any tracked snapshot from those reads.
                context.ChangeTracker.Clear();
                var authority = await LockAuthorityAsync(
                    context,
                    preparation.TaskId.Value,
                    preparation.Proof,
                    token);
                if (authority is null ||
                    preparation.RequestedBy == Guid.Empty ||
                    preparation.RequestedBy != authority.Task.UserId ||
                    !await MatchesProofMaterialAsync(
                        context,
                        authority,
                        preparation.Proof,
                        token))
                {
                    return Attempt(Conflict());
                }

                var approvals = await LockApprovalsByTaskAsync(
                    context,
                    authority.Task.Id.Value,
                    token);
                var finalApprovals = approvals
                    .Where(approval => approval.ApprovalType == AgentApprovalType.FinalOutput)
                    .ToArray();
                if (finalApprovals.Length > 1 ||
                    approvals.Any(approval =>
                        approval.Status == AgentApprovalStatus.Pending &&
                        approval.ApprovalType != AgentApprovalType.FinalOutput))
                {
                    return Attempt(Conflict(authority));
                }

                if (finalApprovals.Length == 1)
                {
                    var existing = finalApprovals[0];
                    if (!existing.MatchesFinalOutputProof(preparation.Proof))
                    {
                        return Attempt(Conflict(authority, existing));
                    }

                    if (existing.Status == AgentApprovalStatus.Pending &&
                        IsPaused(authority) &&
                        !await EnsureApprovalTimelineProjectionAsync(
                            context,
                            authority,
                            existing,
                            MessageEventType.ApprovalRequested,
                            token))
                    {
                        return Attempt(Conflict(authority, existing));
                    }

                    return existing.Status switch
                    {
                        AgentApprovalStatus.Pending when IsPaused(authority) =>
                            Attempt(Result(
                                FinalOutputApprovalCommandStatus.ExistingPending,
                                authority,
                                existing,
                                queueItem: null,
                                stateChanged: false)),
                        AgentApprovalStatus.Approved =>
                            Attempt(Result(
                                FinalOutputApprovalCommandStatus.Approved,
                                authority,
                                existing,
                                queueItem: null,
                                stateChanged: false)),
                        AgentApprovalStatus.Rejected =>
                            Attempt(Result(
                                FinalOutputApprovalCommandStatus.ApprovalRejected,
                                authority,
                                existing,
                                queueItem: null,
                                stateChanged: false)),
                        _ => Attempt(Conflict(authority, existing))
                    };
                }

                if (!IsPreApproval(authority, preparation.CreatedAtUtc))
                {
                    return Attempt(Conflict(authority));
                }

                if (!await RetireOriginatingQueueAsync(
                        context,
                        authority,
                        preparation.CreatedAtUtc,
                        token))
                {
                    return Attempt(Conflict(authority));
                }

                var approval = ApprovalRequest.CreateFinalOutput(
                    authority.Task.Id,
                    preparation.RequestedBy,
                    preparation.CreatedAtUtc,
                    preparation.Proof);
                context.ApprovalRequests.Add(approval);
                if (!await EnsureApprovalTimelineProjectionAsync(
                        context,
                        authority,
                        approval,
                        MessageEventType.ApprovalRequested,
                        token))
                {
                    throw new InvalidOperationException(
                        "Final-output approval timeline authority is missing or inconsistent.");
                }

                if (authority.Task.Status != AgentTaskStatus.WorkspaceReady)
                {
                    authority.Task.MarkWorkspaceReady(preparation.CreatedAtUtc);
                }

                authority.Task.WaitForFinalApproval(preparation.CreatedAtUtc);
                authority.Attempt.WaitForApproval(
                    preparation.CreatedAtUtc,
                    "Waiting for final output approval.");
                authority.Task.ReleaseRunLease(
                    preparation.CreatedAtUtc,
                    clearActiveAttempt: false);
                return Attempt(
                    Result(
                        FinalOutputApprovalCommandStatus.Created,
                        authority,
                        approval,
                        queueItem: null,
                        stateChanged: true),
                    CreateAuditEntry(
                        approval,
                        authority,
                        preparation.RequestedBy,
                        AuditResults.Succeeded,
                        "Agent.FinalReviewSubmitted",
                        "Workspace final review submitted and is waiting for approval.",
                        preparation.CreatedAtUtc));
            },
            cancellationToken);
    }

    public Task<FinalOutputApprovalCommandResult> DecideAsync(
        FinalOutputApprovalDecision decision,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(decision);
        return transactionRunner.ExecuteAsync(
            "Agent.FinalOutputApprovalDecision",
            async (context, token) =>
            {
                // Approval decisions must lock and validate fresh database authority,
                // even when the scoped coordinator previously loaded the same rows.
                context.ChangeTracker.Clear();
                var identity = await context.ApprovalRequests
                    .AsNoTracking()
                    .Where(approval => approval.Id == decision.ApprovalRequestId)
                    .Select(approval => new { approval.TaskId })
                    .SingleOrDefaultAsync(token);
                if (identity is null)
                {
                    return Attempt(NotFound());
                }

                var task = await AgentExecutionRowLock.ByIdAsync<AgentTask>(
                    context,
                    identity.TaskId.Value,
                    token);
                if (task is null)
                {
                    return Attempt(NotFound());
                }

                await context.Entry(task)
                    .Collection(candidate => candidate.Steps)
                    .LoadAsync(token);
                var approval = await LockApprovalAsync(
                    context,
                    decision.ApprovalRequestId.Value,
                    token);
                if (approval is null ||
                    approval.TaskId != task.Id ||
                    approval.ApprovalType != AgentApprovalType.FinalOutput ||
                    !approval.HasValidFinalOutputProof())
                {
                    return Attempt(Conflict(task: task, approval: approval));
                }

                var proof = approval.GetFinalOutputProof();
                var authority = await LockAuthorityAsync(
                    context,
                    task.Id.Value,
                    proof,
                    token,
                    lockedTask: task);
                if (authority is null)
                {
                    return Attempt(Conflict(task: task, approval: approval));
                }

                var sourceQueues = await context.AgentTaskRunQueueItems
                    .FromSqlInterpolated($$"""
                        SELECT queue_item.*, queue_item.xmin
                        FROM aigateway.agent_task_run_queue_items AS queue_item
                        WHERE source_approval_request_id = {{approval.Id.Value}}
                        ORDER BY created_at, id
                        FOR UPDATE
                        """)
                    .ToArrayAsync(token);
                if (sourceQueues.Length > 1)
                {
                    return Attempt(Conflict(authority, approval));
                }

                if (approval.Status != AgentApprovalStatus.Pending)
                {
                    return Attempt(ResolveExistingDecision(
                        authority,
                        approval,
                        sourceQueues.SingleOrDefault(),
                        decision.IsApproved));
                }

                if (decision.DecidedBy == Guid.Empty ||
                    decision.CurrentProof is null ||
                    !approval.MatchesFinalOutputProof(decision.CurrentProof) ||
                    !await MatchesProofMaterialAsync(
                        context,
                        authority,
                        proof,
                        token) ||
                    !IsPaused(authority) ||
                    sourceQueues.Length != 0 ||
                    await HasActiveQueueAsync(context, task.Id, token))
                {
                    return Attempt(Conflict(authority, approval));
                }

                if (decision.IsApproved)
                {
                    approval.Approve(
                        decision.DecidedBy,
                        decision.Comment,
                        decision.DecidedAtUtc);
                    authority.FinalStep.Approve();
                    if (!await EnsureApprovalTimelineProjectionAsync(
                            context,
                            authority,
                            approval,
                            MessageEventType.ApprovalDecided,
                            token))
                    {
                        throw new InvalidOperationException(
                            "Final-output approval decision timeline authority is missing or inconsistent.");
                    }

                    var queueItem = new AgentTaskRunQueueItem(
                        task.Id,
                        AgentTaskRunTriggerType.ApprovalResume,
                        decision.DecidedBy,
                        decision.DecidedAtUtc,
                        sourceApprovalRequestId: approval.Id);
                    context.AgentTaskRunQueueItems.Add(queueItem);
                    return Attempt(
                        Result(
                            FinalOutputApprovalCommandStatus.Approved,
                            authority,
                            approval,
                            queueItem,
                            stateChanged: true),
                        CreateAuditEntry(
                            approval,
                            authority,
                            decision.DecidedBy,
                            AuditResults.Succeeded,
                            "Agent.ApprovalDecision",
                            "Final-output approval decision committed with one durable resume queue item.",
                            decision.DecidedAtUtc));
                }

                approval.Reject(
                    decision.DecidedBy,
                    decision.Comment,
                    decision.DecidedAtUtc);
                if (!await EnsureApprovalTimelineProjectionAsync(
                        context,
                        authority,
                        approval,
                        MessageEventType.ApprovalDecided,
                        token))
                {
                    throw new InvalidOperationException(
                        "Final-output approval decision timeline authority is missing or inconsistent.");
                }

                const string rejection = "Final output approval was rejected.";
                authority.FinalStep.Fail(rejection, decision.DecidedAtUtc);
                authority.FinalNode.CancelBeforeExecution(rejection, decision.DecidedAtUtc);
                authority.Attempt.MarkFailed(
                    AppProblemCodes.AgentApprovalRejected,
                    rejection,
                    decision.DecidedAtUtc);
                task.Reject(rejection, decision.DecidedAtUtc);
                return Attempt(
                    Result(
                        FinalOutputApprovalCommandStatus.Rejected,
                        authority,
                        approval,
                        queueItem: null,
                        stateChanged: true),
                    CreateAuditEntry(
                        approval,
                        authority,
                        decision.DecidedBy,
                        AuditResults.Rejected,
                        "Agent.ApprovalDecision",
                        "Final-output rejection committed and terminal task state recorded.",
                        decision.DecidedAtUtc));
            },
            cancellationToken);
    }

    private async Task<bool> EnsureApprovalTimelineProjectionAsync(
        AiGatewayDbContext context,
        LockedAuthority authority,
        ApprovalRequest approval,
        MessageEventType eventType,
        CancellationToken cancellationToken)
    {
        var existing = await context.MessageEvents
            .Where(messageEvent =>
                messageEvent.ApprovalRequestId == approval.Id &&
                messageEvent.EventType == eventType)
            .OrderBy(messageEvent => messageEvent.Sequence)
            .ToArrayAsync(cancellationToken);
        if (existing.Length > 1)
        {
            return false;
        }

        var expectedCreatedAt = eventType == MessageEventType.ApprovalRequested
            ? approval.CreatedAt
            : approval.ApprovedAt;
        if (expectedCreatedAt is null)
        {
            return false;
        }

        if (existing.Length == 1)
        {
            var projection = existing[0];
            return projection.SessionId == authority.Task.SessionId &&
                   projection.AgentTaskId == authority.Task.Id &&
                   projection.ApprovalRequestId == approval.Id &&
                   projection.ArtifactWorkspaceId == authority.Workspace.Id &&
                   projection.CreatedAt == expectedCreatedAt.Value;
        }

        var session = await AgentExecutionRowLock.ByIdAsync<Session>(
            context,
            authority.Task.SessionId.Value,
            cancellationToken);
        if (session is null)
        {
            return false;
        }

        var nextSequence = await context.MessageEvents
            .Where(messageEvent => messageEvent.SessionId == session.Id)
            .Select(messageEvent => (int?)messageEvent.Sequence)
            .MaxAsync(cancellationToken) ?? 0;
        context.MessageEvents.Add(MessageEvent.FromProjection(
            session.Id,
            checked(nextSequence + 1),
            eventType,
            expectedCreatedAt.Value,
            authority.Task.Id,
            approvalRequestId: approval.Id,
            artifactWorkspaceId: authority.Workspace.Id));
        return true;
    }

    private AuditLogEntry CreateAuditEntry(
        ApprovalRequest approval,
        LockedAuthority authority,
        Guid actorId,
        string result,
        string actionCode,
        string summary,
        DateTimeOffset createdAtUtc)
    {
        var operatorId = currentUser?.Id ?? actorId;
        var operatorName = string.IsNullOrWhiteSpace(currentUser?.UserName)
            ? "System"
            : currentUser.UserName;
        return new AuditLogEntry
        {
            Id = Guid.NewGuid(),
            ActionGroup = AuditActionGroups.AiGateway,
            ActionCode = actionCode,
            TargetType = "ApprovalRequest",
            TargetId = approval.Id.Value.ToString(),
            TargetName = approval.ApprovalType.ToString(),
            OperatorUserId = operatorId.ToString(),
            OperatorUserName = operatorName!,
            OperatorRoleName = currentUser?.Role,
            Result = result,
            Summary = summary,
            ChangedFields = AuditMetadataCodec.Combine(
                changedFields: null,
                new Dictionary<string, string>
                {
                    ["taskId"] = authority.Task.Id.Value.ToString(),
                    ["taskCode"] = authority.Task.TaskCode,
                    ["workspaceCode"] = authority.Workspace.WorkspaceCode,
                    ["approvalType"] = approval.ApprovalType.ToString(),
                    ["targetId"] = approval.TargetId,
                    ["approvalStatus"] = approval.Status.ToString()
                }),
            CreatedAt = createdAtUtc.UtcDateTime
        };
    }

    private static FinalOutputApprovalCommandResult ResolveExistingDecision(
        LockedAuthority authority,
        ApprovalRequest approval,
        AgentTaskRunQueueItem? queueItem,
        bool requestedApproval)
    {
        if (approval.Status == AgentApprovalStatus.Approved)
        {
            if (!approval.HasValidFinalOutputDecisionProof() ||
                !MatchesApprovedResumeQueue(authority, approval, queueItem))
            {
                return Conflict(authority, approval);
            }

            if (!requestedApproval)
            {
                return Result(
                    FinalOutputApprovalCommandStatus.DecisionConflict,
                    authority,
                    approval,
                    queueItem,
                    stateChanged: false);
            }

            var completed = authority.Task.Status == AgentTaskStatus.Completed &&
                            authority.Workspace.Status == ArtifactWorkspaceStatus.Finalized &&
                            authority.Attempt.Status == AgentTaskRunAttemptStatus.Succeeded &&
                            authority.FinalStep.Status == AgentStepStatus.Completed &&
                            authority.FinalNode.Status == AgentNodeRunStatus.Succeeded;
            var waiting = authority.Task.Status == AgentTaskStatus.WaitingFinalApproval &&
                          authority.Workspace.Status == ArtifactWorkspaceStatus.Active &&
                          authority.Attempt.Status is AgentTaskRunAttemptStatus.WaitingApproval or AgentTaskRunAttemptStatus.Running &&
                          authority.FinalStep.Status == AgentStepStatus.Approved &&
                          authority.FinalNode.Status is AgentNodeRunStatus.WaitingApproval
                              or AgentNodeRunStatus.Runnable
                              or AgentNodeRunStatus.Claimed
                              or AgentNodeRunStatus.Running;
            return completed || waiting
                ? Result(
                    FinalOutputApprovalCommandStatus.DuplicateDecision,
                    authority,
                    approval,
                    queueItem,
                    stateChanged: false)
                : Conflict(authority, approval);
        }

        if (approval.Status == AgentApprovalStatus.Rejected)
        {
            if (!approval.HasValidFinalOutputDecisionProof())
            {
                return Conflict(authority, approval);
            }

            if (requestedApproval)
            {
                return Result(
                    FinalOutputApprovalCommandStatus.DecisionConflict,
                    authority,
                    approval,
                    queueItem,
                    stateChanged: false);
            }

            var rejected = authority.Task.Status == AgentTaskStatus.Rejected &&
                           authority.Attempt.Status == AgentTaskRunAttemptStatus.Failed &&
                           authority.FinalStep.Status == AgentStepStatus.Failed &&
                           authority.FinalNode.Status == AgentNodeRunStatus.Cancelled &&
                           queueItem is null;
            return rejected
                ? Result(
                    FinalOutputApprovalCommandStatus.DuplicateDecision,
                    authority,
                    approval,
                    queueItem: null,
                    stateChanged: false)
                : Conflict(authority, approval);
        }

        return Conflict(authority, approval);
    }

    private static bool MatchesApprovedResumeQueue(
        LockedAuthority authority,
        ApprovalRequest approval,
        AgentTaskRunQueueItem? queueItem)
    {
        if (queueItem is null ||
            approval.ApprovedBy is null ||
            approval.ApprovedAt is null ||
            queueItem.SourceApprovalRequestId != approval.Id ||
            queueItem.TaskId != authority.Task.Id ||
            queueItem.TriggerType != AgentTaskRunTriggerType.ApprovalResume ||
            queueItem.RequestedBy != approval.ApprovedBy ||
            queueItem.CreatedAt != approval.ApprovedAt ||
            queueItem.AvailableAt != approval.ApprovedAt)
        {
            return false;
        }

        var proof = approval.GetFinalOutputProof();
        return queueItem.Status switch
        {
            AgentTaskRunQueueStatus.Queued =>
                queueItem.RunAttemptId is null &&
                queueItem.TaskFencingToken == 0,
            AgentTaskRunQueueStatus.Claimed or
            AgentTaskRunQueueStatus.Started or
            AgentTaskRunQueueStatus.Succeeded =>
                queueItem.RunAttemptId == proof.ActiveRunAttemptId &&
                queueItem.TaskFencingToken == proof.TaskFencingToken,
            _ => false
        };
    }

    private static async Task<LockedAuthority?> LockAuthorityAsync(
        AiGatewayDbContext context,
        Guid taskId,
        FinalOutputApprovalProof proof,
        CancellationToken cancellationToken,
        AgentTask? lockedTask = null)
    {
        var task = lockedTask ?? await AgentExecutionRowLock.ByIdAsync<AgentTask>(
            context,
            taskId,
            cancellationToken);
        if (task is null)
        {
            return null;
        }

        if (lockedTask is null)
        {
            await context.Entry(task)
                .Collection(candidate => candidate.Steps)
                .LoadAsync(cancellationToken);
        }

        var workspace = await context.ArtifactWorkspaces
            .FromSqlInterpolated($$"""
                SELECT workspace.*, workspace.xmin
                FROM aigateway.artifact_workspaces AS workspace
                WHERE id = {{proof.WorkspaceId.Value}}
                FOR UPDATE
                """)
            .SingleOrDefaultAsync(cancellationToken);
        if (workspace is null)
        {
            return null;
        }

        var artifacts = await AgentExecutionRowLock.ByAggregateOwnerAsync<Artifact>(
            context,
            workspace.Id.Value,
            cancellationToken);
        var attempt = await AgentExecutionRowLock.ByIdAsync<AgentTaskRunAttempt>(
            context,
            proof.ActiveRunAttemptId.Value,
            cancellationToken);
        var node = await AgentExecutionRowLock.ByIdAsync<AgentNodeRun>(
            context,
            proof.FinalNodeRunId.Value,
            cancellationToken);
        var finalStep = task.Steps.SingleOrDefault(step => step.Id == proof.FinalStepId);
        if (attempt is null ||
            node is null ||
            finalStep is null ||
            !MatchesProofAuthority(task, workspace, artifacts, attempt, node, finalStep, proof))
        {
            return null;
        }

        return new LockedAuthority(task, workspace, artifacts, attempt, node, finalStep);
    }

    private static bool MatchesProofAuthority(
        AgentTask task,
        ArtifactWorkspace workspace,
        IReadOnlyCollection<Artifact> artifacts,
        AgentTaskRunAttempt attempt,
        AgentNodeRun node,
        AgentStep finalStep,
        FinalOutputApprovalProof proof)
    {
        if (task.Id != attempt.TaskId ||
            task.Id != workspace.TaskId ||
            task.WorkspaceId != workspace.Id ||
            (task.ActiveRunAttemptId != attempt.Id &&
             task.Status != AgentTaskStatus.Completed &&
             task.Status != AgentTaskStatus.Rejected) ||
            attempt.Id != proof.ActiveRunAttemptId ||
            node.TaskId != task.Id ||
            node.RunAttemptId != attempt.Id ||
            node.Id != proof.FinalNodeRunId ||
            !node.RequiresApproval ||
            node.SideEffectClass != AgentNodeSideEffectClass.ArtifactWrite ||
            !string.Equals(
                node.ToolCode,
                BuiltInToolRegistrations.FinalizationCheckpointToolCode,
                StringComparison.Ordinal) ||
            finalStep.Id != proof.FinalStepId ||
            !string.Equals(workspace.WorkspaceCode, proof.WorkspaceCode, StringComparison.Ordinal) ||
            task.RunFencingToken != proof.TaskFencingToken ||
            attempt.TaskFencingToken != proof.TaskFencingToken ||
            node.TaskFencingToken != proof.TaskFencingToken ||
            !MatchesNodeFencing(node, proof.NodeFencingToken) ||
            finalStep.StepType != AgentStepType.Finalize ||
            !finalStep.RequiresApproval ||
            !string.Equals(
                finalStep.ToolCode,
                BuiltInToolRegistrations.FinalizationCheckpointToolCode,
                StringComparison.Ordinal) ||
            artifacts.Count == 0 ||
            artifacts.Any(artifact =>
                artifact.TaskId != task.Id ||
                artifact.WorkspaceId != workspace.Id) ||
            !MatchesArtifactBindings(
                artifacts,
                proof,
                allowFinalPaths: task.Status == AgentTaskStatus.Completed &&
                                 workspace.Status == ArtifactWorkspaceStatus.Finalized))
        {
            return false;
        }

        var orderedSteps = task.Steps.OrderBy(step => step.StepIndex).ToArray();
        return orderedSteps.Length > 0 &&
               orderedSteps[^1].Id == finalStep.Id &&
               orderedSteps.Select(step => step.StepIndex)
                   .SequenceEqual(Enumerable.Range(1, orderedSteps.Length)) &&
               orderedSteps[..^1].All(step => step.Status == AgentStepStatus.Completed);
    }

    private static bool MatchesArtifactBindings(
        IReadOnlyCollection<Artifact> artifacts,
        FinalOutputApprovalProof proof,
        bool allowFinalPaths)
    {
        try
        {
            var bindings = JsonSerializer.Deserialize<FinalOutputApprovalArtifactBinding[]>(
                proof.ArtifactBindingsJson,
                SerializerOptions);
            return bindings is not null &&
                   bindings.Length == artifacts.Count &&
                   bindings.Select(binding => binding.ArtifactId).Distinct().Count() == bindings.Length &&
                   artifacts.All(artifact =>
                   {
                       var binding = bindings.SingleOrDefault(candidate =>
                           candidate.ArtifactId == artifact.Id.Value);
                       return binding is not null &&
                              artifact.CreatedByStepId?.Value == binding.CreatedByStepId &&
                              artifact.Version == binding.Version &&
                              artifact.FileSize == binding.FileSize &&
                              (allowFinalPaths && artifact.Status == ArtifactStatus.Final ||
                               string.Equals(
                                   ArtifactPathGuard.NormalizeRelativePath(artifact.RelativePath),
                                   binding.SourceRelativePath,
                                   StringComparison.Ordinal)) &&
                              string.Equals(
                                  artifact.MimeType,
                                  binding.MimeType,
                                  StringComparison.OrdinalIgnoreCase);
                   });
        }
        catch (JsonException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private async Task<bool> MatchesProofMaterialAsync(
        AiGatewayDbContext context,
        LockedAuthority authority,
        FinalOutputApprovalProof proof,
        CancellationToken cancellationToken)
    {
        var bindings = new List<FinalOutputApprovalArtifactBinding>(authority.Artifacts.Count);
        foreach (var artifact in authority.Artifacts.OrderBy(item => item.Id.Value))
        {
            if (artifact.CreatedByStepId is null)
            {
                return false;
            }

            string sourcePath;
            try
            {
                sourcePath = ArtifactPathGuard.NormalizeRelativePath(artifact.RelativePath);
            }
            catch (ArgumentException)
            {
                return false;
            }

            var source = await fileStore.OpenReadAsync(
                authority.Workspace.WorkspaceCode,
                sourcePath,
                artifact.MimeType,
                cancellationToken);
            if (source is null)
            {
                return false;
            }

            string sha256;
            await using (source.Stream)
            {
                sha256 = Convert.ToHexString(
                        await SHA256.HashDataAsync(source.Stream, cancellationToken))
                    .ToLowerInvariant();
            }

            if (source.FileSize != artifact.FileSize ||
                !string.Equals(source.MimeType, artifact.MimeType, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            bindings.Add(new FinalOutputApprovalArtifactBinding(
                artifact.Id.Value,
                artifact.CreatedByStepId.Value.Value,
                artifact.Version,
                sourcePath,
                artifact.FileSize,
                artifact.MimeType,
                sha256));
        }

        var bindingsJson = SerializeCanonical(bindings);
        var bindingDigest = Hash(bindingsJson);
        if (!string.Equals(bindingsJson, proof.ArtifactBindingsJson, StringComparison.Ordinal) ||
            !string.Equals(bindingDigest, proof.ArtifactBindingDigest, StringComparison.Ordinal))
        {
            return false;
        }

        var evidenceSetDigest = await FinalOutputEvidenceSetAuthority.ComputeAsync(
            context,
            authority.Task,
            authority.Attempt,
            authority.FinalNode,
            DateTimeOffset.UtcNow,
            cancellationToken);
        if (evidenceSetDigest is null ||
            !string.Equals(evidenceSetDigest, proof.EvidenceSetDigest, StringComparison.Ordinal))
        {
            return false;
        }

        var manifestDigest = Hash(SerializeCanonical(new
        {
            version = "final-output-source-manifest-v1",
            taskId = authority.Task.Id.Value,
            workspaceId = authority.Workspace.Id.Value,
            workspaceCode = authority.Workspace.WorkspaceCode,
            finalStepId = authority.FinalStep.Id.Value,
            activeRunAttemptId = authority.Attempt.Id.Value,
            finalNodeRunId = authority.FinalNode.Id.Value,
            taskFencingToken = authority.Task.RunFencingToken,
            nodeFencingToken = proof.NodeFencingToken,
            evidenceSetDigest,
            artifactBindingDigest = bindingDigest,
            artifacts = bindings
        }));
        return string.Equals(manifestDigest, proof.ManifestDigest, StringComparison.Ordinal);
    }

    private static readonly JsonSerializerOptions SerializerOptions =
        new(JsonSerializerDefaults.Web)
        {
            PropertyNameCaseInsensitive = false
        };

    private static string SerializeCanonical<T>(T value) =>
        AgentCanonicalJsonV1.Canonicalize(JsonSerializer.Serialize(value, SerializerOptions));

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))
            .ToLowerInvariant();

    private static bool MatchesNodeFencing(AgentNodeRun node, long proofNodeFencingToken)
    {
        var expected = node.Status is AgentNodeRunStatus.WaitingApproval
            or AgentNodeRunStatus.Runnable
            or AgentNodeRunStatus.Cancelled
            ? checked(node.NodeFencingToken + 1)
            : node.NodeFencingToken;
        return expected == proofNodeFencingToken;
    }

    private static bool IsPreApproval(
        LockedAuthority authority,
        DateTimeOffset approvalCreatedAtUtc)
    {
        var task = authority.Task;
        var attempt = authority.Attempt;
        return (task.Status is AgentTaskStatus.Running
                    or AgentTaskStatus.GeneratingArtifacts
                    or AgentTaskStatus.WorkspaceReady) &&
               task.CompletedAt is null &&
               task.FinalSummary is null &&
               task.ActiveRunAttemptId == attempt.Id &&
               task.RunLeaseId is not null &&
               task.RunLeaseId == attempt.LeaseId &&
               task.RunLeaseExpiresAt is not null &&
               task.RunLeaseExpiresAt > approvalCreatedAtUtc &&
               attempt.Status == AgentTaskRunAttemptStatus.Running &&
               attempt.LeaseExpiresAt is not null &&
               attempt.LeaseExpiresAt > approvalCreatedAtUtc &&
               attempt.LeaseExpiresAt == task.RunLeaseExpiresAt &&
               authority.Workspace.Status == ArtifactWorkspaceStatus.Active &&
               authority.FinalStep.Status == AgentStepStatus.WaitingApproval &&
               authority.FinalNode.Status == AgentNodeRunStatus.WaitingApproval;
    }

    private static bool IsPaused(LockedAuthority authority)
    {
        var task = authority.Task;
        var attempt = authority.Attempt;
        return task.Status == AgentTaskStatus.WaitingFinalApproval &&
               task.CompletedAt is null &&
               task.FinalSummary is null &&
               task.ActiveRunAttemptId == attempt.Id &&
               task.RunLeaseId is null &&
               task.RunLeaseOwner is null &&
               task.RunLeaseExpiresAt is null &&
               attempt.Status == AgentTaskRunAttemptStatus.WaitingApproval &&
               attempt.LeaseId is null &&
               attempt.LeaseOwner is null &&
               attempt.LeaseExpiresAt is null &&
               authority.Workspace.Status == ArtifactWorkspaceStatus.Active &&
               authority.FinalStep.Status == AgentStepStatus.WaitingApproval &&
               authority.FinalNode.Status == AgentNodeRunStatus.WaitingApproval;
    }

    private static Task<bool> HasActiveQueueAsync(
        AiGatewayDbContext context,
        AICopilot.Core.AiGateway.Ids.AgentTaskId taskId,
        CancellationToken cancellationToken) =>
        context.AgentTaskRunQueueItems.AnyAsync(item =>
                item.TaskId == taskId &&
                (item.Status == AgentTaskRunQueueStatus.Queued ||
                 item.Status == AgentTaskRunQueueStatus.Claimed ||
                 item.Status == AgentTaskRunQueueStatus.Started),
            cancellationToken);

    private static async Task<bool> RetireOriginatingQueueAsync(
        AiGatewayDbContext context,
        LockedAuthority authority,
        DateTimeOffset pausedAtUtc,
        CancellationToken cancellationToken)
    {
        var queues = await context.AgentTaskRunQueueItems
            .FromSqlInterpolated($$"""
                SELECT queue_item.*, queue_item.xmin
                FROM aigateway.agent_task_run_queue_items AS queue_item
                WHERE task_id = {{authority.Task.Id.Value}}
                  AND run_attempt_id = {{authority.Attempt.Id.Value}}
                  AND task_fencing_token = {{authority.Task.RunFencingToken}}
                  AND source_approval_request_id IS NULL
                ORDER BY created_at, id
                FOR UPDATE
                """)
            .ToArrayAsync(cancellationToken);
        if (queues.Length != 1)
        {
            return false;
        }

        var queue = queues[0];
        if (queue.TriggerType == AgentTaskRunTriggerType.ApprovalResume ||
            queue.StartedAt is null)
        {
            return false;
        }

        if (queue.Status == AgentTaskRunQueueStatus.Succeeded)
        {
            return queue.CompletedAt is not null &&
                   queue.LeaseId is null &&
                   queue.LeaseOwner is null &&
                   queue.LeaseExpiresAt is null;
        }

        if (queue.Status != AgentTaskRunQueueStatus.Started ||
            queue.LeaseId != authority.Task.RunLeaseId ||
            queue.LeaseId != authority.Attempt.LeaseId ||
            queue.LeaseOwner != authority.Task.RunLeaseOwner ||
            queue.LeaseOwner != authority.Attempt.LeaseOwner ||
            queue.LeaseExpiresAt != authority.Task.RunLeaseExpiresAt ||
            queue.LeaseExpiresAt != authority.Attempt.LeaseExpiresAt)
        {
            return false;
        }

        queue.MarkSucceeded(
            pausedAtUtc,
            "Agent task run paused at the proof-bound final-output approval checkpoint.");
        return true;
    }

    private static Task<ApprovalRequest?> LockApprovalAsync(
        AiGatewayDbContext context,
        Guid approvalId,
        CancellationToken cancellationToken) =>
        context.ApprovalRequests
            .FromSqlInterpolated($$"""
                SELECT approval.*, approval.xmin
                FROM aigateway.approval_requests AS approval
                WHERE id = {{approvalId}}
                FOR UPDATE
                """)
            .SingleOrDefaultAsync(cancellationToken);

    private static Task<ApprovalRequest[]> LockApprovalsByTaskAsync(
        AiGatewayDbContext context,
        Guid taskId,
        CancellationToken cancellationToken) =>
        context.ApprovalRequests
            .FromSqlInterpolated($$"""
                SELECT approval.*, approval.xmin
                FROM aigateway.approval_requests AS approval
                WHERE task_id = {{taskId}}
                ORDER BY created_at, id
                FOR UPDATE
                """)
            .ToArrayAsync(cancellationToken);

    private static AgentExecutionTransactionAttempt<FinalOutputApprovalCommandResult> Attempt(
        FinalOutputApprovalCommandResult result,
        AuditLogEntry? auditEntry = null) =>
        new(
            result,
            AuditEntries: auditEntry is null ? null : [auditEntry]);

    private static FinalOutputApprovalCommandResult Result(
        FinalOutputApprovalCommandStatus status,
        LockedAuthority authority,
        ApprovalRequest approval,
        AgentTaskRunQueueItem? queueItem,
        bool stateChanged) =>
        new(
            status,
            approval,
            authority.Task,
            authority.Workspace,
            authority.Attempt,
            queueItem,
            stateChanged);

    private static FinalOutputApprovalCommandResult Conflict(
        LockedAuthority? authority = null,
        ApprovalRequest? approval = null,
        AgentTask? task = null) =>
        new(
            FinalOutputApprovalCommandStatus.FinalizationConflict,
            approval,
            authority?.Task ?? task,
            authority?.Workspace,
            authority?.Attempt,
            QueueItem: null,
            StateChanged: false);

    private static FinalOutputApprovalCommandResult NotFound() =>
        new(
            FinalOutputApprovalCommandStatus.NotFound,
            Approval: null,
            Task: null,
            Workspace: null,
            RunAttempt: null,
            QueueItem: null,
            StateChanged: false);

    private sealed record LockedAuthority(
        AgentTask Task,
        ArtifactWorkspace Workspace,
        IReadOnlyCollection<Artifact> Artifacts,
        AgentTaskRunAttempt Attempt,
        AgentNodeRun FinalNode,
        AgentStep FinalStep);
}
