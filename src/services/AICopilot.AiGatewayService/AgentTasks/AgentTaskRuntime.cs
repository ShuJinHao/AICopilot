using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Security.Cryptography;
using System.Globalization;
using AICopilot.AiGatewayService.CloudReadiness;
using AICopilot.AiGatewayService.Tools;
using AICopilot.AiGatewayService.Workspaces;
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
    IRepository<AgentTaskRunAttempt> runAttemptRepository,
    IRepository<ArtifactWorkspace> workspaceRepository,
    IRepository<ApprovalRequest> approvalRepository,
    IRepository<ToolExecutionRecord> toolExecutionRepository,
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
    AgentAuditRecorder auditRecorder,
    IEnumerable<IAgentToolExecutor> toolExecutors,
    IOptions<AgentRunQueueOptions>? runQueueOptions = null,
    IBusinessDatabaseReadService? businessDatabaseReadService = null,
    IBusinessTextToSqlRuntime? businessTextToSqlRuntime = null,
    CloudReadonlySandboxAgentTrialService? cloudSandboxAgentTrialService = null,
    CloudReadonlySandboxControlledTrialService? cloudSandboxControlledTrialService = null,
    CloudReadonlyProductionPilotService? cloudReadonlyProductionPilotService = null,
    CloudReadonlyProductionControlledPilotService? cloudReadonlyProductionControlledPilotService = null,
    CloudReadonlyPilotReadinessService? cloudReadonlyPilotReadinessService = null,
    IReadRepository<ToolRegistration>? toolReadRepository = null)
    : IAgentTaskRuntime
{
    private readonly AgentTaskRunAttemptCoordinator runAttemptCoordinator = new(
        taskRepository,
        runAttemptRepository,
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
        cloudSandboxAgentTrialService,
        cloudSandboxControlledTrialService,
        cloudReadonlyProductionPilotService,
        cloudReadonlyProductionControlledPilotService,
        cloudReadonlyPilotReadinessService,
        toolReadRepository,
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

        var plan = DeserializePlan(task.PlanJson);
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
            var allowProductionPilotTool =
                plan.IsCloudProductionPilotTrial &&
                string.Equals(step.ToolCode, CloudReadonlyProductionPilotMarkers.ToolCode, StringComparison.OrdinalIgnoreCase);
            var allowProductionControlledPilotTool =
                plan.IsCloudProductionControlledPilotTrial &&
                string.Equals(step.ToolCode, CloudReadonlyProductionControlledPilotMarkers.ToolCode, StringComparison.OrdinalIgnoreCase);
            var toolDecision = await toolRegistryGuard.ValidateAsync(
                step.ToolCode,
                task.UserId,
                cancellationToken,
                allowProtectedProductionPilotTool: allowProductionPilotTool,
                allowProtectedProductionControlledPilotTool: allowProductionControlledPilotTool);
            if (!toolDecision.IsAllowed)
            {
                return await RejectStepAsync(task, workspace, step, attempt, toolDecision.Problem!, cancellationToken);
            }

            var toolRegistration = toolDecision.Tool!;
            if (RequiresRuntimeApproval(step, toolRegistration) && step.Status == AgentStepStatus.Pending)
            {
                step.WaitForApproval();
            }

            if (step.Status == AgentStepStatus.WaitingApproval)
            {
                if (string.Equals(step.ToolCode, "finalize_artifacts", StringComparison.OrdinalIgnoreCase))
                {
                    await EnsureApprovalRequestAsync(
                        task,
                        AgentApprovalType.FinalOutput,
                        workspace.WorkspaceCode,
                        cancellationToken);
                    task.MarkWorkspaceReady(now);
                    task.WaitForFinalApproval(now);
                    attempt.WaitForApproval(now, "Waiting for final output approval.");
                    task.ReleaseRunLease(now, clearActiveAttempt: false);

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
                        await EnsureApprovalRequestAsync(
                            task,
                            AgentApprovalType.ToolCall,
                            stepTargetId,
                            cancellationToken);
                        task.WaitForToolApproval(now);
                        attempt.WaitForApproval(now, "Waiting for tool approval.");
                        task.ReleaseRunLease(now, clearActiveAttempt: false);

                        await SaveAsync(task, workspace, attempt, cancellationToken);
                        return Result.Success(task);
                    }
                }
            }

            if (step.Status is not AgentStepStatus.Pending and not AgentStepStatus.Approved)
            {
                continue;
            }

            ToolExecutionRecord? executionRecord = null;
            try
            {
                executionRecord = new ToolExecutionRecord(
                    task.Id,
                    step.Id,
                    step.ToolCode ?? toolRegistration.ToolCode,
                    AgentToolExecutionAuditBuilder.BuildInputSummary(step, toolRegistration),
                    DateTimeOffset.UtcNow,
                    attempt.Id);
                toolExecutionRepository.Add(executionRecord);

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
                var output = (await ExecuteWithTimeoutAsync(executor, executionContext)).Output;
                var artifactId = AgentToolExecutionAuditBuilder.ExtractArtifactId(output);
                executionRecord.MarkSucceeded(
                    AgentToolExecutionAuditBuilder.BuildOutputSummary(output),
                    artifactId,
                    AgentToolExecutionAuditBuilder.BuildAuditMetadata(task, workspace, step, toolRegistration, output),
                    DateTimeOffset.UtcNow);
                step.Complete(JsonSerializer.Serialize(output, AgentRuntimeJson.Options), DateTimeOffset.UtcNow);
                await auditRecorder.RecordToolAsync(
                    task,
                    workspace,
                    step,
                    AuditResults.Succeeded,
                    $"Agent step {step.StepIndex} executed.",
                    artifactId,
                    cancellationToken);
            }
            catch (Exception ex)
            {
                var safeMessage = AgentToolExecutionAuditBuilder.SanitizeSummary(ex.Message, 2000) ?? "Tool execution failed.";
                if (executionRecord is null)
                {
                    executionRecord = new ToolExecutionRecord(
                        task.Id,
                        step.Id,
                        step.ToolCode ?? "unknown",
                        AgentToolExecutionAuditBuilder.BuildInputSummary(step, null),
                        DateTimeOffset.UtcNow,
                        attempt.Id);
                    toolExecutionRepository.Add(executionRecord);
                }

                var errorCode = AgentToolExecutionAuditBuilder.ResolveExecutionErrorCode(ex, step, toolRegistration);
                if (executionRecord.Status == ToolExecutionStatus.Running)
                {
                    executionRecord.MarkFailed(
                        errorCode,
                        safeMessage,
                        AgentToolExecutionAuditBuilder.BuildAuditMetadata(task, workspace, step, toolRegistration),
                        DateTimeOffset.UtcNow);
                }

                step.Fail(safeMessage, DateTimeOffset.UtcNow);
                await auditRecorder.RecordToolAsync(
                    task,
                    workspace,
                    step,
                    AuditResults.Rejected,
                    safeMessage,
                    null,
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

    private async Task EnsureApprovalRequestAsync(
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
            return;
        }

        approvalRepository.Add(new ApprovalRequest(
            task.Id,
            approvalType,
            targetId,
            task.UserId,
            DateTimeOffset.UtcNow));
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
        var executionRecord = new ToolExecutionRecord(
            task.Id,
            step.Id,
            step.ToolCode ?? "unknown",
            AgentToolExecutionAuditBuilder.BuildInputSummary(step, null),
            now,
            attempt.Id);
        executionRecord.MarkRejected(
            problem.Code,
            safeMessage,
            AgentToolExecutionAuditBuilder.BuildAuditMetadata(task, workspace, step, null),
            now);
        toolExecutionRepository.Add(executionRecord);

        step.Fail(safeMessage, now);
        await auditRecorder.RecordToolAsync(
            task,
            workspace,
            step,
            AuditResults.Rejected,
            safeMessage,
            null,
            cancellationToken);
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
            runAttemptRepository.Update(attempt);
        }

        await taskRepository.SaveChangesAsync(cancellationToken);
    }

    private static AgentTaskPlanDocument DeserializePlan(string planJson)
    {
        return JsonSerializer.Deserialize<AgentTaskPlanDocument>(planJson, AgentRuntimeJson.Options)
               ?? throw new InvalidOperationException("Agent task plan JSON is invalid.");
    }

}
