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
    private const string ToolExecutionFailedCode = "tool_execution_failed";
    private const string RunLeaseOwner = "agent-runtime-sync";
    private TimeSpan RunLeaseDuration => runQueueOptions?.Value.LeaseDuration ?? new AgentRunQueueOptions().LeaseDuration;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = true
    };

    public Task<Result<AgentTask>> RunAsync(AgentTask task, CancellationToken cancellationToken = default)
    {
        return RunAsync(task, AgentTaskRunTriggerType.Manual, cancellationToken);
    }

    public async Task<Result<AgentTask>> RunAsync(
        AgentTask task,
        AgentTaskRunTriggerType triggerType = AgentTaskRunTriggerType.Manual,
        CancellationToken cancellationToken = default)
    {
        var attemptResult = await BeginOrResumeAttemptAsync(task, triggerType, cancellationToken);
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

            await RefreshRunLeaseAsync(task, attempt, cancellationToken);
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
                    BuildInputSummary(step, toolRegistration),
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
                var artifactId = ExtractArtifactId(output);
                executionRecord.MarkSucceeded(
                    BuildOutputSummary(output),
                    artifactId,
                    BuildAuditMetadata(task, workspace, step, toolRegistration, output),
                    DateTimeOffset.UtcNow);
                step.Complete(JsonSerializer.Serialize(output, JsonOptions), DateTimeOffset.UtcNow);
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
                var safeMessage = SanitizeSummary(ex.Message, 2000) ?? "Tool execution failed.";
                if (executionRecord is null)
                {
                    executionRecord = new ToolExecutionRecord(
                        task.Id,
                        step.Id,
                        step.ToolCode ?? "unknown",
                        BuildInputSummary(step, null),
                        DateTimeOffset.UtcNow,
                        attempt.Id);
                    toolExecutionRepository.Add(executionRecord);
                }

                var errorCode = ResolveExecutionErrorCode(ex, step, toolRegistration);
                if (executionRecord.Status == ToolExecutionStatus.Running)
                {
                    executionRecord.MarkFailed(
                        errorCode,
                        safeMessage,
                        BuildAuditMetadata(task, workspace, step, toolRegistration),
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

    private async Task<Result<AgentTaskRunAttempt>> BeginOrResumeAttemptAsync(
        AgentTask task,
        AgentTaskRunTriggerType triggerType,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        if (task.IsRunInProgress(now))
        {
            return Result.Failure(new ApiProblemDescriptor(
                AppProblemCodes.AgentTaskRunInProgress,
                "Agent task already has an active run lease."));
        }

        if (task.Status is AgentTaskStatus.WorkspaceReady or AgentTaskStatus.WaitingFinalApproval)
        {
            return Result.Invalid("Agent task is waiting for final output approval and cannot be run again.");
        }

        if (task.ActiveRunAttemptId is not null)
        {
            var activeAttempt = await runAttemptRepository.FirstOrDefaultAsync(
                new AgentTaskRunAttemptByIdSpec(task.ActiveRunAttemptId.Value),
                cancellationToken);
            if (activeAttempt is not null && !activeAttempt.IsTerminal)
            {
                if (task.Status == AgentTaskStatus.WaitingToolApproval ||
                    activeAttempt.Status == AgentTaskRunAttemptStatus.WaitingApproval ||
                    task.Steps.Any(step => step.Status == AgentStepStatus.Approved))
                {
                    return await AcquireAttemptLeaseAsync(task, activeAttempt, cancellationToken);
                }

                var message = "Previous agent task run lease expired. Retry the task before continuing.";
                activeAttempt.MarkFailed(AppProblemCodes.AgentTaskRunLeaseExpired, message, now);
                task.Fail(message, now);
                task.ReleaseRunLease(now, clearActiveAttempt: true);
                runAttemptRepository.Update(activeAttempt);
                taskRepository.Update(task);
                await taskRepository.SaveChangesAsync(cancellationToken);
                return Result.Failure(new ApiProblemDescriptor(AppProblemCodes.AgentTaskRunLeaseExpired, message));
            }
        }

        if (task.Status is not AgentTaskStatus.PlanApproved and not AgentTaskStatus.WaitingToolApproval)
        {
            return Result.Invalid("Only approved or waiting-approval agent tasks can be executed.");
        }

        var attempt = new AgentTaskRunAttempt(
            task.Id,
            task.RunAttemptCount + 1,
            triggerType,
            RunLeaseOwner,
            now,
            RunLeaseDuration);
        runAttemptRepository.Add(attempt);
        task.BeginRunAttempt(
            attempt.Id,
            attempt.AttemptNo,
            attempt.LeaseId!.Value,
            attempt.LeaseOwner ?? RunLeaseOwner,
            attempt.LeaseExpiresAt!.Value,
            now);
        taskRepository.Update(task);
        await taskRepository.SaveChangesAsync(cancellationToken);
        return Result.Success(attempt);
    }

    private async Task<Result<AgentTaskRunAttempt>> AcquireAttemptLeaseAsync(
        AgentTask task,
        AgentTaskRunAttempt attempt,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        attempt.AcquireLease(Guid.NewGuid(), RunLeaseOwner, now, RunLeaseDuration);
        task.AcquireRunLease(
            attempt.LeaseId!.Value,
            attempt.LeaseOwner ?? RunLeaseOwner,
            attempt.LeaseExpiresAt!.Value,
            now);
        runAttemptRepository.Update(attempt);
        taskRepository.Update(task);
        await taskRepository.SaveChangesAsync(cancellationToken);
        return Result.Success(attempt);
    }

    private async Task RefreshRunLeaseAsync(
        AgentTask task,
        AgentTaskRunAttempt attempt,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        attempt.RefreshLease(now, RunLeaseDuration);
        task.AcquireRunLease(
            attempt.LeaseId!.Value,
            attempt.LeaseOwner ?? RunLeaseOwner,
            attempt.LeaseExpiresAt!.Value,
            now);
        runAttemptRepository.Update(attempt);
        taskRepository.Update(task);
        await taskRepository.SaveChangesAsync(cancellationToken);
    }

    private AgentToolExecutorResolver CreateExecutorResolver()
    {
        return new AgentToolExecutorResolver(
            toolExecutors.Append(new RuntimeBuiltInAgentToolExecutor(ExecuteBuiltInStepAsync)));
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

    private Task<object> ExecuteBuiltInStepAsync(AgentToolExecutionContext context)
    {
        return ExecuteBuiltInStepAsync(
            context.Task,
            context.Workspace,
            context.Plan,
            context.Step,
            context.State,
            context.CancellationToken);
    }

    private async Task<object> ExecuteBuiltInStepAsync(
        AgentTask task,
        ArtifactWorkspace workspace,
        AgentTaskPlanDocument plan,
        AgentStep step,
        AgentTaskRunState state,
        CancellationToken cancellationToken)
    {
        return step.ToolCode switch
        {
            "read_uploaded_file" => await ReadUploadedFilesAsync(task.UserId, workspace, step, plan, state, writeSourceArtifacts: true, cancellationToken: cancellationToken),
            "parse_csv_json" => await ParseTableFileAsync(task.UserId, workspace, step, plan, state, cancellationToken),
            "parse_table_file" => await ParseTableFileAsync(task.UserId, workspace, step, plan, state, cancellationToken),
            "rag_search" => await SearchRagAsync(task, plan, state, cancellationToken),
            "query_cloud_data_readonly" => await QueryCloudReadonlyAsync(plan, state, cancellationToken),
            "query_cloud_sandbox_readonly" => await QueryCloudReadonlySandboxAsync(plan, state, step, cancellationToken),
            "query_cloud_production_pilot_readonly" => await QueryCloudReadonlyProductionPilotAsync(plan, state, step, cancellationToken),
            "query_cloud_production_controlled_readonly" => await QueryCloudReadonlyProductionControlledPilotAsync(plan, state, step, cancellationToken),
            "query_business_database_readonly" => await QueryBusinessDatabaseReadonlyP1Async(plan, state, cancellationToken),
            "summarize_business_query_result" => SummarizeBusinessQueryResult(state),
            "generate_business_chart" => await GenerateChartDataAsync(workspace, step, state, cancellationToken),
            "generate_chart_data" => await GenerateChartDataAsync(workspace, step, state, cancellationToken),
            "generate_markdown_report" => await GenerateMarkdownReportAsync(task, workspace, step, state, cancellationToken),
            "generate_html_report" => await GenerateHtmlReportAsync(task, workspace, step, state, cancellationToken),
            "generate_pdf" => await GeneratePdfReportAsync(task, workspace, step, state, cancellationToken),
            "generate_pptx" => await GeneratePptxReportAsync(task, workspace, step, state, cancellationToken),
            "generate_xlsx" => await GenerateXlsxReportAsync(task, workspace, step, state, cancellationToken),
            _ => throw new InvalidOperationException($"Unsupported agent tool code: {step.ToolCode}")
        };
    }

    private async Task<object> ReadUploadedFilesAsync(
        Guid userId,
        ArtifactWorkspace? workspace,
        AgentStep? step,
        AgentTaskPlanDocument plan,
        AgentTaskRunState state,
        bool writeSourceArtifacts,
        CancellationToken cancellationToken)
    {
        var uploads = await LoadUploadsAsync(plan.UploadIds, userId, cancellationToken);
        foreach (var upload in uploads)
        {
            if (state.Uploads.Any(item => item.Id == upload.Id.Value))
            {
                continue;
            }

            string? preview = null;
            if (!string.IsNullOrWhiteSpace(upload.StoragePath))
            {
                await using var stream = await fileStorage.GetAsync(upload.StoragePath, cancellationToken);
                if (stream is not null)
                {
                    await using var buffer = new MemoryStream();
                    await stream.CopyToAsync(buffer, cancellationToken);
                    var bytes = buffer.ToArray();
                    preview = BuildTextPreview(upload.FileName, bytes);

                    if (writeSourceArtifacts && workspace is not null && step is not null)
                    {
                        await workspaceService.WriteDraftBinaryArtifactAsync(
                            workspace,
                            ResolveArtifactType(upload.FileName),
                            upload.FileName,
                            $"source/{SafeFileName(upload.FileName)}",
                            bytes,
                            upload.ContentType,
                            step.Id,
                            sourceMetadata: null,
                            cancellationToken);
                    }
                }
            }

            state.Uploads.Add(new AgentUploadSummary(
                upload.Id.Value,
                upload.FileName,
                upload.ContentType,
                upload.FileSize,
                upload.Sha256,
                upload.StoragePath,
                preview));
        }

        return new { status = "completed", files = state.Uploads };
    }

    private async Task<object> ParseTableFileAsync(
        Guid userId,
        ArtifactWorkspace workspace,
        AgentStep step,
        AgentTaskPlanDocument plan,
        AgentTaskRunState state,
        CancellationToken cancellationToken)
    {
        if (state.Uploads.Count == 0)
        {
            await ReadUploadedFilesAsync(userId, workspace, step, plan, state, writeSourceArtifacts: false, cancellationToken: cancellationToken);
        }

        foreach (var upload in state.Uploads)
        {
            if (string.IsNullOrWhiteSpace(upload.StoragePath))
            {
                continue;
            }

            await using var stream = await fileStorage.GetAsync(upload.StoragePath, cancellationToken);
            if (stream is null)
            {
                continue;
            }

            var table = await tableFileParser.ParseAsync(
                new AgentTableFileParseRequest(upload.FileName, upload.ContentType, stream),
                cancellationToken);
            if (table is null)
            {
                continue;
            }

            state.Tables.Add(table);
            state.ParsedData.Add(new AgentParsedData(upload.FileName, "table", BuildTablePreview(table)));
            var fileStem = SafeFileStem(upload.FileName);
            var json = JsonSerializer.Serialize(table, JsonOptions);
            await workspaceService.WriteDraftTextArtifactAsync(
                workspace,
                ArtifactType.Json,
                $"{fileStem}.normalized.json",
                $"data/{fileStem}.normalized.json",
                json,
                "application/json",
                step.Id,
                sourceMetadata: null,
                cancellationToken);
            await workspaceService.WriteDraftTextArtifactAsync(
                workspace,
                ArtifactType.Csv,
                $"{fileStem}.normalized.csv",
                $"data/{fileStem}.normalized.csv",
                BuildCsv(table),
                "text/csv",
                step.Id,
                sourceMetadata: null,
                cancellationToken);
        }

        return new
        {
            status = "completed",
            tables = state.Tables.Select(table => new
            {
                table.Name,
                table.Columns,
                rowCount = table.Rows.Count
            }),
            parsed = state.ParsedData
        };
    }

    private async Task<object> SearchRagAsync(
        AgentTask task,
        AgentTaskPlanDocument plan,
        AgentTaskRunState state,
        CancellationToken cancellationToken)
    {
        var isTaskOwnerAdmin = await IsTaskOwnerAdminAsync(task, cancellationToken);
        foreach (var knowledgeBaseId in plan.KnowledgeBaseIds)
        {
            var accessChecker = knowledgeBaseAccessCheckers.FirstOrDefault();
            if (accessChecker is null)
            {
                throw new InvalidOperationException("RAG knowledge base access checker is not configured.");
            }

            var canRead = await accessChecker.CanReadAsync(
                knowledgeBaseId,
                task.UserId,
                isTaskOwnerAdmin,
                cancellationToken);
            if (!canRead)
            {
                throw new UnauthorizedAccessException("RAG knowledge base is not visible to the current agent task.");
            }

            var results = await knowledgeRetrievalService.SearchAsync(
                knowledgeBaseId,
                task.Goal,
                topK: 3,
                minScore: 0.5,
                cancellationToken);
            state.RagResults.AddRange(results.Select(result => new AgentRagResult(
                knowledgeBaseId,
                result.DocumentId,
                result.DocumentName,
                result.ChunkIndex,
                result.Score,
                result.IsLowConfidence,
                result.LowConfidenceReason,
                result.Text)));
        }

        return new
        {
            status = "completed",
            lowConfidence = state.RagResults.Count == 0 || state.RagResults.All(item => item.Score < 0.65),
            sources = state.RagResults
        };
    }

    private async Task<bool> IsTaskOwnerAdminAsync(AgentTask task, CancellationToken cancellationToken)
    {
        var access = await identityAccessService.GetCurrentUserAccessAsync(task.UserId, cancellationToken);
        return string.Equals(access?.RoleName, "Admin", StringComparison.OrdinalIgnoreCase);
    }

    private async Task<object> QueryCloudReadonlyAsync(
        AgentTaskPlanDocument plan,
        AgentTaskRunState state,
        CancellationToken cancellationToken)
    {
        var intent = plan.CloudReadonlyIntent ?? throw new CloudAiReadException(
            AppProblemCodes.CloudReadonlyIntentUnsupported,
            "Cloud readonly intent is missing from the agent plan.");
        var result = await cloudReadonlyToolExecutor.ExecuteAsync(
            new CloudReadonlyAgentToolRequest(intent.Intent, intent.Query, intent.Confidence),
            cancellationToken);

        state.CloudReadonlySummary = result.Summary;
        state.CloudReadonlyRows = result.Rows;
        state.CloudReadonlySourceLabel = result.SourceLabel;
        state.CloudReadonlySourcePath = result.SourcePath;
        state.CloudReadonlySourceMode = result.SourceMode;
        state.CloudReadonlyIsSimulation = result.IsSimulation;
        state.CloudReadonlyRowCount = result.RowCount;
        state.CloudReadonlyIsTruncated = result.IsTruncated;
        return result;
    }

    private async Task<object> QueryCloudReadonlySandboxAsync(
        AgentTaskPlanDocument plan,
        AgentTaskRunState state,
        AgentStep step,
        CancellationToken cancellationToken)
    {
        if (plan.IsCloudSandboxControlledTrial || plan.CloudSandboxGoalIntent is not null)
        {
            return await QueryCloudReadonlySandboxControlledAsync(plan, state, step, cancellationToken);
        }

        if (cloudSandboxAgentTrialService is null)
        {
            throw new InvalidOperationException("CloudReadonlySandbox agent trial service is not configured.");
        }

        var scenarioId = ReadStepString(step.InputJson, "scenarioId") ?? plan.TrialScenarioId;
        if (string.IsNullOrWhiteSpace(scenarioId))
        {
            throw new InvalidOperationException("CloudReadonlySandbox agent trial scenario id is missing.");
        }

        var maxRows = ReadStepInt(step.InputJson, "maxRows") ?? 20;
        var timeoutMs = ReadStepInt(step.InputJson, "timeoutMs") ?? 5000;
        var result = await cloudSandboxAgentTrialService.RunScenarioAsync(
            scenarioId,
            plan.ArtifactTypes,
            maxRows,
            timeoutMs,
            cancellationToken);
        if (!result.IsSuccess || result.Value is null)
        {
            throw new InvalidOperationException($"CloudReadonlySandbox agent trial query failed: {BuildResultErrorSummary(result)}");
        }

        var queryResult = result.Value.QueryResult;
        var rows = queryResult.Rows
            .Select(row => row.ToDictionary(item => item.Key, item => item.Value, StringComparer.OrdinalIgnoreCase))
            .ToArray();

        state.CloudReadonlySummary =
            $"CloudReadonly sandbox query executed. sourceType={queryResult.SourceType}; sourceMode={queryResult.SourceMode}; isSandbox={queryResult.IsSandbox.ToString().ToLowerInvariant()}; isSimulation={queryResult.IsSimulation.ToString().ToLowerInvariant()}; sourceLabel={queryResult.SourceLabel}; boundary={queryResult.Boundary}; endpointCode={queryResult.EndpointCode}; queryHash={queryResult.QueryHash}; resultHash={queryResult.ResultHash}; rows={queryResult.RowCount}; truncated={queryResult.IsTruncated.ToString().ToLowerInvariant()}; approvalStatus={queryResult.ApprovalStatus}.";
        state.CloudReadonlyRows = rows;
        state.CloudReadonlySourceLabel = queryResult.SourceLabel;
        state.CloudReadonlySourcePath = queryResult.EndpointCode;
        state.CloudReadonlySourceMode = queryResult.SourceMode;
        state.CloudReadonlyIsSimulation = queryResult.IsSimulation;
        state.CloudReadonlyRowCount = queryResult.RowCount;
        state.CloudReadonlyIsTruncated = queryResult.IsTruncated;
        state.BusinessQueryHash = queryResult.QueryHash;
        state.CloudSandboxQueryResults.Add(new AgentCloudSandboxQuerySummary(
            queryResult.EndpointCode,
            queryResult.SourceMode,
            queryResult.IsSandbox,
            queryResult.SourceLabel,
            queryResult.QueryHash,
            queryResult.ResultHash,
            queryResult.RowCount,
            queryResult.IsTruncated,
            [],
            CloudReadonlySandboxControlledTrialMarkers.FixedScenarioTrialMode,
            null,
            queryResult.Boundary,
            queryResult.ApprovalStatus));

        return new
        {
            status = "completed",
            trialMode = CloudReadonlySandboxControlledTrialMarkers.FixedScenarioTrialMode,
            scenarioId = result.Value.ScenarioId,
            scenarioTitle = result.Value.ScenarioTitle,
            sourceType = queryResult.SourceType,
            sourceMode = queryResult.SourceMode,
            isSandbox = queryResult.IsSandbox,
            isSimulation = queryResult.IsSimulation,
            sourceLabel = queryResult.SourceLabel,
            boundary = queryResult.Boundary,
            endpointCode = queryResult.EndpointCode,
            queryHash = queryResult.QueryHash,
            resultHash = queryResult.ResultHash,
            rowCount = queryResult.RowCount,
            isTruncated = queryResult.IsTruncated,
            approvalStatus = queryResult.ApprovalStatus,
            rows
        };
    }

    private async Task<object> QueryCloudReadonlySandboxControlledAsync(
        AgentTaskPlanDocument plan,
        AgentTaskRunState state,
        AgentStep step,
        CancellationToken cancellationToken)
    {
        if (cloudSandboxControlledTrialService is null)
        {
            throw new InvalidOperationException("CloudReadonlySandbox controlled trial service is not configured.");
        }

        var intent = plan.CloudSandboxGoalIntent
                     ?? throw new InvalidOperationException("CloudReadonlySandbox controlled trial intent is missing.");
        var maxRows = ReadStepInt(step.InputJson, "maxRows") ?? intent.MaxRows;
        var timeoutMs = ReadStepInt(step.InputJson, "timeoutMs") ?? 5000;
        var result = await cloudSandboxControlledTrialService.RunIntentAsync(
            intent,
            plan.ArtifactTypes,
            maxRows,
            timeoutMs,
            cancellationToken);
        if (!result.IsSuccess || result.Value is null)
        {
            throw new InvalidOperationException($"CloudReadonlySandbox controlled trial query failed: {BuildResultErrorSummary(result)}");
        }

        var queryResult = result.Value.QueryResult;
        var rows = queryResult.Rows
            .Select(row => row.ToDictionary(item => item.Key, item => item.Value, StringComparer.OrdinalIgnoreCase))
            .ToArray();

        state.CloudReadonlySummary =
            $"CloudReadonly sandbox controlled query executed. trialMode={CloudReadonlySandboxControlledTrialMarkers.TrialMode}; intentId={intent.IntentId}; sourceType={queryResult.SourceType}; sourceMode={queryResult.SourceMode}; isSandbox={queryResult.IsSandbox.ToString().ToLowerInvariant()}; isSimulation={queryResult.IsSimulation.ToString().ToLowerInvariant()}; sourceLabel={queryResult.SourceLabel}; boundary={queryResult.Boundary}; endpointCode={queryResult.EndpointCode}; queryHash={queryResult.QueryHash}; resultHash={queryResult.ResultHash}; rows={queryResult.RowCount}; truncated={queryResult.IsTruncated.ToString().ToLowerInvariant()}; approvalStatus={queryResult.ApprovalStatus}.";
        state.CloudReadonlyRows = rows;
        state.CloudReadonlySourceLabel = queryResult.SourceLabel;
        state.CloudReadonlySourcePath = queryResult.EndpointCode;
        state.CloudReadonlySourceMode = queryResult.SourceMode;
        state.CloudReadonlyIsSimulation = queryResult.IsSimulation;
        state.CloudReadonlyRowCount = queryResult.RowCount;
        state.CloudReadonlyIsTruncated = queryResult.IsTruncated;
        state.BusinessQueryHash = queryResult.QueryHash;
        state.CloudSandboxQueryResults.Add(new AgentCloudSandboxQuerySummary(
            queryResult.EndpointCode,
            queryResult.SourceMode,
            queryResult.IsSandbox,
            queryResult.SourceLabel,
            queryResult.QueryHash,
            queryResult.ResultHash,
            queryResult.RowCount,
            queryResult.IsTruncated,
            [],
            CloudReadonlySandboxControlledTrialMarkers.TrialMode,
            intent.IntentId,
            queryResult.Boundary,
            queryResult.ApprovalStatus));

        return new
        {
            status = "completed",
            trialMode = CloudReadonlySandboxControlledTrialMarkers.TrialMode,
            intentId = intent.IntentId,
            analysisType = intent.AnalysisType,
            sourceType = queryResult.SourceType,
            sourceMode = queryResult.SourceMode,
            isSandbox = queryResult.IsSandbox,
            isSimulation = queryResult.IsSimulation,
            sourceLabel = queryResult.SourceLabel,
            boundary = queryResult.Boundary,
            endpointCode = queryResult.EndpointCode,
            queryHash = queryResult.QueryHash,
            resultHash = queryResult.ResultHash,
            rowCount = queryResult.RowCount,
            isTruncated = queryResult.IsTruncated,
            approvalStatus = queryResult.ApprovalStatus,
            rows
        };
    }

    private async Task<object> QueryCloudReadonlyProductionPilotAsync(
        AgentTaskPlanDocument plan,
        AgentTaskRunState state,
        AgentStep step,
        CancellationToken cancellationToken)
    {
        if (cloudReadonlyProductionPilotService is null ||
            cloudReadonlyPilotReadinessService is null ||
            toolReadRepository is null)
        {
            throw new InvalidOperationException("CloudReadonlyProductionPilot service is not configured.");
        }

        if (!plan.IsCloudProductionPilotTrial)
        {
            throw new InvalidOperationException("CloudReadonlyProductionPilot tool is only allowed inside P12 production Pilot plans.");
        }

        var scenarioId = ReadStepString(step.InputJson, "scenarioId") ?? plan.TrialScenarioId;
        if (string.IsNullOrWhiteSpace(scenarioId))
        {
            throw new InvalidOperationException("CloudReadonlyProductionPilot scenario id is missing.");
        }

        var maxRows = ReadStepInt(step.InputJson, "maxRows") ?? 20;
        var timeoutMs = ReadStepInt(step.InputJson, "timeoutMs") ?? 5000;
        var windowId = ReadStepString(step.InputJson, "pilotWindowId");
        var protectedTools = await GetCloudReadonlyPilotReadinessQueryHandler.LoadProtectedToolRegistrationsAsync(
            toolReadRepository,
            cancellationToken);
        var result = await cloudReadonlyProductionPilotService.RunScenarioAsync(
            new RunCloudReadonlyProductionPilotScenarioCommand(
                scenarioId,
                plan.ArtifactTypes,
                windowId,
                TimeRange: null,
                maxRows,
                timeoutMs),
            cloudReadonlyPilotReadinessService.BuildStatus(protectedTools),
            protectedTools,
            cancellationToken);
        if (!result.IsSuccess || result.Value is null)
        {
            throw new InvalidOperationException($"CloudReadonlyProductionPilot query failed: {BuildResultErrorSummary(result)}");
        }

        var queryResult = result.Value.QueryResult;
        var rows = queryResult.Rows
            .Select(row => row.ToDictionary(item => item.Key, item => item.Value, StringComparer.OrdinalIgnoreCase))
            .ToArray();

        state.CloudReadonlySummary =
            $"CloudReadonly production Pilot query executed. sourceType={queryResult.SourceType}; sourceMode={queryResult.SourceMode}; isProductionData={queryResult.IsProductionData.ToString().ToLowerInvariant()}; isSandbox={queryResult.IsSandbox.ToString().ToLowerInvariant()}; isSimulation={queryResult.IsSimulation.ToString().ToLowerInvariant()}; sourceLabel={queryResult.SourceLabel}; boundary={queryResult.Boundary}; pilotWindowId={queryResult.PilotWindowId}; endpointCode={queryResult.EndpointCode}; queryHash={queryResult.QueryHash}; resultHash={queryResult.ResultHash}; rows={queryResult.RowCount}; truncated={queryResult.IsTruncated.ToString().ToLowerInvariant()}; approvalStatus={queryResult.ApprovalStatus}.";
        state.CloudReadonlyRows = rows;
        state.CloudReadonlySourceLabel = queryResult.SourceLabel;
        state.CloudReadonlySourcePath = queryResult.EndpointCode;
        state.CloudReadonlySourceMode = queryResult.SourceMode;
        state.CloudReadonlyIsSimulation = queryResult.IsSimulation;
        state.CloudReadonlyRowCount = queryResult.RowCount;
        state.CloudReadonlyIsTruncated = queryResult.IsTruncated;
        state.BusinessQueryHash = queryResult.QueryHash;
        state.CloudSandboxQueryResults.Add(new AgentCloudSandboxQuerySummary(
            queryResult.EndpointCode,
            queryResult.SourceMode,
            queryResult.IsSandbox,
            queryResult.SourceLabel,
            queryResult.QueryHash,
            queryResult.ResultHash,
            queryResult.RowCount,
            queryResult.IsTruncated,
            [],
            "ProductionPilotFixedScenario",
            null,
            queryResult.Boundary,
            queryResult.ApprovalStatus));

        return new
        {
            status = "completed",
            trialMode = "ProductionPilotFixedScenario",
            scenarioId = result.Value.ScenarioId,
            scenarioTitle = result.Value.ScenarioTitle,
            sourceType = queryResult.SourceType,
            sourceMode = queryResult.SourceMode,
            isProductionData = queryResult.IsProductionData,
            isSandbox = queryResult.IsSandbox,
            isSimulation = queryResult.IsSimulation,
            sourceLabel = queryResult.SourceLabel,
            boundary = queryResult.Boundary,
            pilotWindowId = queryResult.PilotWindowId,
            endpointCode = queryResult.EndpointCode,
            queryHash = queryResult.QueryHash,
            resultHash = queryResult.ResultHash,
            rowCount = queryResult.RowCount,
            isTruncated = queryResult.IsTruncated,
            approvalStatus = queryResult.ApprovalStatus,
            rows
        };
    }

    private async Task<object> QueryCloudReadonlyProductionControlledPilotAsync(
        AgentTaskPlanDocument plan,
        AgentTaskRunState state,
        AgentStep step,
        CancellationToken cancellationToken)
    {
        if (cloudReadonlyProductionControlledPilotService is null ||
            cloudReadonlyProductionPilotService is null ||
            cloudReadonlyPilotReadinessService is null ||
            toolReadRepository is null)
        {
            throw new InvalidOperationException("CloudReadonlyProductionControlledPilot service is not configured.");
        }

        if (!plan.IsCloudProductionControlledPilotTrial)
        {
            throw new InvalidOperationException("CloudReadonlyProductionControlledPilot tool is only allowed inside P13 controlled production Pilot plans.");
        }

        var intentId = ReadStepString(step.InputJson, "intentId") ?? plan.CloudProductionGoalIntent?.IntentId;
        if (string.IsNullOrWhiteSpace(intentId))
        {
            throw new InvalidOperationException("CloudProductionGoalIntent id is missing.");
        }

        var maxRows = ReadStepInt(step.InputJson, "maxRows") ?? plan.CloudProductionGoalIntent?.MaxRows ?? 20;
        var timeoutMs = ReadStepInt(step.InputJson, "timeoutMs") ?? 5000;
        var protectedTools = await GetCloudReadonlyPilotReadinessQueryHandler.LoadProtectedToolRegistrationsAsync(
            toolReadRepository,
            cancellationToken);
        var p12Status = cloudReadonlyProductionPilotService.BuildStatus(
            cloudReadonlyPilotReadinessService.BuildStatus(protectedTools),
            protectedTools);
        var result = await cloudReadonlyProductionControlledPilotService.RunIntentAsync(
            intentId,
            plan.ArtifactTypes,
            maxRows,
            timeoutMs,
            p12Status,
            protectedTools,
            cancellationToken);
        if (!result.IsSuccess || result.Value is null)
        {
            throw new InvalidOperationException($"CloudReadonlyProductionControlledPilot query failed: {BuildResultErrorSummary(result)}");
        }

        var queryResult = result.Value.QueryResult;
        var rows = queryResult.Rows
            .Select(row => row.ToDictionary(item => item.Key, item => item.Value, StringComparer.OrdinalIgnoreCase))
            .ToArray();

        state.CloudReadonlySummary =
            $"CloudReadonly production controlled Pilot query executed. sourceType={queryResult.SourceType}; sourceMode={queryResult.SourceMode}; isProductionData={queryResult.IsProductionData.ToString().ToLowerInvariant()}; isSandbox={queryResult.IsSandbox.ToString().ToLowerInvariant()}; isSimulation={queryResult.IsSimulation.ToString().ToLowerInvariant()}; sourceLabel={queryResult.SourceLabel}; boundary={queryResult.Boundary}; pilotWindowId={queryResult.PilotWindowId}; intentId={queryResult.IntentId}; endpointCode={queryResult.EndpointCode}; queryHash={queryResult.QueryHash}; resultHash={queryResult.ResultHash}; rows={queryResult.RowCount}; truncated={queryResult.IsTruncated.ToString().ToLowerInvariant()}; approvalStatus={queryResult.ApprovalStatus}.";
        state.CloudReadonlyRows = rows;
        state.CloudReadonlySourceLabel = queryResult.SourceLabel;
        state.CloudReadonlySourcePath = queryResult.EndpointCode;
        state.CloudReadonlySourceMode = queryResult.SourceMode;
        state.CloudReadonlyIsSimulation = queryResult.IsSimulation;
        state.CloudReadonlyRowCount = queryResult.RowCount;
        state.CloudReadonlyIsTruncated = queryResult.IsTruncated;
        state.BusinessQueryHash = queryResult.QueryHash;
        state.CloudSandboxQueryResults.Add(new AgentCloudSandboxQuerySummary(
            queryResult.EndpointCode,
            queryResult.SourceMode,
            queryResult.IsSandbox,
            queryResult.SourceLabel,
            queryResult.QueryHash,
            queryResult.ResultHash,
            queryResult.RowCount,
            queryResult.IsTruncated,
            [],
            CloudReadonlyProductionControlledPilotMarkers.TrialMode,
            queryResult.IntentId,
            queryResult.Boundary,
            queryResult.ApprovalStatus));

        return new
        {
            status = "completed",
            trialMode = CloudReadonlyProductionControlledPilotMarkers.TrialMode,
            intentId = result.Value.IntentId,
            analysisType = result.Value.AnalysisType,
            sourceType = queryResult.SourceType,
            sourceMode = queryResult.SourceMode,
            isProductionData = queryResult.IsProductionData,
            isSandbox = queryResult.IsSandbox,
            isSimulation = queryResult.IsSimulation,
            sourceLabel = queryResult.SourceLabel,
            boundary = queryResult.Boundary,
            pilotWindowId = queryResult.PilotWindowId,
            endpointCode = queryResult.EndpointCode,
            queryHash = queryResult.QueryHash,
            resultHash = queryResult.ResultHash,
            rowCount = queryResult.RowCount,
            isTruncated = queryResult.IsTruncated,
            approvalStatus = queryResult.ApprovalStatus,
            rows
        };
    }

    private async Task<object> QueryBusinessDatabaseReadonlyP1Async(
        AgentTaskPlanDocument plan,
        AgentTaskRunState state,
        CancellationToken cancellationToken)
    {
        if (businessDatabaseReadService is null || businessTextToSqlRuntime is null)
        {
            throw new InvalidOperationException("Business Text-to-SQL runtime is not configured.");
        }

        var enabledSources = await businessDatabaseReadService.ListSelectableAsync(
            DataSourceSelectionMode.Agent,
            cancellationToken);
        var selectedIds = (plan.DataSourceIds ?? [])
            .Where(id => id != Guid.Empty)
            .ToHashSet();
        var selectedDomains = (plan.BusinessDomains ?? [])
            .Where(domain => !string.IsNullOrWhiteSpace(domain))
            .Select(domain => domain.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var source = enabledSources
            .Where(item => item.IsSelectableInAgent)
            .Where(item => item.ExternalSystemType == DataSourceExternalSystemType.SimulationBusiness)
            .Where(item => selectedIds.Count == 0 || selectedIds.Contains(item.Id))
            .Where(item => selectedDomains.Count == 0 ||
                           selectedDomains.Contains(item.BusinessDomain) ||
                           selectedDomains.Contains(item.Category))
            .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();

        if (source is null)
        {
            throw new InvalidOperationException("No authorized SimulationBusiness data source is available for this agent task.");
        }

        var draftResult = await businessTextToSqlRuntime.GenerateDraftAsync(
            new BusinessTextToSqlDraftRequest(
                source.Id,
                plan.Goal,
                plan.BusinessDomains,
                source.DefaultQueryLimit,
                PreviewOnly: false),
            cancellationToken);
        if (!draftResult.IsSuccess || draftResult.Value is null)
        {
            throw new InvalidOperationException($"Business Text-to-SQL draft failed: {BuildResultErrorSummary(draftResult)}");
        }

        var draft = draftResult.Value;
        var queryResult = await businessTextToSqlRuntime.ExecuteAsync(
            new BusinessTextToSqlExecuteRequest(DraftId: draft.DraftId, RequestedLimit: source.DefaultQueryLimit),
            cancellationToken);
        if (!queryResult.IsSuccess || queryResult.Value is null)
        {
            throw new InvalidOperationException($"Business Text-to-SQL execution failed: {BuildResultErrorSummary(queryResult)}");
        }

        var result = queryResult.Value;
        var rows = result.Rows
            .Select(row => row.ToDictionary(item => item.Key, item => item.Value))
            .ToArray();

        state.CloudReadonlySummary =
            $"BusinessDatabase Text-to-SQL executed. sourceType=BusinessDatabase; sourceMode={result.SourceMode}; isSimulation={result.IsSimulation.ToString().ToLowerInvariant()}; sourceLabel={result.SourceLabel}; queryHash={result.QueryHash}; rows={result.RowCount}; truncated={result.IsTruncated.ToString().ToLowerInvariant()}.";
        state.CloudReadonlyRows = rows;
        state.CloudReadonlySourceLabel = result.SourceLabel;
        state.CloudReadonlySourcePath = "BusinessDataSourceCenter/TextToSql";
        state.CloudReadonlySourceMode = result.SourceMode.ToString();
        state.CloudReadonlyIsSimulation = result.IsSimulation;
        state.CloudReadonlyRowCount = result.RowCount;
        state.CloudReadonlyIsTruncated = result.IsTruncated;
        state.BusinessQueryHash = result.QueryHash;
        state.BusinessQueryResults.Add(new AgentBusinessQuerySummary(
            result.DataSourceId,
            result.DataSourceName,
            result.SourceMode.ToString(),
            result.IsSimulation,
            result.SourceLabel,
            result.QueryHash,
            result.RowCount,
            result.IsTruncated,
            ArtifactId: null));

        return new
        {
            status = "completed",
            sourceType = result.SourceType,
            sourceMode = result.SourceMode.ToString(),
            isSimulation = result.IsSimulation,
            sourceLabel = result.SourceLabel,
            queryHash = result.QueryHash,
            questionHash = draft.QuestionHash,
            sqlHash = draft.SqlHash,
            rowCount = result.RowCount,
            isTruncated = result.IsTruncated,
            columns = result.Columns,
            rows
        };
    }

    private static string BuildResultErrorSummary(IResult result)
    {
        return result.Errors is null
            ? result.Status.ToString()
            : string.Join("; ", result.Errors.Select(error => error?.ToString()).Where(error => !string.IsNullOrWhiteSpace(error)));
    }

    private static string? ReadStepString(string? inputJson, string propertyName)
    {
        if (string.IsNullOrWhiteSpace(inputJson))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(inputJson);
            return document.RootElement.ValueKind == JsonValueKind.Object &&
                   document.RootElement.TryGetProperty(propertyName, out var property) &&
                   property.ValueKind == JsonValueKind.String
                ? property.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static int? ReadStepInt(string? inputJson, string propertyName)
    {
        if (string.IsNullOrWhiteSpace(inputJson))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(inputJson);
            if (document.RootElement.ValueKind != JsonValueKind.Object ||
                !document.RootElement.TryGetProperty(propertyName, out var property))
            {
                return null;
            }

            return property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out var number)
                ? number
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private async Task<object> QueryBusinessDatabaseReadonlyAsync(
        AgentTaskPlanDocument plan,
        AgentTaskRunState state,
        CancellationToken cancellationToken)
    {
        if (businessDatabaseReadService is null)
        {
            throw new InvalidOperationException("Business database read service is not configured.");
        }

        var enabledSources = await businessDatabaseReadService.ListEnabledAsync(cancellationToken);
        var selectedIds = (plan.DataSourceIds ?? [])
            .Where(id => id != Guid.Empty)
            .ToHashSet();
        var selectedDomains = (plan.BusinessDomains ?? [])
            .Where(domain => !string.IsNullOrWhiteSpace(domain))
            .Select(domain => domain.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var candidates = enabledSources
            .Where(source => source.IsSelectableInAgent)
            .Where(source => selectedIds.Count == 0 || selectedIds.Contains(source.Id))
            .Where(source => selectedDomains.Count == 0 ||
                             selectedDomains.Contains(source.BusinessDomain) ||
                             selectedDomains.Contains(source.Category))
            .ToArray();

        if (candidates.Length == 0)
        {
            throw new InvalidOperationException("No authorized BusinessDatabase data source is available for this agent task.");
        }

        var rows = candidates.Select(source => new Dictionary<string, object?>
        {
            ["dataSourceId"] = source.Id,
            ["dataSourceName"] = source.Name,
            ["sourceType"] = "BusinessDatabase",
            ["sourceMode"] = source.ExternalSystemType.ToString(),
            ["isSimulation"] = source.ExternalSystemType == DataSourceExternalSystemType.SimulationBusiness,
            ["sourceLabel"] = source.ExternalSystemType == DataSourceExternalSystemType.SimulationBusiness
                ? "AI 独立模拟业务库"
                : source.Name,
            ["businessDomain"] = source.BusinessDomain,
            ["category"] = source.Category,
            ["sensitivityLevel"] = source.SensitivityLevel,
            ["defaultQueryLimit"] = source.DefaultQueryLimit,
            ["maxQueryLimit"] = source.MaxQueryLimit
        }).ToArray();

        var hasSimulation = candidates.Any(source => source.ExternalSystemType == DataSourceExternalSystemType.SimulationBusiness);
        var queryHash = ComputeHash($"{plan.Goal}|{string.Join(',', candidates.Select(source => source.Id))}|{plan.QueryMode ?? "TextToSql"}");

        state.CloudReadonlySummary =
            $"BusinessDatabase readonly query prepared. sourceType=BusinessDatabase; sourceMode={(hasSimulation ? "SimulationBusiness" : "NonCloud")}; isSimulation={hasSimulation.ToString().ToLowerInvariant()}; sourceLabel={(hasSimulation ? "AI 独立模拟业务库" : string.Join(", ", candidates.Select(source => source.Name)))}; queryHash={queryHash}.";
        state.CloudReadonlyRows = rows;
        state.CloudReadonlySourceLabel = hasSimulation ? "AI 独立模拟业务库" : string.Join(", ", candidates.Select(source => source.Name));
        state.CloudReadonlySourcePath = "BusinessDataSourceCenter";
        state.CloudReadonlySourceMode = hasSimulation ? "SimulationBusiness" : "NonCloud";
        state.CloudReadonlyIsSimulation = hasSimulation;
        state.CloudReadonlyRowCount = rows.Length;
        state.CloudReadonlyIsTruncated = false;
        state.BusinessQueryHash = queryHash;

        return new
        {
            status = "completed",
            sourceType = "BusinessDatabase",
            sourceMode = state.CloudReadonlySourceMode,
            isSimulation = state.CloudReadonlyIsSimulation,
            sourceLabel = state.CloudReadonlySourceLabel,
            queryHash,
            rowCount = rows.Length,
            rows
        };
    }

    private static object SummarizeBusinessQueryResult(AgentTaskRunState state)
    {
        return new
        {
            status = "completed",
            sourceType = "BusinessDatabase",
            sourceMode = state.CloudReadonlySourceMode,
            isSimulation = state.CloudReadonlyIsSimulation,
            sourceLabel = state.CloudReadonlySourceLabel,
            queryHash = state.BusinessQueryHash,
            rowCount = state.CloudReadonlyRowCount,
            summary = state.CloudReadonlySummary ?? "BusinessDatabase readonly query result is not available."
        };
    }

    private static string ComputeHash(string value)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    }

    private async Task<object> GenerateChartDataAsync(
        ArtifactWorkspace workspace,
        AgentStep step,
        AgentTaskRunState state,
        CancellationToken cancellationToken)
    {
        var payload = AgentReportComposer.BuildChartPayload(state);
        var content = JsonSerializer.Serialize(payload, JsonOptions);
        var sourceMetadata = BuildArtifactSourceMetadata(state);
        var artifact = await workspaceService.WriteDraftTextArtifactAsync(
            workspace,
            ArtifactType.Chart,
            "chart-data.json",
            "charts/chart-data.json",
            content,
            "application/json",
            step.Id,
            sourceMetadata,
            cancellationToken);
        return new { status = "completed", artifactId = artifact.Id.Value, artifact.RelativePath };
    }

    private async Task<object> GenerateMarkdownReportAsync(
        AgentTask task,
        ArtifactWorkspace workspace,
        AgentStep step,
        AgentTaskRunState state,
        CancellationToken cancellationToken)
    {
        var markdown = AgentReportComposer.BuildMarkdownReport(task, state);
        var sourceMetadata = BuildArtifactSourceMetadata(state);
        var artifact = await workspaceService.WriteDraftTextArtifactAsync(
            workspace,
            ArtifactType.Markdown,
            "report.md",
            "draft/report.md",
            markdown,
            "text/markdown",
            step.Id,
            sourceMetadata,
            cancellationToken);
        return new { status = "completed", artifactId = artifact.Id.Value, artifact.RelativePath };
    }

    private async Task<object> GenerateHtmlReportAsync(
        AgentTask task,
        ArtifactWorkspace workspace,
        AgentStep step,
        AgentTaskRunState state,
        CancellationToken cancellationToken)
    {
        var html = AgentReportComposer.BuildHtmlReport(task, state);
        var sourceMetadata = BuildArtifactSourceMetadata(state);
        var artifact = await workspaceService.WriteDraftTextArtifactAsync(
            workspace,
            ArtifactType.Html,
            "report.html",
            "draft/report.html",
            html,
            "text/html",
            step.Id,
            sourceMetadata,
            cancellationToken);
        return new { status = "completed", artifactId = artifact.Id.Value, artifact.RelativePath };
    }

    private async Task<object> GeneratePdfReportAsync(
        AgentTask task,
        ArtifactWorkspace workspace,
        AgentStep step,
        AgentTaskRunState state,
        CancellationToken cancellationToken)
    {
        var content = await documentGenerator.GeneratePdfAsync(AgentReportComposer.BuildReportDocument(task, state), cancellationToken);
        EnsureGeneratedContent(content, "PDF");
        var sourceMetadata = BuildArtifactSourceMetadata(state);
        var artifact = await workspaceService.WriteDraftBinaryArtifactAsync(
            workspace,
            ArtifactType.Pdf,
            "report.pdf",
            "draft/report.pdf",
            content,
            "application/pdf",
            step.Id,
            sourceMetadata,
            cancellationToken);
        return new { status = "completed", artifactId = artifact.Id.Value, artifact.RelativePath };
    }

    private async Task<object> GeneratePptxReportAsync(
        AgentTask task,
        ArtifactWorkspace workspace,
        AgentStep step,
        AgentTaskRunState state,
        CancellationToken cancellationToken)
    {
        var content = await documentGenerator.GeneratePptxAsync(AgentReportComposer.BuildReportDocument(task, state), cancellationToken);
        EnsureGeneratedContent(content, "PPTX");
        var sourceMetadata = BuildArtifactSourceMetadata(state);
        var artifact = await workspaceService.WriteDraftBinaryArtifactAsync(
            workspace,
            ArtifactType.Pptx,
            "report.pptx",
            "draft/report.pptx",
            content,
            "application/vnd.openxmlformats-officedocument.presentationml.presentation",
            step.Id,
            sourceMetadata,
            cancellationToken);
        return new { status = "completed", artifactId = artifact.Id.Value, artifact.RelativePath };
    }

    private async Task<object> GenerateXlsxReportAsync(
        AgentTask task,
        ArtifactWorkspace workspace,
        AgentStep step,
        AgentTaskRunState state,
        CancellationToken cancellationToken)
    {
        var content = await documentGenerator.GenerateXlsxAsync(AgentReportComposer.BuildReportDocument(task, state), cancellationToken);
        EnsureGeneratedContent(content, "XLSX");
        var sourceMetadata = BuildArtifactSourceMetadata(state);
        var artifact = await workspaceService.WriteDraftBinaryArtifactAsync(
            workspace,
            ArtifactType.Xlsx,
            "report.xlsx",
            "draft/report.xlsx",
            content,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            step.Id,
            sourceMetadata,
            cancellationToken);
        return new { status = "completed", artifactId = artifact.Id.Value, artifact.RelativePath };
    }

    private static ArtifactSourceMetadata? BuildArtifactSourceMetadata(AgentTaskRunState state)
    {
        var cloudSandbox = state.CloudSandboxQueryResults.LastOrDefault();
        if (cloudSandbox is not null)
        {
            return new ArtifactSourceMetadata(
                cloudSandbox.SourceMode,
                cloudSandbox.Boundary,
                IsSimulation: false,
                cloudSandbox.IsSandbox,
                cloudSandbox.SourceLabel,
                cloudSandbox.QueryHash,
                cloudSandbox.ResultHash,
                cloudSandbox.RowCount,
                cloudSandbox.IsTruncated);
        }

        var business = state.BusinessQueryResults.LastOrDefault();
        if (business is not null)
        {
            return new ArtifactSourceMetadata(
                business.SourceMode,
                Boundary: null,
                business.IsSimulation,
                IsSandbox: false,
                business.SourceLabel,
                business.QueryHash,
                ResultHash: null,
                business.RowCount,
                business.IsTruncated);
        }

        if (!string.IsNullOrWhiteSpace(state.CloudReadonlySourceMode) ||
            !string.IsNullOrWhiteSpace(state.CloudReadonlySourceLabel))
        {
            return new ArtifactSourceMetadata(
                state.CloudReadonlySourceMode,
                Boundary: null,
                state.CloudReadonlyIsSimulation,
                IsSandbox: string.Equals(state.CloudReadonlySourceMode, "CloudReadonlySandbox", StringComparison.OrdinalIgnoreCase),
                state.CloudReadonlySourceLabel,
                state.BusinessQueryHash,
                ResultHash: null,
                state.CloudReadonlyRowCount,
                state.CloudReadonlyIsTruncated);
        }

        return null;
    }

    private static void EnsureGeneratedContent(byte[] content, string artifactType)
    {
        if (content.Length == 0)
        {
            throw new InvalidOperationException($"{AppProblemCodes.ArtifactGenerationFailed}: {artifactType} generator returned an empty artifact.");
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
        var safeMessage = SanitizeSummary(problem.Detail, 2000) ?? "Tool execution rejected.";
        var executionRecord = new ToolExecutionRecord(
            task.Id,
            step.Id,
            step.ToolCode ?? "unknown",
            BuildInputSummary(step, null),
            now,
            attempt.Id);
        executionRecord.MarkRejected(
            problem.Code,
            safeMessage,
            BuildAuditMetadata(task, workspace, step, null),
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

    private static string ResolveExecutionErrorCode(Exception ex, AgentStep step, ToolRegistration? tool)
    {
        if (ex is AgentToolExecutionException toolExecutionException)
        {
            return toolExecutionException.Code;
        }

        if (ex is CloudAiReadException cloudAiReadException)
        {
            return cloudAiReadException.Code;
        }

        if (ex.Message.StartsWith(AppProblemCodes.ArtifactFinalized, StringComparison.OrdinalIgnoreCase))
        {
            return AppProblemCodes.ArtifactFinalized;
        }

        return step.StepType is AgentStepType.ArtifactGeneration or AgentStepType.ChartGeneration ||
               tool?.ProviderType == ToolProviderType.Artifact
            ? AppProblemCodes.ArtifactGenerationFailed
            : ToolExecutionFailedCode;
    }

    private static string BuildInputSummary(AgentStep step, ToolRegistration? tool)
    {
        var payload = new
        {
            stepIndex = step.StepIndex,
            stepType = step.StepType.ToString(),
            toolCode = step.ToolCode ?? tool?.ToolCode,
            targetName = tool?.TargetName,
            inputJsonLength = step.InputJson?.Length ?? 0
        };
        return SanitizeSummary(JsonSerializer.Serialize(payload, JsonOptions), 2000) ?? "{}";
    }

    private static string? BuildOutputSummary(object output)
    {
        return SanitizeSummary(JsonSerializer.Serialize(output, JsonOptions), 4000);
    }

    private static string BuildAuditMetadata(
        AgentTask task,
        ArtifactWorkspace workspace,
        AgentStep step,
        ToolRegistration? tool,
        object? output = null)
    {
        var cloudReadonly = output is CloudReadonlyAgentToolResult cloudResult
            ? new
            {
                cloudResult.SourceMode,
                cloudResult.IsSimulation,
                cloudResult.SourceLabel,
                cloudResult.SourcePath,
                cloudResult.Intent,
                cloudResult.Target,
                cloudResult.Kind,
                cloudResult.RowCount,
                cloudResult.IsTruncated
            }
            : null;
        var businessQuery = TryReadBusinessQueryAudit(output);
        var toolOutputAudit = TryReadToolOutputAudit(output);
        var payload = new
        {
            taskId = task.Id.Value,
            taskCode = task.TaskCode,
            workspaceCode = workspace.WorkspaceCode,
            stepIndex = step.StepIndex,
            toolCode = step.ToolCode ?? tool?.ToolCode,
            providerType = tool?.ProviderType.ToString(),
            providerKind = toolOutputAudit.ProviderKind ?? tool?.ProviderType.ToString(),
            isMock = toolOutputAudit.IsMock ?? (tool?.ProviderType == ToolProviderType.MockMcp),
            targetType = tool?.TargetType.ToString(),
            targetName = tool?.TargetName,
            timeoutSeconds = tool?.TimeoutSeconds,
            auditLevel = tool?.AuditLevel.ToString(),
            riskLevel = tool?.RiskLevel.ToString(),
            requiresApproval = tool?.RequiresApproval,
            approvalPolicy = tool?.ApprovalPolicy,
            approvalStatus = ResolveApprovalStatus(step, tool),
            dataBoundary = tool?.DataBoundary.ToString(),
            schemaVersion = tool?.SchemaVersion,
            toolCatalogVersion = tool?.CatalogVersion,
            toolRunId = toolOutputAudit.ToolRunId,
            resultHash = toolOutputAudit.ResultHash,
            cloudReadonly,
            businessQuery
        };
        return SanitizeSummary(JsonSerializer.Serialize(payload, JsonOptions), 4000) ?? "{}";
    }

    private static string ResolveApprovalStatus(AgentStep step, ToolRegistration? tool)
    {
        var requiresApproval = step.RequiresApproval ||
                               tool?.RequiresApproval == true ||
                               tool?.RiskLevel is AiToolRiskLevel.RequiresApproval
                                   or AiToolRiskLevel.High
                                   or AiToolRiskLevel.Critical;
        if (!requiresApproval)
        {
            return "NotRequired";
        }

        return step.Status is AgentStepStatus.Approved or AgentStepStatus.Running
            ? "Approved"
            : "Required";
    }

    private static ToolOutputAudit TryReadToolOutputAudit(object? output)
    {
        if (output is null)
        {
            return new ToolOutputAudit(null, null, null, null);
        }

        try
        {
            var serialized = JsonSerializer.Serialize(output, JsonOptions);
            using var document = JsonDocument.Parse(serialized);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return new ToolOutputAudit(null, null, null, ComputeSha256(serialized));
            }

            var providerKind = TryGetString(root, "providerKind") ?? TryGetString(root, "providerType");
            var isMock = TryGetBool(root, "isMock");
            var toolRunId = TryGetString(root, "toolRunId");
            var resultHash = TryGetString(root, "resultHash") ?? ComputeSha256(serialized);
            return new ToolOutputAudit(providerKind, isMock, toolRunId, resultHash);
        }
        catch (JsonException)
        {
            return new ToolOutputAudit(null, null, null, null);
        }
    }

    private static object? TryReadBusinessQueryAudit(object? output)
    {
        if (output is null)
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(JsonSerializer.Serialize(output, JsonOptions));
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("sourceType", out var sourceType) ||
                !string.Equals(sourceType.GetString(), "BusinessDatabase", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            return new
            {
                SourceType = sourceType.GetString(),
                SourceMode = TryGetString(root, "sourceMode"),
                IsSimulation = TryGetBool(root, "isSimulation"),
                SourceLabel = TryGetString(root, "sourceLabel"),
                QueryHash = TryGetString(root, "queryHash"),
                RowCount = TryGetInt(root, "rowCount"),
                IsTruncated = TryGetBool(root, "isTruncated")
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? TryGetString(JsonElement root, string propertyName)
    {
        return root.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private static bool? TryGetBool(JsonElement root, string propertyName)
    {
        return root.TryGetProperty(propertyName, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : null;
    }

    private static int? TryGetInt(JsonElement root, string propertyName)
    {
        return root.TryGetProperty(propertyName, out var value) && value.TryGetInt32(out var result)
            ? result
            : null;
    }

    private static string ComputeSha256(string value)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string? SanitizeSummary(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var sanitized = Regex.Replace(
            value,
            @"(?i)(api[_-]?key|token|password|secret|connection\s*string)\s*[""':=]+\s*[^,""}\s]+",
            "$1=******");
        sanitized = Regex.Replace(
            sanitized,
            @"[A-Za-z]:\\[^\s""']+",
            "[redacted-path]");
        sanitized = Regex.Replace(
            sanitized,
            @"(?i)(Host|Username|Password|Database|Port)\s*=\s*[^;""'}]+",
            "$1=******");

        return sanitized.Length <= maxLength ? sanitized : sanitized[..maxLength];
    }

    private static string? ExtractArtifactId(object output)
    {
        try
        {
            using var document = JsonDocument.Parse(JsonSerializer.Serialize(output, JsonOptions));
            return document.RootElement.TryGetProperty("artifactId", out var artifactId)
                ? artifactId.ToString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private async Task<List<UploadRecord>> LoadUploadsAsync(
        IReadOnlyCollection<Guid> uploadIds,
        Guid userId,
        CancellationToken cancellationToken)
    {
        if (uploadIds.Count == 0)
        {
            return [];
        }

        return await uploadRepository.ListAsync(
            new UploadRecordsByIdsForUserSpec(uploadIds.Select(id => new UploadRecordId(id)).ToArray(), userId),
            cancellationToken);
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
        return JsonSerializer.Deserialize<AgentTaskPlanDocument>(planJson, JsonOptions)
               ?? throw new InvalidOperationException("Agent task plan JSON is invalid.");
    }

    private static string BuildMarkdownReport(AgentTask task, AgentTaskRunState state)
    {
        var report = BuildReportDocument(task, state);
        var builder = new StringBuilder();
        builder.AppendLine($"# {report.Title}");
        builder.AppendLine();
        builder.AppendLine("## 任务目标");
        builder.AppendLine(report.Goal);
        builder.AppendLine();
        builder.AppendLine("## 输入文件");
        builder.AppendLine(report.UploadSummaries.Count == 0
            ? "- 未绑定上传文件。"
            : string.Join(Environment.NewLine, report.UploadSummaries.Select(item => $"- {item}")));
        builder.AppendLine();
        builder.AppendLine("## 表格数据");
        if (report.Tables.Count == 0)
        {
            builder.AppendLine("- 未解析到 CSV/JSON/XLSX 表格数据。");
        }
        else
        {
            foreach (var table in report.Tables)
            {
                builder.AppendLine($"### {table.Name}");
                builder.AppendLine(BuildMarkdownTable(table));
                builder.AppendLine();
            }
        }

        builder.AppendLine();
        builder.AppendLine("## 知识库来源");
        builder.AppendLine(report.Sources.Count == 0
            ? "- 未检索到可靠知识库来源。"
            : string.Join(Environment.NewLine, report.Sources.Select(item =>
                $"- {item.SourceType}: {item.Name}，{item.Detail}{(item.Score.HasValue ? $"，分数 {item.Score:F2}" : string.Empty)}{(item.IsLowConfidence ? "，低置信度" : string.Empty)}")));
        builder.AppendLine();
        builder.AppendLine("## Cloud 只读边界");
        builder.AppendLine(report.CloudReadonlySummary ?? "未访问 Cloud。");
        builder.AppendLine();
        builder.AppendLine("> 草稿产物，正式输出前需要用户确认。");
        return builder.ToString();
    }

    private static string BuildHtmlReport(AgentTask task, AgentTaskRunState state)
    {
        var report = BuildReportDocument(task, state);
        var builder = new StringBuilder();
        builder.AppendLine("<!doctype html>");
        builder.AppendLine("<html lang=\"zh-CN\">");
        builder.AppendLine("<head>");
        builder.AppendLine("  <meta charset=\"utf-8\">");
        builder.AppendLine($"  <title>{EscapeHtml(report.Title)}</title>");
        builder.AppendLine("  <style>body{font-family:Arial,'Microsoft YaHei',sans-serif;margin:32px;color:#1f2937}table{border-collapse:collapse;width:100%;margin:12px 0 24px}th,td{border:1px solid #d1d5db;padding:6px 8px;text-align:left}th{background:#f3f4f6}.muted{color:#6b7280}</style>");
        builder.AppendLine("</head>");
        builder.AppendLine("<body>");
        builder.AppendLine($"<h1>{EscapeHtml(report.Title)}</h1>");
        builder.AppendLine($"<p>{EscapeHtml(report.Goal)}</p>");
        builder.AppendLine("<h2>输入文件</h2>");
        builder.AppendLine("<ul>");
        foreach (var upload in report.UploadSummaries.DefaultIfEmpty("未绑定上传文件。"))
        {
            builder.AppendLine($"<li>{EscapeHtml(upload)}</li>");
        }

        builder.AppendLine("</ul>");
        builder.AppendLine("<h2>表格数据</h2>");
        foreach (var table in report.Tables)
        {
            builder.AppendLine($"<h3>{EscapeHtml(table.Name)}</h3>");
            builder.AppendLine(BuildHtmlTable(table));
        }

        if (report.Tables.Count == 0)
        {
            builder.AppendLine("<p class=\"muted\">未解析到 CSV/JSON/XLSX 表格数据。</p>");
        }

        builder.AppendLine("<h2>来源与边界</h2>");
        builder.AppendLine("<ul>");
        foreach (var source in report.Sources)
        {
            builder.AppendLine($"<li>{EscapeHtml(source.SourceType)}: {EscapeHtml(source.Name)} - {EscapeHtml(source.Detail)}</li>");
        }

        builder.AppendLine($"<li>{EscapeHtml(report.CloudReadonlySummary ?? "未访问 Cloud。")}</li>");
        builder.AppendLine("</ul>");
        builder.AppendLine("<p class=\"muted\">草稿产物，正式输出前需要用户确认。</p>");
        builder.AppendLine("</body>");
        builder.AppendLine("</html>");
        return builder.ToString();
    }

    private static AgentReportDocument BuildReportDocument(AgentTask task, AgentTaskRunState state)
    {
        return new AgentReportDocument(
            task.Title,
            task.Goal,
            state.Uploads
                .Select(item => $"{item.FileName} ({item.FileSize} bytes, sha256={SafeShaPrefix(item.Sha256)})")
                .ToArray(),
            state.Tables,
            state.RagResults
                .Select(item => new AgentReportSource(
                    "RAG",
                    item.DocumentName,
                    $"DocumentId={item.DocumentId}, Chunk={item.ChunkIndex}",
                    item.Score,
                    item.IsLowConfidence))
                .ToArray(),
            state.CloudReadonlySummary,
            DateTimeOffset.UtcNow);
    }

    private static object BuildChartPayload(AgentTaskRunState state)
    {
        var table = state.Tables.FirstOrDefault();
        if (table is not null && table.Rows.Count > 0 && table.Columns.Count > 0)
        {
            var labelColumn = table.Columns[0];
            var valueColumn = table.Columns.FirstOrDefault(column =>
                table.Rows.Any(row => row.TryGetValue(column, out var value) && TryParseNumber(value, out _)));
            if (valueColumn is not null)
            {
                return new
                {
                    type = "bar",
                    source = table.Name,
                    labels = table.Rows
                        .Take(20)
                        .Select(row => row.TryGetValue(labelColumn, out var value) ? value : string.Empty)
                        .ToArray(),
                    values = table.Rows
                        .Take(20)
                        .Select(row => row.TryGetValue(valueColumn, out var value) && TryParseNumber(value, out var number) ? number : 0)
                        .ToArray(),
                    generatedAt = DateTimeOffset.UtcNow
                };
            }
        }

        if (state.CloudReadonlyRows.Count > 0)
        {
            var columns = state.CloudReadonlyRows
                .SelectMany(row => row.Keys)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var valueColumn = columns.FirstOrDefault(column =>
                state.CloudReadonlyRows.Any(row =>
                    row.TryGetValue(column, out var value) && TryParseNumber(value, out _)));
            if (valueColumn is not null)
            {
                var labelColumn = columns.FirstOrDefault(column =>
                    state.CloudReadonlyRows.Any(row =>
                        row.TryGetValue(column, out var value) &&
                        value is not null &&
                        !TryParseNumber(value, out _))) ?? columns[0];
                return new
                {
                    type = "bar",
                    source = state.CloudReadonlySourceLabel ?? "cloud-readonly",
                    sourceMode = state.CloudReadonlySourceMode,
                    isSimulation = state.CloudReadonlyIsSimulation,
                    labels = state.CloudReadonlyRows
                        .Take(20)
                        .Select(row => row.TryGetValue(labelColumn, out var value) ? FormatChartLabel(value) : string.Empty)
                        .ToArray(),
                    values = state.CloudReadonlyRows
                        .Take(20)
                        .Select(row => row.TryGetValue(valueColumn, out var value) && TryParseNumber(value, out var number) ? number : 0)
                        .ToArray(),
                    truncated = state.CloudReadonlyIsTruncated,
                    rowCount = state.CloudReadonlyRowCount,
                    generatedAt = DateTimeOffset.UtcNow
                };
            }
        }

        return new
        {
            type = "bar",
            source = "parsed-preview",
            labels = state.ParsedData.Select(item => item.FileName).DefaultIfEmpty("输入").ToArray(),
            values = state.ParsedData.Select(item => item.Preview.Length).DefaultIfEmpty(0).ToArray(),
            generatedAt = DateTimeOffset.UtcNow
        };
    }

    private static string BuildTablePreview(AgentReportTable table)
    {
        return string.Join(Environment.NewLine, table.Rows.Take(5).Select(row =>
            "- " + string.Join("; ", table.Columns.Select(column =>
                $"{column}: {(row.TryGetValue(column, out var value) ? value : string.Empty)}"))));
    }

    private static string BuildMarkdownTable(AgentReportTable table)
    {
        if (table.Columns.Count == 0)
        {
            return "_无列数据_";
        }

        var builder = new StringBuilder();
        builder.AppendLine("| " + string.Join(" | ", table.Columns.Select(EscapeMarkdownCell)) + " |");
        builder.AppendLine("| " + string.Join(" | ", table.Columns.Select(_ => "---")) + " |");
        foreach (var row in table.Rows.Take(20))
        {
            builder.AppendLine("| " + string.Join(" | ", table.Columns.Select(column =>
                EscapeMarkdownCell(row.TryGetValue(column, out var value) ? value : string.Empty))) + " |");
        }

        return builder.ToString();
    }

    private static string BuildHtmlTable(AgentReportTable table)
    {
        var builder = new StringBuilder();
        builder.AppendLine("<table>");
        builder.AppendLine("<thead><tr>");
        foreach (var column in table.Columns)
        {
            builder.AppendLine($"<th>{EscapeHtml(column)}</th>");
        }

        builder.AppendLine("</tr></thead>");
        builder.AppendLine("<tbody>");
        foreach (var row in table.Rows.Take(50))
        {
            builder.AppendLine("<tr>");
            foreach (var column in table.Columns)
            {
                builder.AppendLine($"<td>{EscapeHtml(row.TryGetValue(column, out var value) ? value : string.Empty)}</td>");
            }

            builder.AppendLine("</tr>");
        }

        builder.AppendLine("</tbody></table>");
        return builder.ToString();
    }

    private static string BuildCsv(AgentReportTable table)
    {
        var builder = new StringBuilder();
        builder.AppendLine(string.Join(",", table.Columns.Select(EscapeCsv)));
        foreach (var row in table.Rows)
        {
            builder.AppendLine(string.Join(",", table.Columns.Select(column =>
                EscapeCsv(row.TryGetValue(column, out var value) ? value : string.Empty))));
        }

        return builder.ToString();
    }

    private static string? BuildTextPreview(string fileName, byte[] content)
    {
        var extension = Path.GetExtension(fileName);
        if (!new[] { ".txt", ".md", ".csv", ".json", ".html" }.Contains(extension, StringComparer.OrdinalIgnoreCase))
        {
            return null;
        }

        var length = Math.Min(content.Length, 4096);
        return Encoding.UTF8.GetString(content, 0, length);
    }

    private static ArtifactType ResolveArtifactType(string fileName)
    {
        return Path.GetExtension(fileName).ToLowerInvariant() switch
        {
            ".csv" => ArtifactType.Csv,
            ".json" => ArtifactType.Json,
            ".html" => ArtifactType.Html,
            ".pdf" => ArtifactType.Pdf,
            ".pptx" => ArtifactType.Pptx,
            ".xlsx" => ArtifactType.Xlsx,
            ".md" => ArtifactType.Markdown,
            _ => ArtifactType.Log
        };
    }

    private static bool TryParseNumber(string value, out double number)
    {
        return double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out number)
               || double.TryParse(value, out number);
    }

    private static bool TryParseNumber(object? value, out double number)
    {
        switch (value)
        {
            case null:
                number = 0;
                return false;
            case double doubleValue:
                number = doubleValue;
                return true;
            case float floatValue:
                number = floatValue;
                return true;
            case decimal decimalValue:
                number = (double)decimalValue;
                return true;
            case byte or sbyte or short or ushort or int or uint or long or ulong:
                number = Convert.ToDouble(value, CultureInfo.InvariantCulture);
                return true;
            default:
                return TryParseNumber(Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty, out number);
        }
    }

    private static string FormatChartLabel(object? value)
    {
        return value switch
        {
            null => string.Empty,
            DateTimeOffset date => date.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
            DateTime date => date.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty
        };
    }

    private static string SafeFileName(string fileName)
    {
        var name = Path.GetFileName(fileName);
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        var sanitized = new string(name.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray());
        return string.IsNullOrWhiteSpace(sanitized) ? "upload.bin" : sanitized;
    }

    private static string SafeFileStem(string fileName)
    {
        var stem = Path.GetFileNameWithoutExtension(SafeFileName(fileName));
        return string.IsNullOrWhiteSpace(stem) ? "data" : stem;
    }

    private static string SafeShaPrefix(string sha256)
    {
        return sha256.Length <= 12 ? sha256 : sha256[..12];
    }

    private static string EscapeCsv(string value)
    {
        if (!value.Contains(',') && !value.Contains('"') && !value.Contains('\n') && !value.Contains('\r'))
        {
            return value;
        }

        return "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }

    private static string EscapeMarkdownCell(string value)
    {
        return value.Replace("|", "\\|", StringComparison.Ordinal).ReplaceLineEndings(" ");
    }

    private static string EscapeHtml(string value)
    {
        return value
            .Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal)
            .Replace("\"", "&quot;", StringComparison.Ordinal);
    }

    private sealed class RuntimeBuiltInAgentToolExecutor(
        Func<AgentToolExecutionContext, Task<object>> execute)
        : IAgentToolExecutor
    {
        public bool CanExecute(ToolRegistration tool, AgentStep step)
        {
            return tool.ProviderType is ToolProviderType.BuiltIn or ToolProviderType.Artifact or ToolProviderType.CloudReadonly &&
                   tool.TargetType == ToolRegistrationTargetType.AgentRuntime &&
                   string.Equals(tool.TargetName, "AgentTaskRuntime", StringComparison.OrdinalIgnoreCase);
        }

        public async Task<AgentToolExecutionResult> ExecuteAsync(AgentToolExecutionContext context)
        {
            return AgentToolExecutionResult.From(await execute(context));
        }
    }

    private sealed record ToolOutputAudit(
        string? ProviderKind,
        bool? IsMock,
        string? ToolRunId,
        string? ResultHash);
}

internal sealed class AgentTaskRunState
{
    public List<AgentUploadSummary> Uploads { get; } = [];

    public List<AgentParsedData> ParsedData { get; } = [];

    public List<AgentReportTable> Tables { get; } = [];

    public List<AgentRagResult> RagResults { get; } = [];

    public string? CloudReadonlySummary { get; set; }

    public IReadOnlyList<Dictionary<string, object?>> CloudReadonlyRows { get; set; } = [];

    public string? CloudReadonlySourceLabel { get; set; }

    public string? CloudReadonlySourcePath { get; set; }

    public string? CloudReadonlySourceMode { get; set; }

    public bool CloudReadonlyIsSimulation { get; set; }

    public int CloudReadonlyRowCount { get; set; }

    public bool CloudReadonlyIsTruncated { get; set; }

    public string? BusinessQueryHash { get; set; }

    public List<AgentBusinessQuerySummary> BusinessQueryResults { get; } = [];

    public List<AgentCloudSandboxQuerySummary> CloudSandboxQueryResults { get; } = [];
}

internal sealed record AgentBusinessQuerySummary(
    Guid DataSourceId,
    string DataSourceName,
    string SourceMode,
    bool IsSimulation,
    string SourceLabel,
    string QueryHash,
    int RowCount,
    bool IsTruncated,
    Guid? ArtifactId);

internal sealed record AgentCloudSandboxQuerySummary(
    string EndpointCode,
    string SourceMode,
    bool IsSandbox,
    string SourceLabel,
    string QueryHash,
    string ResultHash,
    int RowCount,
    bool IsTruncated,
    IReadOnlyCollection<Guid> ArtifactRefs,
    string? TrialMode,
    string? IntentId,
    string? Boundary,
    string? ApprovalStatus);

internal sealed record AgentUploadSummary(
    Guid Id,
    string FileName,
    string ContentType,
    long FileSize,
    string Sha256,
    string? StoragePath,
    string? Preview);

internal sealed record AgentParsedData(string FileName, string Format, string Preview);

internal sealed record AgentRagResult(
    Guid KnowledgeBaseId,
    int DocumentId,
    string DocumentName,
    int ChunkIndex,
    double Score,
    bool IsLowConfidence,
    string? LowConfidenceReason,
    string Text);
