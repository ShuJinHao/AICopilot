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
                if (string.Equals(step.ToolCode, "finalize_artifacts", StringComparison.OrdinalIgnoreCase))
                {
                    var approval = await EnsureApprovalRequestAsync(
                        task,
                        AgentApprovalType.FinalOutput,
                        workspace.WorkspaceCode,
                        cancellationToken);
                    task.MarkWorkspaceReady(now);
                    task.WaitForFinalApproval(now);
                    attempt.WaitForApproval(now, "Waiting for final output approval.");
                    task.ReleaseRunLease(now, clearActiveAttempt: false);
                    await runtimeEventRecorder.StageApprovalRequestedAsync(task, approval, cancellationToken);

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
                        var approval = await EnsureApprovalRequestAsync(
                            task,
                            AgentApprovalType.ToolCall,
                            stepTargetId,
                            cancellationToken);
                        task.WaitForToolApproval(now);
                        attempt.WaitForApproval(now, "Waiting for tool approval.");
                        task.ReleaseRunLease(now, clearActiveAttempt: false);
                        await runtimeEventRecorder.StageApprovalRequestedAsync(task, approval, cancellationToken);

                        await SaveAsync(task, workspace, attempt, cancellationToken);
                        return Result.Success(task);
                    }
                }
            }

            if (step.Status is not AgentStepStatus.Pending and not AgentStepStatus.Approved)
            {
                continue;
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

        task.MarkWorkspaceReady(DateTimeOffset.UtcNow);
        task.WaitForFinalApproval(DateTimeOffset.UtcNow);
        attempt.WaitForApproval(DateTimeOffset.UtcNow, "Waiting for final output approval.");
        task.ReleaseRunLease(DateTimeOffset.UtcNow, clearActiveAttempt: false);
        var finalApproval = await EnsureApprovalRequestAsync(
            task,
            AgentApprovalType.FinalOutput,
            workspace.WorkspaceCode,
            cancellationToken);
        await runtimeEventRecorder.StageApprovalRequestedAsync(task, finalApproval, cancellationToken);

        await SaveAsync(task, workspace, attempt, cancellationToken);
        return Result.Success(task);
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

    private async Task<ApprovalRequest> EnsureApprovalRequestAsync(
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
            return existing;
        }

        var approval = new ApprovalRequest(
            task.Id,
            approvalType,
            targetId,
            task.UserId,
            DateTimeOffset.UtcNow);
        approvalRepository.Add(approval);
        return approval;
    }

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
