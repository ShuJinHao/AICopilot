using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Security.Cryptography;
using System.Globalization;
using AICopilot.AiGatewayService.Skills;
using AICopilot.AiGatewayService.Tools;
using AICopilot.AiGatewayService.Workspaces;
using AICopilot.AiGatewayService.Workflows.Executors;
using AICopilot.Core.AiGateway.Aggregates.AgentTasks;
using AICopilot.Core.AiGateway.Aggregates.Approvals;
using AICopilot.Core.AiGateway.Aggregates.Artifacts;
using AICopilot.Core.AiGateway.Aggregates.Tools;
using AICopilot.Core.AiGateway.Aggregates.Uploads;
using AICopilot.Core.AiGateway.Ids;
using AICopilot.Core.AiGateway.Runtime.AgentExecution;
using AICopilot.Core.AiGateway.Specifications.AgentTasks;
using AICopilot.Core.AiGateway.Specifications.Approvals;
using AICopilot.Core.AiGateway.Specifications.Artifacts;
using AICopilot.Core.AiGateway.Specifications.Uploads;
using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Ai;
using AICopilot.SharedKernel.Repository;
using AICopilot.SharedKernel.Result;
using Microsoft.Extensions.Options;

namespace AICopilot.AiGatewayService.AgentTasks;

public interface IAgentTaskRuntime
{
    Task<Result<AgentTask>> RunAsync(AgentTask task, CancellationToken cancellationToken = default);

    Task<Result<AgentTask>> RunAsync(
        AgentTask task,
        AgentTaskRunTriggerType triggerType = AgentTaskRunTriggerType.Manual,
        CancellationToken cancellationToken = default);

    Task<Result<AgentTask>> RunClaimedAsync(
        DurableTaskClaim claim,
        CancellationToken cancellationToken = default)
    {
        return RunAsync(claim.Task, claim.QueueItem.TriggerType, cancellationToken);
    }
}

internal sealed class AgentTaskRuntime(
    IRepository<AgentTask> taskRepository,
    IAgentTaskRunAttemptStore runAttemptStore,
    IRepository<ArtifactWorkspace> workspaceRepository,
    IRepository<ApprovalRequest> approvalRepository,
    IReadRepository<UploadRecord> uploadRepository,
    IAgentArtifactWorkspaceService workspaceService,
    IFileStorageService fileStorage,
    IAgentTableFileParser tableFileParser,
    IAgentArtifactDocumentGenerator documentGenerator,
    IKnowledgeRetrievalService knowledgeRetrievalService,
    IEnumerable<IKnowledgeBaseAccessChecker> knowledgeBaseAccessCheckers,
    ICloudReadonlyAgentToolExecutor cloudReadonlyToolExecutor,
    IIdentityAccessService identityAccessService,
    ToolRegistryGuard toolRegistryGuard,
    IAgentPlanRuntimeSnapshotVerifier runtimeSnapshotVerifier,
    AgentRuntimeEventRecorder runtimeEventRecorder,
    IEnumerable<IAgentToolExecutor> toolExecutors,
    AgentTaskPlanFreshReadGate freshReadGate,
    AgentNodeRunMaterializer? nodeRunMaterializer = null,
    NodeRunClaimCoordinator? nodeRunClaimCoordinator = null,
    NodeCheckpointCoordinator? nodeCheckpointCoordinator = null,
    IAgentNodeRunStore? nodeRunStore = null,
    AgentRuntimeWriteAuthorityAccessor? writeAuthorityAccessor = null,
    AgentArtifactReferenceEvidenceResolver? artifactReferenceEvidenceResolver = null,
    AgentArtifactFileSetCheckpointGate? artifactFileSetCheckpointGate = null,
    AgentFinalizationNodeExecutor? finalizationNodeExecutor = null,
    IOptions<AgentRunQueueOptions>? runQueueOptions = null,
    IBusinessDatabaseReadService? businessDatabaseReadService = null,
    IBusinessTextToSqlRuntime? businessTextToSqlRuntime = null,
    CloudReadOnlyTextToSqlFallbackRunner? cloudTextToSqlFallbackRunner = null)
    : IAgentTaskRuntime
{
    private readonly AgentTaskRunAttemptCoordinator runAttemptCoordinator = new(
        taskRepository,
        runAttemptStore,
        runQueueOptions);

    private readonly AgentBuiltInToolDispatcher builtInToolDispatcher = new(
        uploadRepository,
        workspaceService,
        fileStorage,
        tableFileParser,
        knowledgeRetrievalService,
        knowledgeBaseAccessCheckers,
        cloudReadonlyToolExecutor,
        identityAccessService,
        businessDatabaseReadService,
        businessTextToSqlRuntime,
        cloudTextToSqlFallbackRunner,
        new AgentRuntimeArtifactBuilder(workspaceService, documentGenerator));

    public Task<Result<AgentTask>> RunAsync(AgentTask task, CancellationToken cancellationToken = default)
    {
        return RunAsync(task, AgentTaskRunTriggerType.Manual, cancellationToken);
    }

    public async Task<Result<AgentTask>> RunAsync(
        AgentTask task,
        AgentTaskRunTriggerType triggerType = AgentTaskRunTriggerType.Manual,
        CancellationToken cancellationToken = default)
    {
        return await RunCoreAsync(task, triggerType, null, cancellationToken);
    }

    public Task<Result<AgentTask>> RunClaimedAsync(
        DurableTaskClaim claim,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(claim);
        return RunCoreAsync(
            claim.Task,
            claim.QueueItem.TriggerType,
            claim,
            cancellationToken);
    }

    private async Task<Result<AgentTask>> RunCoreAsync(
        AgentTask task,
        AgentTaskRunTriggerType triggerType,
        DurableTaskClaim? durableClaim,
        CancellationToken cancellationToken)
    {
        var integrity = await freshReadGate.VerifyAsync(
            task,
            requireExecutable: true,
            cancellationToken);
        if (!integrity.IsSuccess)
        {
            return Result.From(integrity);
        }

        var plan = DeserializePlan(task.PlanJson);
        var snapshot = await runtimeSnapshotVerifier.VerifyAsync(plan, task.UserId, cancellationToken);
        if (!snapshot.IsSuccess)
        {
            return Result.From(snapshot);
        }

        AgentFinalizationCheckpointState? approvedFinalization = null;
        if (task.Status == AgentTaskStatus.WaitingFinalApproval)
        {
            var checkpointState = await ValidateFinalizationCheckpointAsync(
                task,
                durableClaim,
                cancellationToken);
            if (!checkpointState.IsSuccess)
            {
                return Result.From(checkpointState);
            }

            if (durableClaim is null ||
                checkpointState.Value!.Phase == AgentFinalizationCheckpointPhase.PendingApproval)
            {
                return Result.Success(task);
            }

            approvedFinalization = checkpointState.Value;
        }

        var claimedAttempt = durableClaim?.RunAttempt;
        AgentTaskRunAttempt attempt;
        if (claimedAttempt is null)
        {
            var attemptResult = await runAttemptCoordinator.BeginOrResumeAttemptAsync(
                task,
                triggerType,
                cancellationToken);
            if (!attemptResult.IsSuccess)
            {
                return Result.From(attemptResult);
            }

            attempt = attemptResult.Value!;
        }
        else
        {
            var nowUtc = DateTimeOffset.UtcNow;
            if (task.ActiveRunAttemptId != claimedAttempt.Id ||
                task.RunFencingToken <= 0 ||
                task.RunFencingToken != claimedAttempt.TaskFencingToken ||
                task.RunLeaseId != claimedAttempt.LeaseId ||
                task.RunLeaseExpiresAt is null ||
                task.RunLeaseExpiresAt <= nowUtc ||
                !claimedAttempt.HasActiveLease(nowUtc))
            {
                return Result.Failure(new ApiProblemDescriptor(
                    AppProblemCodes.AgentTaskRunFenceStale,
                    "Durable task claim is stale and cannot enter runtime execution."));
            }

            attempt = claimedAttempt;
        }
        var now = DateTimeOffset.UtcNow;
        if (task.Status is AgentTaskStatus.PlanApproved or AgentTaskStatus.WaitingToolApproval)
        {
            task.Start(now);
        }

        if (task.Status is not AgentTaskStatus.Running
            and not AgentTaskStatus.GeneratingArtifacts
            and not AgentTaskStatus.WaitingFinalApproval)
        {
            return Result.Invalid("Only approved or running agent tasks can be executed.");
        }

        var workspace = await LoadWorkspaceAsync(task, cancellationToken);
        var state = new AgentTaskRunState();
        IReadOnlyDictionary<int, AgentNodeRun>? durableNodesByStep = null;
        IReadOnlyCollection<AgentEvidenceRecord> durableEvidence = [];
        var durableOutputByNodeRun = new Dictionary<AgentNodeRunId, string>();
        if (durableClaim is not null)
        {
            if (nodeRunMaterializer is null || nodeRunClaimCoordinator is null ||
                nodeCheckpointCoordinator is null || nodeRunStore is null)
            {
                return Result.Failure(new ApiProblemDescriptor(
                    AppProblemCodes.AgentNodeRunStateConflict,
                    "Durable NodeRun runtime services are unavailable."));
            }

            var materialized = await nodeRunMaterializer.EnsureMaterializedAsync(
                durableClaim,
                plan,
                DateTimeOffset.UtcNow,
                cancellationToken);
            durableNodesByStep = plan.Nodes!
                .Select((node, index) => new
                {
                    StepIndex = index + 1,
                    Node = materialized.Single(runtimeNode =>
                        string.Equals(runtimeNode.NodeId, node.NodeId, StringComparison.Ordinal))
                })
                .ToDictionary(item => item.StepIndex, item => item.Node);
            durableEvidence = await nodeRunStore.ListEvidenceByAttemptAsync(
                durableClaim.RunAttempt.Id,
                cancellationToken);
            foreach (var evidence in durableEvidence.OrderBy(item => item.CreatedAt))
            {
                if (evidence.StorageMode == AgentEvidenceStorageMode.InlineCanonicalJson &&
                    !string.IsNullOrWhiteSpace(evidence.InlinePayloadJson))
                {
                    durableOutputByNodeRun[evidence.NodeRunId] =
                        AgentTaskRunStateCheckpointCodec.RestoreEvidencePayload(
                            state,
                            evidence.InlinePayloadJson);
                    continue;
                }

                var evidenceNode = materialized.SingleOrDefault(node => node.Id == evidence.NodeRunId);
                if (evidence.StorageMode != AgentEvidenceStorageMode.ArtifactReference ||
                    evidenceNode is null || artifactReferenceEvidenceResolver is null)
                {
                    return Result.Failure(new ApiProblemDescriptor(
                        AppProblemCodes.AgentNodeRunStateConflict,
                        "Durable runtime recovery requires complete authorized Evidence payloads."));
                }

                var resolved = await artifactReferenceEvidenceResolver.ResolveDurableOutputAsync(
                    task,
                    workspace,
                    evidenceNode,
                    evidence,
                    cancellationToken);
                if (!resolved.IsSuccess)
                {
                    return Result.From(resolved);
                }

                durableOutputByNodeRun[evidence.NodeRunId] = resolved.Value!;
            }
        }

        var executorResolver = CreateExecutorResolver();

        foreach (var step in task.Steps.OrderBy(step => step.StepIndex))
        {
            var durableNode = durableNodesByStep?.GetValueOrDefault(step.StepIndex);
            var nodeContract = plan.Nodes?.ElementAtOrDefault(step.StepIndex - 1);
            if (durableNode?.Status == AgentNodeRunStatus.Succeeded)
            {
                var evidence = durableEvidence.SingleOrDefault(item => item.NodeRunId == durableNode.Id);
                if (evidence is null ||
                    !durableOutputByNodeRun.TryGetValue(durableNode.Id, out var durableOutput))
                {
                    return Result.Failure(new ApiProblemDescriptor(
                        AppProblemCodes.AgentNodeRunStateConflict,
                        $"Succeeded NodeRun '{durableNode.NodeId}' is missing authoritative Evidence."));
                }

                if (step.Status != AgentStepStatus.Completed)
                {
                    if (step.Status is AgentStepStatus.Pending or AgentStepStatus.Approved)
                    {
                        step.Start(DateTimeOffset.UtcNow);
                    }

                    step.Complete(durableOutput, DateTimeOffset.UtcNow);
                }

                continue;
            }

            if (step.Status == AgentStepStatus.Completed)
            {
                if (durableNode is not null)
                {
                    return Result.Failure(new ApiProblemDescriptor(
                        AppProblemCodes.AgentNodeRunStateConflict,
                        $"Step {step.StepIndex} is complete but its NodeRun checkpoint is not authoritative."));
                }

                continue;
            }

            await runAttemptCoordinator.RefreshRunLeaseAsync(task, attempt, cancellationToken);
            var toolDecision = await toolRegistryGuard.ValidateAsync(
                step.ToolCode,
                task.UserId,
                cancellationToken);
            if (!toolDecision.IsAllowed)
            {
                return await RejectStepAsync(task, workspace, step, attempt, toolDecision.Problem!, cancellationToken);
            }

            var toolRegistration = toolDecision.Tool!;
            if (plan.PluginSelectionMode != AgentPluginSelectionMode.BuiltInOnly ||
                toolRegistration.TargetType != ToolRegistrationTargetType.AgentRuntime ||
                toolRegistration.ProviderType is ToolProviderType.Mcp or ToolProviderType.MockMcp)
            {
                return await RejectStepAsync(
                    task,
                    workspace,
                    step,
                    attempt,
                    new ApiProblemDescriptor(
                        AppProblemCodes.AgentPlanToolDenied,
                        $"Tool '{toolRegistration.ToolCode}' is outside BuiltInOnly runtime scope."),
                    cancellationToken);
            }

            if (RequiresRuntimeApproval(step, toolRegistration) && step.Status == AgentStepStatus.Pending)
            {
                step.WaitForApproval();
            }

            if (step.Status == AgentStepStatus.WaitingApproval)
            {
                if (BuiltInToolRegistrations.IsLifecycleCheckpoint(step.ToolCode))
                {
                    if (workspace.Artifacts.Count == 0)
                    {
                        return await RejectStepAsync(
                            task,
                            workspace,
                            step,
                            attempt,
                            new ApiProblemDescriptor(
                                AppProblemCodes.AgentFinalizationStateConflict,
                                "Final-output checkpoint requires at least one persisted workspace artifact."),
                            cancellationToken);
                    }

                    var finalApprovalResolution = await ResolveFinalOutputApprovalAsync(
                        task,
                        workspace.WorkspaceCode,
                        cancellationToken);
                    if (!finalApprovalResolution.IsSuccess)
                    {
                        return await RejectStepAsync(
                            task,
                            workspace,
                            step,
                            attempt,
                            finalApprovalResolution.Errors!
                                .OfType<ApiProblemDescriptor>()
                                .Single(),
                            cancellationToken);
                    }

                    var approvalResolution = finalApprovalResolution.Value!;
                    var approval = approvalResolution.Approval;
                    task.MarkWorkspaceReady(now);
                    task.WaitForFinalApproval(now);
                    attempt.WaitForApproval(now, "Waiting for final output approval.");
                    task.ReleaseRunLease(now, clearActiveAttempt: false);
                    if (approvalResolution.IsCreated)
                    {
                        await runtimeEventRecorder.StageFinalReviewSubmittedAsync(
                            task,
                            workspace,
                            approval,
                            cancellationToken);
                    }

                    await SaveAsync(task, workspace, attempt, cancellationToken);
                    return Result.Success(task);
                }
                else
                {
                    var stepTargetId = step.Id.Value.ToString();
                    if (await HasApprovedApprovalAsync(task, AgentApprovalType.ToolCall, stepTargetId, cancellationToken))
                    {
                        step.Approve();
                        if (durableClaim is not null && durableNode is not null &&
                            durableNode.Status == AgentNodeRunStatus.WaitingApproval)
                        {
                            var released = await nodeRunMaterializer!.ReleaseApprovedNodeAsync(
                                durableNode.Id,
                                durableClaim,
                                DateTimeOffset.UtcNow,
                                cancellationToken);
                            if (released != AgentFencedWriteResult.Succeeded)
                            {
                                return Result.Failure(new ApiProblemDescriptor(
                                    released == AgentFencedWriteResult.StaleFence
                                        ? AppProblemCodes.AgentNodeRunFenceStale
                                        : AppProblemCodes.AgentNodeRunStateConflict,
                                    "Approved NodeRun could not become runnable under the current task fence."));
                            }
                        }
                    }
                    else
                    {
                        if (await HasCompetingPendingApprovalAsync(
                                task,
                                AgentApprovalType.ToolCall,
                                stepTargetId,
                                cancellationToken))
                        {
                            return await RejectStepAsync(
                                task,
                                workspace,
                                step,
                                attempt,
                                new ApiProblemDescriptor(
                                    AppProblemCodes.AgentApprovalStateConflict,
                                    "Tool-call checkpoint has another pending task approval."),
                                cancellationToken);
                        }

                        var approvalResolution = await EnsureApprovalRequestAsync(
                            task,
                            AgentApprovalType.ToolCall,
                            stepTargetId,
                            cancellationToken);
                        var approval = approvalResolution.Approval;
                        task.WaitForToolApproval(now);
                        attempt.WaitForApproval(now, "Waiting for tool approval.");
                        task.ReleaseRunLease(now, clearActiveAttempt: false);
                        if (approvalResolution.IsCreated)
                        {
                            await runtimeEventRecorder.StageApprovalRequestedAsync(task, approval, cancellationToken);
                        }

                        await SaveAsync(task, workspace, attempt, cancellationToken);
                        return Result.Success(task);
                    }
                }
            }

            if (step.Status is not AgentStepStatus.Pending and not AgentStepStatus.Approved)
            {
                continue;
            }

            if (BuiltInToolRegistrations.IsLifecycleCheckpoint(step.ToolCode) &&
                step.Status == AgentStepStatus.Approved)
            {
                if (approvedFinalization is null ||
                    durableClaim is null ||
                    durableNode is null ||
                    nodeContract is null)
                {
                    return Result.Failure(new ApiProblemDescriptor(
                        AppProblemCodes.AgentFinalizationStateConflict,
                        "Approved final-output checkpoint is missing durable execution authority."));
                }

                return await ExecuteApprovedFinalizationAsync(
                    task,
                    workspace,
                    step,
                    attempt,
                    toolRegistration,
                    approvedFinalization,
                    durableClaim,
                    durableNode,
                    nodeContract,
                    durableEvidence,
                    cancellationToken);
            }

            if (BuiltInToolRegistrations.IsLifecycleCheckpoint(step.ToolCode))
            {
                return await RejectStepAsync(
                    task,
                    workspace,
                    step,
                    attempt,
                    new ApiProblemDescriptor(
                        AppProblemCodes.AgentPlanToolDenied,
                        "Final output is a lifecycle checkpoint and cannot be dispatched as a provider tool."),
                    cancellationToken);
            }

            AgentNodeRunClaim? activeNodeClaim = null;
            var nodeExecutionStartedAt = DateTimeOffset.UtcNow;
            if (durableClaim is not null)
            {
                if (durableNode is null || nodeContract is null)
                {
                    return Result.Failure(new ApiProblemDescriptor(
                        AppProblemCodes.AgentNodeRunStateConflict,
                        $"Step {step.StepIndex} has no materialized NodeRun contract."));
                }

                var claimedNode = await nodeRunClaimCoordinator!.ClaimNextAsync(
                    durableClaim.RunAttempt.Id,
                    durableClaim.TaskFencingToken,
                    durableClaim.RunAttempt.LeaseOwner ?? "agent-node-runtime",
                    (runQueueOptions?.Value ?? new AgentRunQueueOptions()).LeaseDuration,
                    nodeExecutionStartedAt,
                    cancellationToken);
                if (!claimedNode.IsSuccess || claimedNode.Value is null ||
                    claimedNode.Value.NodeRun.Id != durableNode.Id)
                {
                    return Result.Failure(new ApiProblemDescriptor(
                        AppProblemCodes.AgentNodeRunStateConflict,
                        $"Runnable NodeRun for step {step.StepIndex} could not be claimed deterministically."));
                }

                activeNodeClaim = claimedNode.Value;
                var running = await nodeRunClaimCoordinator.MarkRunningAsync(
                    activeNodeClaim,
                    nodeExecutionStartedAt,
                    cancellationToken);
                if (!running.IsSuccess)
                {
                    return Result.From(running);
                }
            }

            AgentToolExecutionAuditScope? executionScope = null;
            var toolDispatchStarted = false;
            var nodeCheckpointCommitted = false;
            try
            {
                executionScope = runtimeEventRecorder.BeginToolExecution(
                    task,
                    step,
                    toolRegistration,
                    attempt,
                    DateTimeOffset.UtcNow);

                var inputValidation = ToolInputSchemaValidator.ValidateAndParse(
                    step.InputJson,
                    toolRegistration.InputSchemaJson);
                if (!inputValidation.IsValid)
                {
                    throw new AgentToolExecutionException(
                        AppProblemCodes.AgentPlanSchemaInvalid,
                        inputValidation.Error ?? "Agent step input does not match registry schema.");
                }

                step.Start(DateTimeOffset.UtcNow);
                await runtimeEventRecorder.StageStepStartedAsync(task, step, cancellationToken);

                if (task.Status == AgentTaskStatus.Running &&
                    step.StepType is AgentStepType.ChartGeneration or AgentStepType.ArtifactGeneration)
                {
                    task.BeginArtifactGeneration(DateTimeOffset.UtcNow);
                }

                var executor = executorResolver.Resolve(toolRegistration, step);
                var executionContext = new AgentToolExecutionContext(
                    task,
                    workspace,
                    plan,
                    step,
                    state,
                    toolRegistration,
                    cancellationToken);
                using var writeAuthorityScope = activeNodeClaim is null || writeAuthorityAccessor is null
                    ? null
                    : writeAuthorityAccessor.Push(new AgentRuntimeWriteAuthority(
                        activeNodeClaim.NodeRun.Id,
                        activeNodeClaim.TaskFencingToken,
                        activeNodeClaim.NodeFencingToken));
                toolDispatchStarted = true;
                var executionResult = activeNodeClaim is null
                    ? await ExecuteWithTimeoutAsync(executor, executionContext)
                    : await ExecuteWithLeaseRenewalAsync(
                        executor,
                        executionContext,
                        activeNodeClaim,
                        cancellationToken);
                var outputValidation = AgentToolRuntimeOutputGate.Validate(
                    toolRegistration,
                    executionResult);
                if (!outputValidation.IsValid)
                {
                    throw new AgentToolExecutionException(
                        outputValidation.IsPayloadTooLarge
                            ? AppProblemCodes.EvidencePayloadTooLarge
                            : AppProblemCodes.ToolOutputSchemaInvalid,
                        outputValidation.Error ?? "Tool output does not match the registry schema.");
                }

                var artifactBinding = AgentArtifactOutputBindingGate.Validate(
                    task,
                    workspace,
                    step,
                    toolRegistration,
                    executionResult.ContractOutput);
                if (!artifactBinding.IsValid)
                {
                    throw new AgentToolExecutionException(
                        AppProblemCodes.ToolOutputSchemaInvalid,
                        artifactBinding.Error ?? "Artifact tool output is not bound to the workspace aggregate.");
                }

                if (durableClaim is not null && activeNodeClaim is not null && nodeContract is not null)
                {
                    if (activeNodeClaim.NodeRun.SideEffectClass == AgentNodeSideEffectClass.ArtifactWrite)
                    {
                        if (artifactFileSetCheckpointGate is null)
                        {
                            throw new AgentToolExecutionException(
                                AppProblemCodes.AgentNodeRunStateConflict,
                                "ArtifactWrite checkpoint gate is unavailable.");
                        }

                        var artifactCheckpoint = await artifactFileSetCheckpointGate.ValidateAsync(
                            durableClaim,
                            activeNodeClaim,
                            workspace,
                            step,
                            cancellationToken);
                        if (!artifactCheckpoint.IsSuccess)
                        {
                            var problem = artifactCheckpoint.Errors!
                                .OfType<ApiProblemDescriptor>()
                                .FirstOrDefault();
                            throw new AgentToolExecutionException(
                                problem?.Code ?? AppProblemCodes.AgentNodeRunStateConflict,
                                problem?.Detail ?? "ArtifactWrite file-set checkpoint is not authoritative.");
                        }
                    }

                    var parentEvidence = await nodeRunStore!.ListEvidenceByAttemptAsync(
                        durableClaim.RunAttempt.Id,
                        cancellationToken);
                    var normalized = AgentEvidenceNormalizer.Normalize(
                        durableClaim,
                        activeNodeClaim,
                        nodeContract,
                        toolRegistration,
                        step,
                        executionResult,
                        state,
                        workspace,
                        parentEvidence,
                        DateTimeOffset.UtcNow - nodeExecutionStartedAt,
                        DateTimeOffset.UtcNow);
                    if (!normalized.IsSuccess)
                    {
                        var problem = normalized.Errors!
                            .OfType<ApiProblemDescriptor>()
                            .FirstOrDefault();
                        throw new AgentToolExecutionException(
                            problem?.Code ?? AppProblemCodes.AgentPlanInvalid,
                            problem?.Detail ?? "Node output could not be normalized as Evidence v1.");
                    }

                    var checkpoint = normalized.Value!;
                    var committed = await nodeCheckpointCoordinator!.CommitSuccessAsync(
                        new AgentNodeSuccessCheckpoint(
                            durableClaim.Task.Id,
                            durableClaim.RunAttempt.Id,
                            activeNodeClaim.NodeRun.Id,
                            durableClaim.TaskFencingToken,
                            activeNodeClaim.NodeFencingToken,
                            checkpoint.Evidence,
                            checkpoint.Usage,
                            checkpoint.OutputDigest,
                            toolRegistration.ToolCode,
                            ProviderReceiptHash: null,
                            DateTimeOffset.UtcNow),
                        cancellationToken);
                    if (!committed.IsSuccess)
                    {
                        var problem = committed.Errors!
                            .OfType<ApiProblemDescriptor>()
                            .FirstOrDefault();
                        throw new AgentToolExecutionException(
                            problem?.Code ?? AppProblemCodes.AgentNodeRunStateConflict,
                            problem?.Detail ?? "Node checkpoint could not be committed.");
                    }

                    nodeCheckpointCommitted = true;
                    durableEvidence = parentEvidence.Append(checkpoint.Evidence).ToArray();
                }

                step.Complete(executionResult.DurableOutput.CanonicalJson, DateTimeOffset.UtcNow);
                var artifactId = runtimeEventRecorder.MarkToolExecutionSucceeded(
                    executionScope,
                    task,
                    workspace,
                    step,
                    toolRegistration,
                    executionResult.DurableOutput.ToJsonElement(),
                    DateTimeOffset.UtcNow);
                await runtimeEventRecorder.StageStepCompletedAsync(task, step, cancellationToken);

                await runtimeEventRecorder.RecordToolSucceededAsync(
                    task,
                    workspace,
                    step,
                    artifactId,
                    cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (PersistenceCommitOutcomeUnknownException)
            {
                throw;
            }
            catch (Exception ex)
            {
                if (nodeCheckpointCommitted)
                {
                    return Result.Success(task);
                }

                var safeMessage = AgentToolExecutionAuditBuilder.BuildSafeExceptionSummary(ex);
                var errorCode = AgentToolExecutionAuditBuilder.ResolveExecutionErrorCode(ex, step, toolRegistration);
                if (durableClaim is not null && activeNodeClaim is not null)
                {
                    var sideEffecting = activeNodeClaim.NodeRun.SideEffectClass is
                        AgentNodeSideEffectClass.ArtifactWrite or
                        AgentNodeSideEffectClass.ExternalIdempotent or
                        AgentNodeSideEffectClass.ExternalOutcomeUnknown;
                    if (sideEffecting && toolDispatchStarted)
                    {
                        var unknown = await nodeCheckpointCoordinator!.CommitOutcomeUnknownAsync(
                            new AgentNodeOutcomeUnknownCheckpoint(
                                durableClaim.Task.Id,
                                durableClaim.RunAttempt.Id,
                                activeNodeClaim.NodeRun.Id,
                                durableClaim.TaskFencingToken,
                                activeNodeClaim.NodeFencingToken,
                                toolRegistration.ToolCode,
                                ProviderReceiptHash: null,
                                ReconciliationPolicy: "provider-receipt-or-manual-v1",
                                LastConfirmedStage: "tool-dispatched-result-unconfirmed",
                                IntegrityStatus: "not-confirmed",
                                safeMessage,
                                DateTimeOffset.UtcNow.AddMinutes(1),
                                DateTimeOffset.UtcNow.AddHours(24),
                                DateTimeOffset.UtcNow),
                            cancellationToken);
                        if (!unknown.IsSuccess)
                        {
                            return Result.From(unknown);
                        }

                        step.Fail(safeMessage, DateTimeOffset.UtcNow);
                        await runtimeEventRecorder.RecordToolFailedAsync(
                            executionScope,
                            task,
                            workspace,
                            step,
                            toolRegistration,
                            attempt,
                            errorCode,
                            safeMessage,
                            DateTimeOffset.UtcNow,
                            cancellationToken);
                        await SaveAsync(task, workspace, attempt, cancellationToken);
                        return Result.Success(task);
                    }

                    var failed = await nodeCheckpointCoordinator!.CommitFailureAsync(
                        new AgentNodeFailureCheckpoint(
                            durableClaim.Task.Id,
                            durableClaim.RunAttempt.Id,
                            activeNodeClaim.NodeRun.Id,
                            durableClaim.TaskFencingToken,
                            activeNodeClaim.NodeFencingToken,
                            errorCode,
                            safeMessage,
                            CreateFailureUsage(
                                durableClaim,
                                activeNodeClaim,
                                toolDispatchStarted,
                                nodeExecutionStartedAt,
                                DateTimeOffset.UtcNow),
                            DateTimeOffset.UtcNow,
                            RetryAtUtc: null),
                        cancellationToken);
                    if (!failed.IsSuccess)
                    {
                        return Result.From(failed);
                    }
                }

                step.Fail(safeMessage, DateTimeOffset.UtcNow);
                await runtimeEventRecorder.RecordToolFailedAsync(
                    executionScope,
                    task,
                    workspace,
                    step,
                    toolRegistration,
                    attempt,
                    errorCode,
                    safeMessage,
                    DateTimeOffset.UtcNow,
                    cancellationToken);
                task.Fail($"步骤 {step.StepIndex} 执行失败：{safeMessage}", DateTimeOffset.UtcNow);
                attempt.MarkFailed(errorCode, safeMessage, DateTimeOffset.UtcNow);
                task.ReleaseRunLease(DateTimeOffset.UtcNow, clearActiveAttempt: true);
                await SaveAsync(task, workspace, attempt, cancellationToken);
                return Result.Success(task);
            }
        }

        var failedAt = DateTimeOffset.UtcNow;
        const string message =
            "Agent plan did not pause at the canonical final-output checkpoint; no final approval was created.";
        task.Fail(message, failedAt);
        attempt.MarkFailed(AppProblemCodes.AgentFinalizationStateConflict, message, failedAt);
        task.ReleaseRunLease(failedAt, clearActiveAttempt: true);
        await SaveAsync(task, workspace, attempt, cancellationToken);
        return Result.Success(task);
    }

    private async Task<Result<AgentTask>> ExecuteApprovedFinalizationAsync(
        AgentTask task,
        ArtifactWorkspace workspace,
        AgentStep step,
        AgentTaskRunAttempt attempt,
        ToolRegistration toolRegistration,
        AgentFinalizationCheckpointState finalizationState,
        DurableTaskClaim durableClaim,
        AgentNodeRun durableNode,
        AgentPlanNodeDocument nodeContract,
        IReadOnlyCollection<AgentEvidenceRecord> durableEvidence,
        CancellationToken cancellationToken)
    {
        if (finalizationNodeExecutor is null ||
            nodeRunMaterializer is null ||
            nodeRunClaimCoordinator is null ||
            nodeCheckpointCoordinator is null ||
            finalizationState.Phase != AgentFinalizationCheckpointPhase.Approved ||
            finalizationState.Step.Id != step.Id ||
            finalizationState.ActiveAttempt.Id != attempt.Id)
        {
            return Result.Failure(new ApiProblemDescriptor(
                AppProblemCodes.AgentFinalizationStateConflict,
                "Final-output execution services or approved checkpoint authority are unavailable."));
        }

        if (durableNode.Status == AgentNodeRunStatus.WaitingApproval)
        {
            var released = await nodeRunMaterializer.ReleaseApprovedNodeAsync(
                durableNode.Id,
                durableClaim,
                DateTimeOffset.UtcNow,
                cancellationToken);
            if (released != AgentFencedWriteResult.Succeeded)
            {
                return Result.Failure(new ApiProblemDescriptor(
                    released == AgentFencedWriteResult.StaleFence
                        ? AppProblemCodes.AgentNodeRunFenceStale
                        : AppProblemCodes.AgentNodeRunStateConflict,
                    "Approved final-output NodeRun could not become runnable under the active task fence."));
            }
        }
        else if (durableNode.Status != AgentNodeRunStatus.Runnable)
        {
            return Result.Failure(new ApiProblemDescriptor(
                AppProblemCodes.AgentNodeRunStateConflict,
                "Approved final-output NodeRun is not runnable."));
        }

        var nodeExecutionStartedAt = DateTimeOffset.UtcNow;
        var claimed = await nodeRunClaimCoordinator.ClaimNextAsync(
            durableClaim.RunAttempt.Id,
            durableClaim.TaskFencingToken,
            durableClaim.RunAttempt.LeaseOwner ?? "agent-finalization-runtime",
            (runQueueOptions?.Value ?? new AgentRunQueueOptions()).LeaseDuration,
            nodeExecutionStartedAt,
            cancellationToken);
        if (!claimed.IsSuccess || claimed.Value is null || claimed.Value.NodeRun.Id != durableNode.Id)
        {
            return claimed.IsSuccess
                ? Result.Failure(new ApiProblemDescriptor(
                    AppProblemCodes.AgentNodeRunStateConflict,
                    "Final-output NodeRun could not be claimed deterministically."))
                : Result.From(claimed);
        }

        var nodeClaim = claimed.Value;
        var running = await nodeRunClaimCoordinator.MarkRunningAsync(
            nodeClaim,
            nodeExecutionStartedAt,
            cancellationToken);
        if (!running.IsSuccess)
        {
            return Result.From(running);
        }

        Result<AgentFinalizationNodeExecutionResult> finalized;
        try
        {
            finalized = await finalizationNodeExecutor.ExecuteAsync(
                durableClaim,
                nodeClaim,
                nodeContract,
                workspace,
                step,
                finalizationState.Approval,
                durableEvidence,
                nodeExecutionStartedAt,
                cancellationToken);
        }
        catch (PersistenceCommitOutcomeUnknownException)
        {
            throw;
        }

        if (finalized.IsSuccess)
        {
            return Result.Success(task);
        }

        var problem = finalized.Errors?
            .OfType<ApiProblemDescriptor>()
            .FirstOrDefault() ?? new ApiProblemDescriptor(
                AppProblemCodes.AgentFinalizationStateConflict,
                "Final-output NodeRun failed before its authoritative checkpoint completed.");
        var failedAt = DateTimeOffset.UtcNow;
        var failed = await nodeCheckpointCoordinator.CommitFailureAsync(
            new AgentNodeFailureCheckpoint(
                task.Id,
                attempt.Id,
                nodeClaim.NodeRun.Id,
                durableClaim.TaskFencingToken,
                nodeClaim.NodeFencingToken,
                problem.Code,
                problem.Detail,
                CreateFailureUsage(
                    durableClaim,
                    nodeClaim,
                    toolDispatchStarted: true,
                    startedAtUtc: nodeExecutionStartedAt,
                    nowUtc: failedAt),
                failedAt,
                RetryAtUtc: null),
            cancellationToken);
        if (!failed.IsSuccess)
        {
            return Result.From(failed);
        }

        step.Fail(problem.Detail, failedAt);
        await runtimeEventRecorder.RecordToolFailedAsync(
            null,
            task,
            workspace,
            step,
            toolRegistration,
            attempt,
            problem.Code,
            problem.Detail,
            failedAt,
            cancellationToken);
        task.Fail($"最终产物确认失败：{problem.Detail}", failedAt);
        attempt.MarkFailed(problem.Code, problem.Detail, failedAt);
        task.ReleaseRunLease(failedAt, clearActiveAttempt: true);
        await SaveAsync(task, workspace, attempt, cancellationToken);
        return Result.Success(task);
    }

    private async Task<Result<AgentFinalizationCheckpointState>> ValidateFinalizationCheckpointAsync(
        AgentTask task,
        DurableTaskClaim? durableClaim,
        CancellationToken cancellationToken)
    {
        if (task.WorkspaceId is null)
        {
            return Result.Failure(new ApiProblemDescriptor(
                AppProblemCodes.AgentFinalizationStateConflict,
                "Final-output checkpoint workspace is missing."));
        }

        var workspace = await workspaceRepository.FirstOrDefaultAsync(
            new ArtifactWorkspaceByIdSpec(task.WorkspaceId.Value, includeArtifacts: true),
            cancellationToken);
        var approvals = await approvalRepository.ListAsync(
            new ApprovalRequestsByTaskSpec(task.Id),
            cancellationToken);
        var attempts = await runAttemptStore.ListByTaskAsync(task.Id, cancellationToken);
        return durableClaim is null
            ? AgentFinalizationCheckpointStateValidator.ValidatePaused(
                task,
                workspace,
                approvals,
                attempts)
            : AgentFinalizationCheckpointStateValidator.ValidateResumed(
                task,
                workspace,
                approvals,
                attempts,
                durableClaim,
                DateTimeOffset.UtcNow);
    }

    private AgentToolExecutorResolver CreateExecutorResolver()
    {
        return new AgentToolExecutorResolver(
            toolExecutors.Append(new RuntimeBuiltInAgentToolExecutor(builtInToolDispatcher.ExecuteAsync)));
    }

    private static async Task<AgentToolExecutionResult> ExecuteWithTimeoutAsync(
        IAgentToolExecutor executor,
        AgentToolExecutionContext context)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(context.ToolRegistration.TimeoutSeconds));

        try
        {
            return await executor.ExecuteAsync(context with { CancellationToken = timeoutCts.Token });
        }
        catch (OperationCanceledException) when (!context.CancellationToken.IsCancellationRequested)
        {
            throw new AgentToolExecutionException(
                AppProblemCodes.ToolExecutionTimeout,
                $"Tool '{context.ToolRegistration.ToolCode}' exceeded timeout {context.ToolRegistration.TimeoutSeconds} seconds.");
        }
    }

    private static AgentRunUsageLedgerEntry CreateFailureUsage(
        DurableTaskClaim taskClaim,
        AgentNodeRunClaim nodeClaim,
        bool toolDispatchStarted,
        DateTimeOffset startedAtUtc,
        DateTimeOffset nowUtc)
    {
        var elapsed = Math.Max(0, (long)(nowUtc - startedAtUtc).TotalMilliseconds);
        elapsed = Math.Min(elapsed, nodeClaim.NodeRun.ReservedElapsedMilliseconds);
        return new AgentRunUsageLedgerEntry(
            taskClaim.Task.Id,
            taskClaim.RunAttempt.Id,
            nodeClaim.NodeRun.Id,
            taskClaim.TaskFencingToken,
            nodeClaim.NodeFencingToken,
            inputTokens: 0,
            outputTokens: 0,
            modelCalls: 0,
            toolCalls: toolDispatchStarted ? 1 : 0,
            elapsedMilliseconds: elapsed,
            costAmount: 0m,
            artifactCount: 0,
            artifactBytes: 0,
            costCurrency: taskClaim.RunAttempt.BudgetCostCurrency,
            correlationHash: CanonicalJson.ComputeSha256(CanonicalJson.Serialize(new
            {
                taskClaim.TaskFencingToken,
                nodeClaim.NodeFencingToken,
                outcome = "known-failure",
                toolDispatchStarted
            })),
            nowUtc);
    }

    private async Task<AgentToolExecutionResult> ExecuteWithLeaseRenewalAsync(
        IAgentToolExecutor executor,
        AgentToolExecutionContext context,
        AgentNodeRunClaim nodeClaim,
        CancellationToken cancellationToken)
    {
        if (nodeRunClaimCoordinator is null)
        {
            throw new AgentToolExecutionException(
                AppProblemCodes.AgentNodeRunStateConflict,
                "NodeRun lease coordinator is unavailable.");
        }

        var leaseDuration = (runQueueOptions?.Value ?? new AgentRunQueueOptions()).LeaseDuration;
        var renewalMilliseconds = Math.Clamp(
            leaseDuration.TotalMilliseconds / 3d,
            100d,
            30_000d);
        var renewalInterval = TimeSpan.FromMilliseconds(renewalMilliseconds);
        using var executionCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var execution = ExecuteWithTimeoutAsync(
            executor,
            context with { CancellationToken = executionCts.Token });
        while (!execution.IsCompleted)
        {
            var delay = Task.Delay(renewalInterval, cancellationToken);
            var completed = await Task.WhenAny(execution, delay);
            if (completed == execution)
            {
                break;
            }

            var renewed = await nodeRunClaimCoordinator.RenewTaskAndNodeLeaseAsync(
                nodeClaim,
                leaseDuration,
                leaseDuration,
                DateTimeOffset.UtcNow,
                cancellationToken);
            if (!renewed.IsSuccess)
            {
                executionCts.Cancel();
                _ = execution.ContinueWith(
                    static task =>
                    {
                        _ = task.Exception;
                    },
                    CancellationToken.None,
                    TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);
                throw new AgentToolExecutionException(
                    AppProblemCodes.AgentNodeRunFenceStale,
                    "NodeRun lease renewal failed; stale worker execution was fenced.");
            }
        }

        return await execution;
    }

    private async Task<ArtifactWorkspace> LoadWorkspaceAsync(AgentTask task, CancellationToken cancellationToken)
    {
        if (task.WorkspaceId is null)
        {
            var created = await workspaceService.CreateForTaskAsync(task, DateTimeOffset.UtcNow, cancellationToken);
            task.AttachWorkspace(created.Id, DateTimeOffset.UtcNow);
            return created;
        }

        var workspace = await workspaceRepository.FirstOrDefaultAsync(
            new ArtifactWorkspaceByIdSpec(task.WorkspaceId.Value, includeArtifacts: true),
            cancellationToken);
        if (workspace is null)
        {
            throw new InvalidOperationException("Agent task workspace was not found.");
        }

        return workspace;
    }

    private async Task<ApprovalRequestResolution> EnsureApprovalRequestAsync(
        AgentTask task,
        AgentApprovalType approvalType,
        string targetId,
        CancellationToken cancellationToken)
    {
        var existing = await approvalRepository.FirstOrDefaultAsync(
            new PendingApprovalRequestByTaskAndTargetSpec(task.Id, approvalType, targetId),
            cancellationToken);
        if (existing is not null)
        {
            return new ApprovalRequestResolution(existing, IsCreated: false);
        }

        var approval = new ApprovalRequest(
            task.Id,
            approvalType,
            targetId,
            task.UserId,
            DateTimeOffset.UtcNow);
        approvalRepository.Add(approval);
        return new ApprovalRequestResolution(approval, IsCreated: true);
    }

    private async Task<Result<ApprovalRequestResolution>> ResolveFinalOutputApprovalAsync(
        AgentTask task,
        string workspaceCode,
        CancellationToken cancellationToken)
    {
        var approvals = await approvalRepository.ListAsync(
            new ApprovalRequestsByTaskSpec(task.Id),
            cancellationToken);
        var finalApprovals = approvals
            .Where(approval => approval.ApprovalType == AgentApprovalType.FinalOutput)
            .ToArray();
        var pendingApprovals = approvals
            .Where(approval => approval.Status == AgentApprovalStatus.Pending)
            .ToArray();
        if (finalApprovals.Length == 0 && pendingApprovals.Length == 0)
        {
            var approval = new ApprovalRequest(
                task.Id,
                AgentApprovalType.FinalOutput,
                workspaceCode,
                task.UserId,
                DateTimeOffset.UtcNow);
            approvalRepository.Add(approval);
            return Result.Success(new ApprovalRequestResolution(approval, IsCreated: true));
        }

        if (finalApprovals.Length == 1 &&
            finalApprovals[0].Status == AgentApprovalStatus.Pending &&
            string.Equals(finalApprovals[0].TargetId, workspaceCode, StringComparison.Ordinal) &&
            finalApprovals[0].RequestedBy == task.UserId &&
            pendingApprovals.Length == 1 &&
            pendingApprovals[0].Id == finalApprovals[0].Id)
        {
            var decisionProof = AgentFinalizationCheckpointStateValidator
                .ValidateApprovalDecisionProof(finalApprovals[0]);
            if (!decisionProof.IsSuccess)
            {
                return Result.From(decisionProof);
            }

            return Result.Success(new ApprovalRequestResolution(
                finalApprovals[0],
                IsCreated: false));
        }

        return Result.Failure(new ApiProblemDescriptor(
            AppProblemCodes.AgentApprovalStateConflict,
            "Final-output checkpoint requires no historical approval and no competing pending approval."));
    }

    private sealed record ApprovalRequestResolution(
        ApprovalRequest Approval,
        bool IsCreated);

    private async Task<bool> HasApprovedApprovalAsync(
        AgentTask task,
        AgentApprovalType approvalType,
        string targetId,
        CancellationToken cancellationToken)
    {
        var approvals = await approvalRepository.ListAsync(
            new ApprovalRequestsByTaskSpec(task.Id),
            cancellationToken);
        return approvals.Any(approval =>
            approval.ApprovalType == approvalType &&
            approval.TargetId == targetId &&
            approval.Status == AgentApprovalStatus.Approved);
    }

    private async Task<bool> HasCompetingPendingApprovalAsync(
        AgentTask task,
        AgentApprovalType approvalType,
        string targetId,
        CancellationToken cancellationToken)
    {
        var pending = await approvalRepository.ListAsync(
            new ApprovalRequestsByTaskSpec(task.Id, pendingOnly: true),
            cancellationToken);
        return pending.Count > 1 ||
               pending.Count == 1 &&
               (pending[0].ApprovalType != approvalType ||
                !string.Equals(pending[0].TargetId, targetId, StringComparison.Ordinal));
    }

    private async Task<Result<AgentTask>> RejectStepAsync(
        AgentTask task,
        ArtifactWorkspace workspace,
        AgentStep step,
        AgentTaskRunAttempt attempt,
        ApiProblemDescriptor problem,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var safeMessage = AgentToolExecutionAuditBuilder.SanitizeSummary(problem.Detail, 2000) ?? "Tool execution rejected.";
        await runtimeEventRecorder.RecordToolRejectedAsync(
            task,
            workspace,
            step,
            attempt,
            problem.Code,
            safeMessage,
            now,
            cancellationToken);

        step.Fail(safeMessage, now);
        task.Fail($"步骤 {step.StepIndex} 执行失败：{safeMessage}", now);
        attempt.MarkFailed(problem.Code, safeMessage, now);
        task.ReleaseRunLease(now, clearActiveAttempt: true);
        await SaveAsync(task, workspace, attempt, cancellationToken);
        return Result.Success(task);
    }

    private static bool RequiresRuntimeApproval(AgentStep step, ToolRegistration tool)
    {
        if (step.RequiresApproval)
        {
            return true;
        }

        return tool.RequiresApproval || tool.RiskLevel == AICopilot.SharedKernel.Ai.AiToolRiskLevel.RequiresApproval;
    }

    private async Task SaveAsync(
        AgentTask task,
        ArtifactWorkspace workspace,
        AgentTaskRunAttempt? attempt,
        CancellationToken cancellationToken)
    {
        taskRepository.Update(task);
        workspaceRepository.Update(workspace);
        if (attempt is not null)
        {
            runAttemptStore.Update(attempt);
        }

        await taskRepository.SaveChangesAsync(cancellationToken);
    }

    private static AgentTaskPlanDocument DeserializePlan(string planJson)
    {
        return JsonSerializer.Deserialize<AgentTaskPlanDocument>(planJson, AgentRuntimeJson.Options)
               ?? throw new InvalidOperationException("Agent task plan JSON is invalid.");
    }

}
