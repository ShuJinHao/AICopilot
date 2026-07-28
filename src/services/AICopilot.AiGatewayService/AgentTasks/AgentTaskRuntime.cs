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

        // Final output is a workspace approval checkpoint, not an executable
        // provider call. Once runtime has paused here, approval/finalization are
        // owned by their dedicated coordinators; retries must be idempotent and
        // must not create another approval or dispatch finalize_artifacts.
        if (task.Status == AgentTaskStatus.WaitingFinalApproval)
        {
            var pausedState = await ValidatePausedFinalizationCheckpointAsync(task, cancellationToken);
            return pausedState.IsSuccess
                ? Result.Success(task)
                : Result.From(pausedState);
        }

        var attemptResult = await runAttemptCoordinator.BeginOrResumeAttemptAsync(task, triggerType, cancellationToken);
        if (!attemptResult.IsSuccess)
        {
            return Result.From(attemptResult);
        }

        var attempt = attemptResult.Value!;
        var now = DateTimeOffset.UtcNow;
        if (task.Status is AgentTaskStatus.PlanApproved or AgentTaskStatus.WaitingToolApproval)
        {
            task.Start(now);
        }

        if (task.Status is not AgentTaskStatus.Running and not AgentTaskStatus.GeneratingArtifacts)
        {
            return Result.Invalid("Only approved or running agent tasks can be executed.");
        }

        var workspace = await LoadWorkspaceAsync(task, cancellationToken);
        var state = new AgentTaskRunState();
        var executorResolver = CreateExecutorResolver();

        foreach (var step in task.Steps.OrderBy(step => step.StepIndex))
        {
            if (step.Status == AgentStepStatus.Completed)
            {
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

            AgentToolExecutionAuditScope? executionScope = null;
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
                var executionResult = await ExecuteWithTimeoutAsync(executor, executionContext);
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
            catch (Exception ex)
            {
                var safeMessage = AgentToolExecutionAuditBuilder.BuildSafeExceptionSummary(ex);
                var errorCode = AgentToolExecutionAuditBuilder.ResolveExecutionErrorCode(ex, step, toolRegistration);
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

    private async Task<Result<AgentFinalizationCheckpointState>> ValidatePausedFinalizationCheckpointAsync(
        AgentTask task,
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
        return AgentFinalizationCheckpointStateValidator.ValidatePaused(
            task,
            workspace,
            approvals,
            attempts);
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
