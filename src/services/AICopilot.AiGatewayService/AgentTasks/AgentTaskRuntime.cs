using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Security.Cryptography;
using System.Globalization;
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
using Microsoft.Extensions.DependencyInjection;
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
    CloudReadOnlyTextToSqlFallbackRunner? cloudTextToSqlFallbackRunner = null,
    AgentReasoningNodeExecutor? reasoningNodeExecutor = null,
    IServiceScopeFactory? serviceScopeFactory = null)
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
        new AgentRuntimeArtifactBuilder(workspaceService, documentGenerator),
        businessDatabaseReadService,
        businessTextToSqlRuntime,
        cloudTextToSqlFallbackRunner,
        reasoningNodeExecutor);

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
        IReadOnlyDictionary<string, AgentNodeRun>? durableNodesById = null;
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
            durableNodesByStep = IndexNodesByStep(plan, materialized);
            durableNodesById = materialized.ToDictionary(
                node => node.NodeId,
                StringComparer.Ordinal);
            durableEvidence = await nodeRunStore.ListEvidenceByAttemptAsync(
                durableClaim.RunAttempt.Id,
                cancellationToken);
            foreach (var evidence in durableEvidence.OrderBy(item => item.CreatedAt))
            {
                if (!durableNodesById.TryGetValue(evidence.NodeId, out var evidenceNode))
                {
                    return Result.Failure(new ApiProblemDescriptor(
                        AppProblemCodes.AgentNodeRunStateConflict,
                        "Durable Evidence references a NodeRun outside the current plan attempt."));
                }

                var access = AgentEvidenceAccessChecker.ValidateDurable(
                    evidence,
                    task,
                    attempt.Id.Value,
                    evidenceNode,
                    DateTimeOffset.UtcNow);
                if (!access.IsSuccess)
                {
                    return Result.From(access);
                }

                if (evidence.StorageMode == AgentEvidenceStorageMode.InlineCanonicalJson &&
                    !string.IsNullOrWhiteSpace(evidence.InlinePayloadJson))
                {
                    try
                    {
                        durableOutputByNodeRun[evidence.NodeRunId] =
                            AgentTaskRunStateCheckpointCodec.MergeEvidencePayload(
                                state,
                                evidence.InlinePayloadJson);
                    }
                    catch (Exception exception) when (exception is JsonException or InvalidOperationException)
                    {
                        return Result.Failure(new ApiProblemDescriptor(
                            AppProblemCodes.AgentNodeRunStateConflict,
                            "Inline Evidence payload could not restore the durable task checkpoint."));
                    }

                    continue;
                }

                if (evidence.StorageMode != AgentEvidenceStorageMode.ArtifactReference ||
                    artifactReferenceEvidenceResolver is null)
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

        if (durableClaim is not null &&
            durableNodesByStep is not null &&
            durableNodesById is not null &&
            serviceScopeFactory is not null &&
            string.Equals(plan.TopologyProfile, "DagV1", StringComparison.Ordinal) &&
            plan.ConcurrencyPolicy is { MaxParallelism: >= AgentPlanContractVersions.DagMinParallelism })
        {
            var parallelRoots = await ExecuteParallelDagReadRootsAsync(
                task,
                workspace,
                attempt,
                plan,
                durableClaim,
                durableNodesByStep,
                durableEvidence,
                durableOutputByNodeRun,
                cancellationToken);
            if (!parallelRoots.IsSuccess)
            {
                return Result.From(parallelRoots);
            }

            durableEvidence = parallelRoots.Value!.Evidence;
            durableOutputByNodeRun = parallelRoots.Value.OutputByNodeRun;
            if (parallelRoots.Value.IsTerminal)
            {
                return Result.Success(task);
            }

            foreach (var evidence in durableEvidence
                         .Where(item =>
                             item.StorageMode == AgentEvidenceStorageMode.InlineCanonicalJson &&
                             !string.IsNullOrWhiteSpace(item.InlinePayloadJson))
                         .OrderBy(item => item.NodeId, StringComparer.Ordinal))
            {
                try
                {
                    _ = AgentTaskRunStateCheckpointCodec.MergeEvidencePayload(
                        state,
                        evidence.InlinePayloadJson!);
                }
                catch (Exception exception) when (exception is JsonException or InvalidOperationException)
                {
                    return Result.Failure(new ApiProblemDescriptor(
                        AppProblemCodes.AgentNodeRunStateConflict,
                        "Parallel NodeRun Evidence could not merge into the durable task checkpoint."));
                }
            }

            var refreshedNodes = await nodeRunStore!.ListByAttemptAsync(
                durableClaim.RunAttempt.Id,
                cancellationToken);
            durableNodesByStep = IndexNodesByStep(plan, refreshedNodes);
            durableNodesById = refreshedNodes.ToDictionary(
                node => node.NodeId,
                StringComparer.Ordinal);
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
                            var releaseError = await ReleaseApprovedNodeAsync(
                                durableNode,
                                durableClaim,
                                "Approved NodeRun could not become runnable under the current task fence.",
                                cancellationToken);
                            if (releaseError is not null)
                            {
                                return Result.Failure(releaseError);
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

                if (durableNodesById is null)
                {
                    return Result.Failure(new ApiProblemDescriptor(
                        AppProblemCodes.AgentNodeRunStateConflict,
                        "Approved final-output checkpoint is missing the materialized NodeRun roster."));
                }

                var parentSelection = SelectFinalizationParentEvidence(
                    task,
                    durableNodesById,
                    nodeContract,
                    durableEvidence,
                    attempt.Id.Value);
                if (!parentSelection.IsSuccess)
                {
                    return Result.From(parentSelection);
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
                    parentSelection.Value!,
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

            IReadOnlyCollection<AgentEvidenceRecord> selectedParentEvidence = [];
            if (durableClaim is not null && nodeContract is not null)
            {
                if (durableNodesById is null)
                {
                    return Result.Failure(new ApiProblemDescriptor(
                        AppProblemCodes.AgentNodeRunStateConflict,
                        "Durable node execution is missing the materialized NodeRun roster."));
                }

                durableEvidence = await nodeRunStore!.ListEvidenceByAttemptAsync(
                    durableClaim.RunAttempt.Id,
                    cancellationToken);
                var refreshedProducerNodes = await nodeRunStore.ListByAttemptAsync(
                    durableClaim.RunAttempt.Id,
                    cancellationToken);
                durableNodesById = refreshedProducerNodes.ToDictionary(
                    node => node.NodeId,
                    StringComparer.Ordinal);
                var parentSelection = SelectParentEvidence(
                    nodeContract,
                    durableEvidence,
                    task,
                    attempt.Id.Value,
                    durableNodesById);
                if (!parentSelection.IsSuccess)
                {
                    return Result.From(parentSelection);
                }

                selectedParentEvidence = parentSelection.Value!;
            }

            AgentToolExecutionAuditScope? executionScope = null;
            var toolDispatchStarted = false;
            var executionAttempts = 0;
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
                    cancellationToken,
                    selectedParentEvidence,
                    activeNodeClaim?.RunAttemptId,
                    activeNodeClaim?.NodeRun.Id);
                using var writeAuthorityScope = activeNodeClaim is null || writeAuthorityAccessor is null
                    ? null
                    : writeAuthorityAccessor.Push(new AgentRuntimeWriteAuthority(
                        activeNodeClaim.NodeRun.Id,
                        activeNodeClaim.TaskFencingToken,
                        activeNodeClaim.NodeFencingToken));
                toolDispatchStarted = true;
                if (nodeContract is null)
                {
                    throw new AgentToolExecutionException(
                        AppProblemCodes.AgentNodeRunStateConflict,
                        "AgentTask execution is missing its frozen Node contract.");
                }

                var executionResult = await AgentNodeExecutionPlane.ExecuteAsync(
                    AgentNodeExecutionContract.ForDurable(nodeContract),
                    executionToken => activeNodeClaim is null
                        ? AgentToolExecutionTimeout.ExecuteAsync(
                            executor,
                            executionContext with { CancellationToken = executionToken })
                        : ExecuteWithLeaseRenewalAsync(
                            executor,
                            executionContext with { CancellationToken = executionToken },
                            activeNodeClaim,
                            executionToken),
                    cancellationToken,
                    attemptCount => executionAttempts = attemptCount);
                AgentToolRuntimeOutputGate.EnsureValid(toolRegistration, executionResult);

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

                    var nodeCompletedAtUtc = DateTimeOffset.UtcNow;
                    var nodeDuration = nodeCompletedAtUtc - nodeExecutionStartedAt;
                    var normalized = AgentEvidenceNormalizer.Normalize(
                        durableClaim,
                        activeNodeClaim,
                        nodeContract,
                        toolRegistration,
                        step,
                        executionResult,
                        state,
                        workspace,
                        selectedParentEvidence,
                        executionAttempts,
                        nodeDuration,
                        nodeCompletedAtUtc);
                    if (!normalized.IsSuccess)
                    {
                        var problem = normalized.Errors!
                            .OfType<ApiProblemDescriptor>()
                            .FirstOrDefault();
                        AgentRuntimeTelemetry.RecordEvidenceNormalizationFailure(
                            problem?.Code ?? AppProblemCodes.AgentPlanInvalid);
                        if (string.Equals(
                                problem?.Code,
                                AppProblemCodes.AgentRunBudgetExceeded,
                                StringComparison.Ordinal))
                        {
                            AgentRuntimeTelemetry.RecordBudgetReject("evidence-normalization");
                        }

                        throw new AgentToolExecutionException(
                            problem?.Code ?? AppProblemCodes.AgentPlanInvalid,
                            problem?.Detail ?? "Node output could not be normalized as Evidence v1.");
                    }

                    AgentRuntimeTelemetry.RecordNodeDuration(nodeDuration, nodeContract.NodeKind);
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
                    durableEvidence = durableEvidence.Append(checkpoint.Evidence).ToArray();
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
                if (string.Equals(errorCode, AppProblemCodes.AgentRunBudgetExceeded, StringComparison.Ordinal))
                {
                    AgentRuntimeTelemetry.RecordBudgetReject("node-execution");
                }
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

                        await RecordOutcomeUnknownStepFailureAsync(
                            task,
                            workspace,
                            executionScope,
                            step,
                            attempt,
                            toolRegistration,
                            errorCode,
                            safeMessage,
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
                                toolDispatchStarted ? Math.Max(1, executionAttempts) : 0,
                                nodeExecutionStartedAt,
                                DateTimeOffset.UtcNow,
                                ex is AgentToolExecutionException toolException
                                    ? toolException.ModelCalls > 0
                                        ? toolException.ModelCalls
                                        : nodeContract?.NodeKind == "AgentReasoningNode" && toolDispatchStarted
                                            ? nodeContract.Budget.MaxModelCalls
                                            : 0
                                    : 0),
                            DateTimeOffset.UtcNow,
                            RetryAtUtc: null),
                        cancellationToken);
                    if (!failed.IsSuccess)
                    {
                        return Result.From(failed);
                    }
                }

                await RecordStepFailureAsync(
                    executionScope,
                    task,
                    workspace,
                    step,
                    toolRegistration,
                    attempt,
                    errorCode,
                    safeMessage,
                    cancellationToken);
                if (durableClaim is not null &&
                    activeNodeClaim is not null &&
                    !activeNodeClaim.NodeRun.IsRequired)
                {
                    await SaveAsync(task, workspace, attempt, cancellationToken);
                    continue;
                }

                if (nodeContract?.Required == true)
                {
                    AgentRuntimeTelemetry.RecordRequiredNodeFailure(nodeContract.NodeKind);
                }

                var stepFailedAt = DateTimeOffset.UtcNow;
                await FailAndSaveAsync(
                    task, workspace, attempt, $"步骤 {step.StepIndex} 执行失败：{safeMessage}",
                    errorCode, safeMessage, stepFailedAt, cancellationToken);
                return Result.Success(task);
            }
        }

        var failedAt = DateTimeOffset.UtcNow;
        const string message =
            "Agent plan did not pause at the canonical final-output checkpoint; no final approval was created.";
        await FailAndSaveAsync(
            task, workspace, attempt, message, AppProblemCodes.AgentFinalizationStateConflict,
            message, failedAt, cancellationToken);
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
            var releaseError = await ReleaseApprovedNodeAsync(
                durableNode,
                durableClaim,
                "Approved final-output NodeRun could not become runnable under the active task fence.",
                cancellationToken);
            if (releaseError is not null)
            {
                return Result.Failure(releaseError);
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
                    toolCallCount: 1,
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
        await FailAndSaveAsync(
            task, workspace, attempt, $"最终产物确认失败：{problem.Detail}",
            problem.Code, problem.Detail, failedAt, cancellationToken);
        return Result.Success(task);
    }

    private async Task<Result<AgentParallelDagRootState>> ExecuteParallelDagReadRootsAsync(
        AgentTask task,
        ArtifactWorkspace workspace,
        AgentTaskRunAttempt attempt,
        AgentTaskPlanDocument plan,
        DurableTaskClaim durableClaim,
        IReadOnlyDictionary<int, AgentNodeRun> durableNodesByStep,
        IReadOnlyCollection<AgentEvidenceRecord> durableEvidence,
        Dictionary<AgentNodeRunId, string> durableOutputByNodeRun,
        CancellationToken cancellationToken)
    {
        if (serviceScopeFactory is null ||
            nodeRunClaimCoordinator is null ||
            nodeCheckpointCoordinator is null ||
            plan.ConcurrencyPolicy is null ||
            plan.Nodes is null)
        {
            return Result.Failure(new ApiProblemDescriptor(
                AppProblemCodes.AgentNodeRunStateConflict,
                "Bounded DAG execution services are unavailable."));
        }

        var orderedSteps = task.Steps.OrderBy(step => step.StepIndex).ToArray();
        var candidates = plan.Nodes
            .Select((node, index) => new AgentParallelDagRootCandidate(
                index + 1,
                node,
                durableNodesByStep[index + 1],
                orderedSteps[index]))
            .Where(candidate =>
                candidate.RuntimeNode.Status == AgentNodeRunStatus.Runnable &&
                candidate.NodeContract.DependsOn.Count == 0 &&
                candidate.NodeContract.EvidenceSelectors.Count == 0 &&
                !candidate.NodeContract.ApprovalPolicy.Required &&
                candidate.NodeContract.SideEffectClass == "ReadOnly" &&
                candidate.NodeContract.NodeKind is
                    "CloudReadNode" or "GovernedDataReadNode" or "KnowledgeRetrievalNode")
            .OrderBy(candidate => candidate.StepIndex)
            .ToArray();
        if (candidates.Length < AgentPlanContractVersions.DagMinParallelism)
        {
            return Result.Success(new AgentParallelDagRootState(
                durableEvidence,
                durableOutputByNodeRun,
                IsTerminal: false));
        }

        var evidence = durableEvidence.ToList();
        AgentParallelDagRequiredFailure? requiredFailure = null;
        foreach (var chunk in candidates.Chunk(plan.ConcurrencyPolicy.MaxParallelism))
        {
            var workItems = new List<AgentParallelDagRootWorkItem>(chunk.Length);
            foreach (var candidate in chunk)
            {
                var toolDecision = await toolRegistryGuard.ValidateAsync(
                    candidate.Step.ToolCode,
                    task.UserId,
                    cancellationToken);
                if (!toolDecision.IsAllowed)
                {
                    return Result.Failure(toolDecision.Problem!);
                }

                var tool = toolDecision.Tool!;
                if (plan.PluginSelectionMode != AgentPluginSelectionMode.BuiltInOnly ||
                    tool.TargetType != ToolRegistrationTargetType.AgentRuntime ||
                    tool.ProviderType is ToolProviderType.Mcp or ToolProviderType.MockMcp ||
                    RequiresRuntimeApproval(candidate.Step, tool))
                {
                    return Result.Failure(new ApiProblemDescriptor(
                        AppProblemCodes.AgentPlanToolDenied,
                        $"Tool '{tool.ToolCode}' is outside the parallel built-in read boundary."));
                }

                var claimed = await nodeRunClaimCoordinator.ClaimAsync(
                    candidate.RuntimeNode.Id,
                    durableClaim.RunAttempt.Id,
                    durableClaim.TaskFencingToken,
                    durableClaim.RunAttempt.LeaseOwner ?? "agent-dag-read-runtime",
                    (runQueueOptions?.Value ?? new AgentRunQueueOptions()).LeaseDuration,
                    DateTimeOffset.UtcNow,
                    cancellationToken);
                if (!claimed.IsSuccess || claimed.Value is null)
                {
                    return claimed.IsSuccess
                        ? Result.Failure(new ApiProblemDescriptor(
                            AppProblemCodes.AgentNodeRunStateConflict,
                            $"Parallel read NodeRun '{candidate.NodeContract.NodeId}' was not claimable."))
                        : Result.From(claimed);
                }

                var running = await nodeRunClaimCoordinator.MarkRunningAsync(
                    claimed.Value,
                    DateTimeOffset.UtcNow,
                    cancellationToken);
                if (!running.IsSuccess)
                {
                    return Result.From(running);
                }

                candidate.Step.Start(DateTimeOffset.UtcNow);
                await runtimeEventRecorder.StageStepStartedAsync(task, candidate.Step, cancellationToken);
                var auditScope = runtimeEventRecorder.BeginToolExecution(
                    task,
                    candidate.Step,
                    tool,
                    attempt,
                    DateTimeOffset.UtcNow);
                workItems.Add(new AgentParallelDagRootWorkItem(
                    candidate,
                    tool,
                    claimed.Value,
                    auditScope));
            }

            using var batchCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var executions = workItems
                .Select(item => ExecuteParallelReadInScopeAsync(
                    new AgentParallelReadNodeExecutionRequest(
                        task,
                        workspace,
                        plan,
                        item.Candidate.NodeContract,
                        item.Candidate.Step,
                        item.Tool),
                    batchCts.Token))
                .ToArray();
            var outcomes = await AwaitParallelReadBatchWithLeaseRenewalAsync(
                executions,
                workItems.Select(item => item.NodeClaim).ToArray(),
                batchCts,
                cancellationToken);
            if (!outcomes.IsSuccess)
            {
                return Result.From(outcomes);
            }

            for (var index = 0; index < workItems.Count; index++)
            {
                var item = workItems[index];
                var outcome = outcomes.Value![index];
                var completedAtUtc = outcome.CompletedAtUtc;
                if (outcome.IsSuccess)
                {
                    var normalized = AgentEvidenceNormalizer.Normalize(
                        durableClaim,
                        item.NodeClaim,
                        item.Candidate.NodeContract,
                        item.Tool,
                        item.Candidate.Step,
                        outcome.ExecutionResult!,
                        outcome.State,
                        workspace,
                        parentEvidence: [],
                        toolCallCount: outcome.ToolCallCount,
                        elapsed: outcome.CompletedAtUtc - outcome.StartedAtUtc,
                        nowUtc: completedAtUtc);
                    if (!normalized.IsSuccess)
                    {
                        var problem = normalized.Errors!
                            .OfType<ApiProblemDescriptor>()
                            .FirstOrDefault();
                        AgentRuntimeTelemetry.RecordEvidenceNormalizationFailure(
                            problem?.Code ?? AppProblemCodes.AgentPlanInvalid);
                        return Result.From(normalized);
                    }

                    AgentRuntimeTelemetry.RecordNodeDuration(
                        outcome.CompletedAtUtc - outcome.StartedAtUtc,
                        item.Candidate.NodeContract.NodeKind);
                    var checkpoint = normalized.Value!;
                    var committed = await nodeCheckpointCoordinator.CommitSuccessAsync(
                        new AgentNodeSuccessCheckpoint(
                            durableClaim.Task.Id,
                            durableClaim.RunAttempt.Id,
                            item.NodeClaim.NodeRun.Id,
                            durableClaim.TaskFencingToken,
                            item.NodeClaim.NodeFencingToken,
                            checkpoint.Evidence,
                            checkpoint.Usage,
                            checkpoint.OutputDigest,
                            item.Tool.ToolCode,
                            ProviderReceiptHash: null,
                            completedAtUtc),
                        cancellationToken);
                    if (!committed.IsSuccess)
                    {
                        return Result.From(committed);
                    }

                    evidence.Add(checkpoint.Evidence);
                    durableOutputByNodeRun[item.NodeClaim.NodeRun.Id] =
                        outcome.ExecutionResult!.DurableOutput.CanonicalJson;
                    item.Candidate.Step.Complete(
                        outcome.ExecutionResult.DurableOutput.CanonicalJson,
                        completedAtUtc);
                    var artifactId = runtimeEventRecorder.MarkToolExecutionSucceeded(
                        item.AuditScope,
                        task,
                        workspace,
                        item.Candidate.Step,
                        item.Tool,
                        outcome.ExecutionResult.ContractOutput.ToJsonElement(),
                        completedAtUtc);
                    await runtimeEventRecorder.StageStepCompletedAsync(
                        task,
                        item.Candidate.Step,
                        cancellationToken);
                    await runtimeEventRecorder.RecordToolSucceededAsync(
                        task,
                        workspace,
                        item.Candidate.Step,
                        artifactId,
                        cancellationToken);
                    continue;
                }

                var failureCode = outcome.FailureCode ?? AppProblemCodes.AgentNodeRunStateConflict;
                var safeMessage = outcome.SafeMessage ?? "Parallel read node failed without an authoritative result.";
                var failed = await nodeCheckpointCoordinator.CommitFailureAsync(
                    new AgentNodeFailureCheckpoint(
                        durableClaim.Task.Id,
                        durableClaim.RunAttempt.Id,
                        item.NodeClaim.NodeRun.Id,
                        durableClaim.TaskFencingToken,
                        item.NodeClaim.NodeFencingToken,
                        failureCode,
                        safeMessage,
                        CreateFailureUsage(
                            durableClaim,
                            item.NodeClaim,
                            outcome.ToolCallCount,
                            outcome.StartedAtUtc,
                            completedAtUtc),
                        completedAtUtc,
                        RetryAtUtc: null),
                    cancellationToken);
                if (!failed.IsSuccess)
                {
                    return Result.From(failed);
                }

                item.Candidate.Step.Fail(safeMessage, completedAtUtc);
                await runtimeEventRecorder.RecordToolFailedAsync(
                    item.AuditScope,
                    task,
                    workspace,
                    item.Candidate.Step,
                    item.Tool,
                    attempt,
                    failureCode,
                    safeMessage,
                    completedAtUtc,
                    cancellationToken);
                if (item.Candidate.RuntimeNode.IsRequired && requiredFailure is null)
                {
                    AgentRuntimeTelemetry.RecordRequiredNodeFailure(
                        item.Candidate.NodeContract.NodeKind);
                    requiredFailure = new AgentParallelDagRequiredFailure(
                        item.Candidate.Step.StepIndex,
                        failureCode,
                        safeMessage,
                        completedAtUtc);
                }
            }
        }

        if (requiredFailure is not null)
        {
            await FailAndSaveAsync(
                task,
                workspace,
                attempt,
                $"步骤 {requiredFailure.StepIndex} 执行失败：{requiredFailure.SafeMessage}",
                requiredFailure.FailureCode,
                requiredFailure.SafeMessage,
                requiredFailure.FailedAtUtc,
                cancellationToken);
            return Result.Success(new AgentParallelDagRootState(
                evidence,
                durableOutputByNodeRun,
                IsTerminal: true));
        }

        await SaveAsync(task, workspace, attempt, cancellationToken);
        return Result.Success(new AgentParallelDagRootState(
            evidence,
            durableOutputByNodeRun,
            IsTerminal: false));
    }

    private async Task<Result<AgentParallelReadNodeExecutionOutcome[]>> AwaitParallelReadBatchWithLeaseRenewalAsync(
        Task<AgentParallelReadNodeExecutionOutcome>[] executions,
        IReadOnlyCollection<AgentNodeRunClaim> nodeClaims,
        CancellationTokenSource batchCts,
        CancellationToken cancellationToken)
    {
        var batch = Task.WhenAll(executions);
        var leaseDuration = (runQueueOptions?.Value ?? new AgentRunQueueOptions()).LeaseDuration;
        var renewalInterval = TimeSpan.FromMilliseconds(Math.Clamp(
            leaseDuration.TotalMilliseconds / 3d,
            100d,
            30_000d));
        while (!batch.IsCompleted)
        {
            var delay = Task.Delay(renewalInterval, cancellationToken);
            if (await Task.WhenAny(batch, delay) == batch)
            {
                break;
            }

            foreach (var nodeClaim in nodeClaims)
            {
                var renewed = await nodeRunClaimCoordinator!.RenewTaskAndNodeLeaseAsync(
                    nodeClaim,
                    leaseDuration,
                    leaseDuration,
                    DateTimeOffset.UtcNow,
                    cancellationToken);
                if (!renewed.IsSuccess)
                {
                    batchCts.Cancel();
                    try
                    {
                        await batch;
                    }
                    catch (OperationCanceledException)
                    {
                        // The authoritative failure is the stale fence reported below.
                    }

                    return Result.From(renewed);
                }
            }
        }

        return Result.Success(await batch);
    }

    private async Task<AgentParallelReadNodeExecutionOutcome> ExecuteParallelReadInScopeAsync(
        AgentParallelReadNodeExecutionRequest request,
        CancellationToken cancellationToken)
    {
        await using var scope = serviceScopeFactory!.CreateAsyncScope();
        var executor = scope.ServiceProvider.GetRequiredService<AgentParallelReadNodeExecutor>();
        return await executor.ExecuteAsync(request, cancellationToken);
    }

    private sealed record AgentParallelDagRootCandidate(
        int StepIndex,
        AgentPlanNodeDocument NodeContract,
        AgentNodeRun RuntimeNode,
        AgentStep Step);

    private sealed record AgentParallelDagRootWorkItem(
        AgentParallelDagRootCandidate Candidate,
        ToolRegistration Tool,
        AgentNodeRunClaim NodeClaim,
        AgentToolExecutionAuditScope AuditScope);

    private sealed record AgentParallelDagRequiredFailure(
        int StepIndex,
        string FailureCode,
        string SafeMessage,
        DateTimeOffset FailedAtUtc);

    private sealed record AgentParallelDagRootState(
        IReadOnlyCollection<AgentEvidenceRecord> Evidence,
        Dictionary<AgentNodeRunId, string> OutputByNodeRun,
        bool IsTerminal);

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

    private static AgentRunUsageLedgerEntry CreateFailureUsage(
        DurableTaskClaim taskClaim,
        AgentNodeRunClaim nodeClaim,
        int toolCallCount,
        DateTimeOffset startedAtUtc,
        DateTimeOffset nowUtc,
        int modelCallCount = 0)
    {
        if (toolCallCount is < 0 or > 5 ||
            toolCallCount > nodeClaim.NodeRun.MaxAttempts ||
            modelCallCount < 0 ||
            modelCallCount > nodeClaim.NodeRun.MaxModelCalls)
        {
            throw new InvalidOperationException("Failure usage is outside the NodeRun retry budget.");
        }

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
            modelCalls: modelCallCount,
            toolCalls: toolCallCount,
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
                toolCallCount,
                modelCallCount
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
        var execution = AgentToolExecutionTimeout.ExecuteAsync(
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

    private async Task<ApiProblemDescriptor?> ReleaseApprovedNodeAsync(
        AgentNodeRun node,
        DurableTaskClaim taskClaim,
        string conflictDetail,
        CancellationToken cancellationToken)
    {
        var released = await nodeRunMaterializer!.ReleaseApprovedNodeAsync(
            node.Id,
            taskClaim,
            DateTimeOffset.UtcNow,
            cancellationToken);
        return released == AgentFencedWriteResult.Succeeded
            ? null
            : new ApiProblemDescriptor(
                released == AgentFencedWriteResult.StaleFence
                    ? AppProblemCodes.AgentNodeRunFenceStale
                    : AppProblemCodes.AgentNodeRunStateConflict,
                conflictDetail);
    }

    private static Result<IReadOnlyCollection<AgentEvidenceRecord>> SelectParentEvidence(
        AgentPlanNodeDocument nodeContract,
        IReadOnlyCollection<AgentEvidenceRecord> availableEvidence,
        AgentTask task,
        Guid runAttemptId,
        IReadOnlyDictionary<string, AgentNodeRun> producerNodes)
    {
        return AgentEvidenceSelector.SelectForNode(
            nodeContract,
            availableEvidence,
            task,
            runAttemptId,
            producerNodes,
            DateTimeOffset.UtcNow);
    }

    private static Result<IReadOnlyCollection<AgentEvidenceRecord>> SelectFinalizationParentEvidence(
        AgentTask task,
        IReadOnlyDictionary<string, AgentNodeRun> producerNodes,
        AgentPlanNodeDocument nodeContract,
        IReadOnlyCollection<AgentEvidenceRecord> availableEvidence,
        Guid runAttemptId)
    {
        return SelectParentEvidence(
            nodeContract,
            availableEvidence,
            task,
            runAttemptId,
            producerNodes);
    }

    private async Task RecordStepFailureAsync(
        AgentToolExecutionAuditScope? executionScope,
        AgentTask task,
        ArtifactWorkspace workspace,
        AgentStep step,
        ToolRegistration tool,
        AgentTaskRunAttempt attempt,
        string errorCode,
        string safeMessage,
        CancellationToken cancellationToken)
    {
        var failedAtUtc = DateTimeOffset.UtcNow;
        step.Fail(safeMessage, failedAtUtc);
        await runtimeEventRecorder.RecordToolFailedAsync(
            executionScope,
            task,
            workspace,
            step,
            tool,
            attempt,
            errorCode,
            safeMessage,
            failedAtUtc,
            cancellationToken);
    }

    private Task RecordOutcomeUnknownStepFailureAsync(
        AgentTask task,
        ArtifactWorkspace workspace,
        AgentToolExecutionAuditScope? executionScope,
        AgentStep step,
        AgentTaskRunAttempt attempt,
        ToolRegistration tool,
        string errorCode,
        string safeMessage,
        CancellationToken cancellationToken)
    {
        return RecordStepFailureAsync(
            executionScope,
            task,
            workspace,
            step,
            tool,
            attempt,
            errorCode,
            safeMessage,
            cancellationToken);
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
        await FailAndSaveAsync(
            task, workspace, attempt, $"步骤 {step.StepIndex} 执行失败：{safeMessage}",
            problem.Code, safeMessage, now, cancellationToken);
        return Result.Success(task);
    }

    private static IReadOnlyDictionary<int, AgentNodeRun> IndexNodesByStep(
        AgentTaskPlanDocument plan,
        IReadOnlyCollection<AgentNodeRun> runtimeNodes)
    {
        return plan.Nodes!
            .Select((node, index) => new
            {
                StepIndex = index + 1,
                Node = runtimeNodes.Single(runtimeNode =>
                    string.Equals(runtimeNode.NodeId, node.NodeId, StringComparison.Ordinal))
            })
            .ToDictionary(item => item.StepIndex, item => item.Node);
    }

    private async Task FailAndSaveAsync(
        AgentTask task,
        ArtifactWorkspace workspace,
        AgentTaskRunAttempt attempt,
        string taskMessage,
        string failureCode,
        string safeMessage,
        DateTimeOffset failedAtUtc,
        CancellationToken cancellationToken)
    {
        task.Fail(taskMessage, failedAtUtc);
        attempt.MarkFailed(failureCode, safeMessage, failedAtUtc);
        task.ReleaseRunLease(failedAtUtc, clearActiveAttempt: true);
        await SaveAsync(task, workspace, attempt, cancellationToken);
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
