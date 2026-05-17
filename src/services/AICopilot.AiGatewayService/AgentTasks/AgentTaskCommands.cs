using System.Text.Json;
using System.Text.Json.Serialization;
using AICopilot.AiGatewayService.Runtime;
using AICopilot.AiGatewayService.Tools;
using AICopilot.AiGatewayService.Workspaces;
using AICopilot.Core.AiGateway.Aggregates.AgentTasks;
using AICopilot.Core.AiGateway.Aggregates.Approvals;
using AICopilot.Core.AiGateway.Aggregates.Artifacts;
using AICopilot.Core.AiGateway.Aggregates.ConversationTemplate;
using AICopilot.Core.AiGateway.Aggregates.LanguageModel;
using AICopilot.Core.AiGateway.Aggregates.Sessions;
using AICopilot.Core.AiGateway.Aggregates.Uploads;
using AICopilot.Core.AiGateway.Ids;
using AICopilot.Core.AiGateway.Specifications.AgentTasks;
using AICopilot.Core.AiGateway.Specifications.Approvals;
using AICopilot.Core.AiGateway.Specifications.Artifacts;
using AICopilot.Core.AiGateway.Specifications.ConversationTemplate;
using AICopilot.Core.AiGateway.Specifications.LanguageModel;
using AICopilot.Core.AiGateway.Specifications.Sessions;
using AICopilot.Core.AiGateway.Specifications.Uploads;
using AICopilot.Services.CrossCutting.Attributes;
using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Ai;
using AICopilot.SharedKernel.Messaging;
using AICopilot.SharedKernel.Repository;
using AICopilot.SharedKernel.Result;

namespace AICopilot.AiGatewayService.AgentTasks;

[AuthorizeRequirement("AiGateway.PlanAgentTask")]
public sealed record PlanAgentTaskCommand(
    Guid SessionId,
    string Goal,
    AgentTaskType TaskType,
    Guid? ModelId,
    IReadOnlyCollection<Guid>? UploadIds = null,
    IReadOnlyCollection<Guid>? KnowledgeBaseIds = null) : ICommand<Result<AgentTaskDto>>;

[AuthorizeRequirement("AiGateway.ApproveAgentTaskPlan")]
public sealed record ApproveAgentTaskPlanCommand(Guid Id) : ICommand<Result<AgentTaskDto>>;

[AuthorizeRequirement("AiGateway.RunAgentTask")]
public sealed record RunAgentTaskCommand(Guid Id) : ICommand<Result<AgentTaskDto>>;

[AuthorizeRequirement("AiGateway.RunAgentTask")]
public sealed record RetryAgentTaskCommand(Guid Id) : ICommand<Result<AgentTaskDto>>;

[AuthorizeRequirement("AiGateway.CancelAgentTask")]
public sealed record CancelAgentTaskCommand(Guid Id) : ICommand<Result<AgentTaskDto>>;

public sealed class PlanAgentTaskCommandHandler(
    IRepository<AgentTask> repository,
    IRepository<ApprovalRequest> approvalRepository,
    IReadRepository<Session> sessionRepository,
    IReadRepository<UploadRecord> uploadRepository,
    IReadRepository<ConversationTemplate> templateRepository,
    IReadRepository<LanguageModel> modelRepository,
    IChatRuntimeSettingsProvider runtimeSettingsProvider,
    IAgentArtifactWorkspaceService workspaceService,
    AgentAuditRecorder auditRecorder,
    IEnumerable<IKnowledgeBaseAccessChecker> knowledgeBaseAccessCheckers,
    AgentPlanToolGuard planToolGuard,
    IAgentDynamicPlanner dynamicPlanner,
    ICloudReadonlyAgentPlanService cloudReadonlyPlanService,
    ICurrentUser currentUser)
    : ICommandHandler<PlanAgentTaskCommand, Result<AgentTaskDto>>
{
    private const int PlannerValidationVersion = 1;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public async Task<Result<AgentTaskDto>> Handle(PlanAgentTaskCommand request, CancellationToken cancellationToken)
    {
        if (currentUser.Id is not { } userId)
        {
            return MissingUser();
        }

        if (request.SessionId == Guid.Empty)
        {
            return Result.Invalid("SessionId is required.");
        }

        var session = await sessionRepository.FirstOrDefaultAsync(
            new SessionByIdForUserSpec(new SessionId(request.SessionId), userId),
            cancellationToken);
        if (session is null)
        {
            return Result.NotFound("Session not found.");
        }

        var uploadIds = (request.UploadIds ?? [])
            .Where(id => id != Guid.Empty)
            .Distinct()
            .ToArray();
        if (uploadIds.Length > 0)
        {
            var uploads = await uploadRepository.ListAsync(
                new UploadRecordsByIdsForUserSpec(uploadIds.Select(id => new UploadRecordId(id)).ToArray(), userId),
                cancellationToken);
            if (uploads.Count != uploadIds.Length)
            {
                return Result.Invalid("One or more upload records do not exist or are not visible to current user.");
            }
        }

        var knowledgeBaseIds = (request.KnowledgeBaseIds ?? [])
            .Where(id => id != Guid.Empty)
            .Distinct()
            .ToArray();
        if (knowledgeBaseIds.Length > 0)
        {
            var accessChecker = knowledgeBaseAccessCheckers.FirstOrDefault();
            if (accessChecker is null)
            {
                return Result.Failure("RAG knowledge base access checker is not configured.");
            }

            foreach (var knowledgeBaseId in knowledgeBaseIds)
            {
                var canRead = await accessChecker.CanReadAsync(
                    knowledgeBaseId,
                    userId,
                    IsAdmin(),
                    cancellationToken);
                if (!canRead)
                {
                    return Result.NotFound();
                }
            }
        }

        _ = await templateRepository.FirstOrDefaultAsync(
            new ConversationTemplateByCodeSpec("agent_planner"),
            cancellationToken);
        var runtimeSettings = await runtimeSettingsProvider.GetAsync(cancellationToken);
        var riskLevel = DetermineRiskLevel(request.TaskType);
        var plannerModelResult = await ResolvePlannerModelAsync(request.ModelId, cancellationToken);
        if (!plannerModelResult.IsSuccess)
        {
            return Result.From(plannerModelResult);
        }

        var plannerModel = plannerModelResult.Value;
        PlannerToolCatalog? plannerToolCatalog = null;
        IReadOnlyCollection<AgentStepPlanDto> steps;
        if (plannerModel is not null)
        {
            var catalogResult = await planToolGuard.GetAvailableToolCatalogAsync(userId, cancellationToken);
            if (!catalogResult.IsSuccess)
            {
                return Result.From(catalogResult);
            }

            plannerToolCatalog = catalogResult.Value!;
            if (plannerToolCatalog.AvailableToolCount == 0)
            {
                return Result.Failure(new ApiProblemDescriptor(
                    AppProblemCodes.PlannerToolCatalogEmpty,
                    "Planner model is available, but no enabled and authorized tools are available for the current user."));
            }

            var dynamicPlanResult = await dynamicPlanner.CreatePlanAsync(
                new AgentDynamicPlannerRequest(
                    request.Goal,
                    request.TaskType,
                    uploadIds,
                    knowledgeBaseIds,
                    plannerToolCatalog,
                    plannerModel,
                    runtimeSettings),
                cancellationToken);
            if (!dynamicPlanResult.IsSuccess)
            {
                return Result.From(dynamicPlanResult);
            }

            steps = dynamicPlanResult.Value!;
        }
        else
        {
            steps = BuildPlanSteps(uploadIds.Length > 0, knowledgeBaseIds.Length > 0, request.TaskType, riskLevel);
        }

        steps = EnsureMandatorySteps(steps, uploadIds.Length > 0, knowledgeBaseIds.Length > 0, request.TaskType);
        var guardedStepsResult = await planToolGuard.ValidateStepsAsync(steps, request.TaskType, userId, cancellationToken);
        if (!guardedStepsResult.IsSuccess)
        {
            return Result.From(guardedStepsResult);
        }

        steps = riskLevel >= AgentTaskRiskLevel.High
            ? guardedStepsResult.Value!.Select(step => step with { RequiresApproval = true }).ToArray()
            : guardedStepsResult.Value!;
        AgentTaskPlanCloudReadonlyIntentDocument? cloudReadonlyIntent = null;
        if (request.TaskType == AgentTaskType.CloudDataReport)
        {
            var cloudIntentResult = await cloudReadonlyPlanService.CreateIntentAsync(
                request.SessionId,
                request.Goal,
                cancellationToken);
            if (!cloudIntentResult.IsSuccess)
            {
                return Result.From(cloudIntentResult);
            }

            cloudReadonlyIntent = AgentTaskPlanCloudReadonlyIntentDocument.From(cloudIntentResult.Value!);
        }

        var plan = new AgentTaskPlanDocument(
            1,
            "agent_planner",
            request.Goal,
            request.TaskType.ToString(),
            riskLevel.ToString(),
            uploadIds,
            knowledgeBaseIds,
            cloudReadonlyIntent,
            steps.Select(step => new AgentTaskPlanStepDocument(
                    step.Title,
                    step.Description,
                    step.StepType,
                    step.ToolCode,
                    step.RequiresApproval,
                    step.InputJson))
                .ToArray(),
            new AgentTaskPlanRuntimeSettingsDocument(
                runtimeSettings.AgentPlanningHistoryCount,
                runtimeSettings.ContextTokenLimit),
            plannerModel is null ? "Static" : "Dynamic",
            plannerModel?.Id.Value,
            PlannerValidationVersion,
            plannerToolCatalog?.Version ?? PlannerToolCatalog.CurrentVersion,
            plannerToolCatalog?.AvailableToolCount ?? 0);

        var now = DateTimeOffset.UtcNow;
        var task = new AgentTask(
            new SessionId(request.SessionId),
            userId,
            BuildTitle(request.Goal),
            request.Goal,
            request.TaskType,
            riskLevel,
            plannerModel?.Id,
            JsonSerializer.Serialize(plan, JsonOptions),
            now);

        foreach (var step in steps)
        {
            task.AddStep(step.Title, step.Description, step.StepType, step.ToolCode, step.RequiresApproval, now, step.InputJson);
        }

        var workspace = await workspaceService.CreateForTaskAsync(task, now, cancellationToken);
        task.AttachWorkspace(workspace.Id, now);
        var approval = new ApprovalRequest(
            task.Id,
            AgentApprovalType.Plan,
            task.Id.Value.ToString(),
            userId,
            now);
        repository.Add(task);
        approvalRepository.Add(approval);
        await auditRecorder.RecordPlanAsync(
            task,
            AuditResults.Succeeded,
            "Agent task plan generated and is waiting for user approval.",
            pendingApprovalCount: 1,
            cancellationToken);
        await repository.SaveChangesAsync(cancellationToken);
        return Result.Success(AgentTaskDtoMapper.Map(task, workspace.WorkspaceCode, pendingApprovalCount: 1));
    }

    private static Result MissingUser()
    {
        return Result.Unauthorized(new ApiProblemDescriptor(
            AuthProblemCodes.Unauthorized,
            "Current user id is missing or invalid."));
    }

    private static AgentTaskRiskLevel DetermineRiskLevel(AgentTaskType taskType)
    {
        return taskType is AgentTaskType.CloudDataReport
            ? AgentTaskRiskLevel.Medium
            : AgentTaskRiskLevel.Low;
    }

    private static string BuildTitle(string goal)
    {
        var normalized = string.Join(' ', (goal ?? string.Empty).Split(default(string[]), StringSplitOptions.RemoveEmptyEntries));
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return "A助理任务";
        }

        return normalized.Length <= 48 ? normalized : normalized[..48];
    }

    private bool IsAdmin()
    {
        return string.Equals(currentUser.Role, "Admin", StringComparison.OrdinalIgnoreCase);
    }

    private async Task<Result<LanguageModel?>> ResolvePlannerModelAsync(
        Guid? modelId,
        CancellationToken cancellationToken)
    {
        if (modelId.HasValue)
        {
            var model = await modelRepository.FirstOrDefaultAsync(
                new LanguageModelByIdSpec(new LanguageModelId(modelId.Value)),
                cancellationToken);
            return model is not null && IsPlannerModelUsable(model)
                ? Result.Success<LanguageModel?>(model)
                : Result.Failure(new ApiProblemDescriptor(
                    AppProblemCodes.PlannerModelUnavailable,
                    "Specified planner model is unavailable or does not support Planner usage."));
        }

        var models = await modelRepository.ListAsync(new LanguageModelsOrderedSpec(), cancellationToken);
        var plannerModel = models.FirstOrDefault(IsPlannerModelUsable);
        return Result.Success<LanguageModel?>(plannerModel);
    }

    private static bool IsPlannerModelUsable(LanguageModel model)
    {
        return model.IsEnabled &&
               model.Usage.HasFlag(LanguageModelUsage.Planner) &&
               !string.IsNullOrWhiteSpace(model.ApiKey);
    }

    private static IReadOnlyCollection<AgentStepPlanDto> EnsureMandatorySteps(
        IReadOnlyCollection<AgentStepPlanDto> steps,
        bool hasUploads,
        bool hasKnowledgeBases,
        AgentTaskType taskType)
    {
        var result = steps.ToList();
        if (hasUploads)
        {
            InsertBeforeOutputs(
                result,
                "read_uploaded_file",
                new AgentStepPlanDto(
                    "Read uploaded files",
                    "Read task uploads into the controlled workspace source area.",
                    AgentStepType.FileRead,
                    "read_uploaded_file",
                    false));
            InsertBeforeOutputs(
                result,
                "parse_table_file",
                new AgentStepPlanDto(
                    "Parse table files",
                    "Parse CSV, JSON, or XLSX uploads into normalized data.",
                    AgentStepType.Analysis,
                    "parse_table_file",
                    false));
        }

        if (hasKnowledgeBases)
        {
            InsertBeforeOutputs(
                result,
                "rag_search",
                new AgentStepPlanDto(
                    "Search knowledge bases",
                    "Retrieve only authorized knowledge base context for the task.",
                    AgentStepType.RagSearch,
                    "rag_search",
                    false));
        }

        if (taskType == AgentTaskType.CloudDataReport)
        {
            InsertBeforeOutputs(
                result,
                "query_cloud_data_readonly",
                new AgentStepPlanDto(
                    "Query Cloud readonly data",
                    "Read Cloud business data only through the AiRead readonly boundary.",
                    AgentStepType.DataQuery,
                    "query_cloud_data_readonly",
                    false));
        }

        if (!ContainsTool(result, "finalize_artifacts"))
        {
            result.Add(new AgentStepPlanDto(
                "Confirm final output",
                "Wait for final output approval before moving draft artifacts to final.",
                AgentStepType.Finalize,
                "finalize_artifacts",
                true));
        }

        return result;
    }

    private static void InsertBeforeOutputs(
        List<AgentStepPlanDto> steps,
        string toolCode,
        AgentStepPlanDto step)
    {
        if (ContainsTool(steps, toolCode))
        {
            return;
        }

        var index = steps.FindIndex(item =>
            item.StepType is AgentStepType.ChartGeneration or AgentStepType.ArtifactGeneration or AgentStepType.Finalize);
        if (index < 0)
        {
            steps.Add(step);
        }
        else
        {
            steps.Insert(index, step);
        }
    }

    private static bool ContainsTool(IEnumerable<AgentStepPlanDto> steps, string toolCode)
    {
        return steps.Any(step => string.Equals(step.ToolCode, toolCode, StringComparison.OrdinalIgnoreCase));
    }

    private static IReadOnlyCollection<AgentStepPlanDto> BuildPlanSteps(
        bool hasUploads,
        bool hasKnowledgeBases,
        AgentTaskType taskType,
        AgentTaskRiskLevel riskLevel)
    {
        var steps = new List<AgentStepPlanDto>();
        if (hasUploads)
        {
            steps.Add(new AgentStepPlanDto("读取上传文件", "读取当前任务绑定的上传文件，复制到受控工作区 source 目录并生成输入摘要。", AgentStepType.FileRead, "read_uploaded_file", false));
            steps.Add(new AgentStepPlanDto("解析表格文件", "对 CSV、JSON 或 XLSX 输入进行结构化解析，输出规范化 JSON/CSV 数据。", AgentStepType.Analysis, "parse_table_file", false));
        }

        if (hasKnowledgeBases)
        {
            steps.Add(new AgentStepPlanDto("检索知识库", "基于任务目标检索绑定知识库并保留来源。", AgentStepType.RagSearch, "rag_search", false));
        }

        if (taskType == AgentTaskType.CloudDataReport)
        {
            steps.Add(new AgentStepPlanDto("读取 Cloud 只读数据", "仅通过 Cloud AiRead 只读边界读取业务摘要。", AgentStepType.DataQuery, "query_cloud_data_readonly", false));
        }

        steps.Add(new AgentStepPlanDto("生成图表数据", "基于可用输入生成前端图表预览数据。", AgentStepType.ChartGeneration, "generate_chart_data", false));
        steps.Add(new AgentStepPlanDto("生成 Markdown 报告", "在受控工作区 draft 目录生成 Markdown 草稿。", AgentStepType.ArtifactGeneration, "generate_markdown_report", false));
        steps.Add(new AgentStepPlanDto("生成 HTML 报告", "在受控工作区 draft 目录生成 HTML 草稿。", AgentStepType.ArtifactGeneration, "generate_html_report", false));
        steps.Add(new AgentStepPlanDto("生成 PDF 草稿", "在受控工作区 draft 目录生成基础 PDF 报告草稿。", AgentStepType.ArtifactGeneration, "generate_pdf", true));
        steps.Add(new AgentStepPlanDto("生成 PPTX 草稿", "在受控工作区 draft 目录生成基础 PPTX 汇报草稿。", AgentStepType.ArtifactGeneration, "generate_pptx", true));
        steps.Add(new AgentStepPlanDto("生成 XLSX 草稿", "在受控工作区 draft 目录生成基础 XLSX 数据草稿。", AgentStepType.ArtifactGeneration, "generate_xlsx", true));
        steps.Add(new AgentStepPlanDto("确认正式输出", "正式输出前等待用户确认，不自动进入 final 目录。", AgentStepType.Finalize, "finalize_artifacts", true));

        return riskLevel >= AgentTaskRiskLevel.High
            ? steps.Select(step => step with { RequiresApproval = true }).ToArray()
            : steps;
    }
}

public sealed class ApproveAgentTaskPlanCommandHandler(
    IRepository<AgentTask> repository,
    IRepository<ApprovalRequest> approvalRepository,
    IReadRepository<ArtifactWorkspace> workspaceRepository,
    AgentAuditRecorder auditRecorder,
    ICurrentUser currentUser)
    : ICommandHandler<ApproveAgentTaskPlanCommand, Result<AgentTaskDto>>
{
    public async Task<Result<AgentTaskDto>> Handle(ApproveAgentTaskPlanCommand request, CancellationToken cancellationToken)
    {
        var taskResult = await AgentTaskCommandLoader.LoadTaskAsync(repository, currentUser, request.Id, cancellationToken);
        if (!taskResult.IsSuccess)
        {
            return Result.From(taskResult);
        }

        var task = taskResult.Value!;
        var userId = currentUser.Id!.Value;
        var now = DateTimeOffset.UtcNow;
        var approval = await approvalRepository.FirstOrDefaultAsync(
            new PendingApprovalRequestByTaskAndTargetSpec(
                task.Id,
                AgentApprovalType.Plan,
                task.Id.Value.ToString()),
            cancellationToken);
        if (approval is null && task.Status == AgentTaskStatus.WaitingPlanApproval)
        {
            approval = new ApprovalRequest(
                task.Id,
                AgentApprovalType.Plan,
                task.Id.Value.ToString(),
                task.UserId,
                now);
            approvalRepository.Add(approval);
        }

        if (approval is not null)
        {
            approval.Approve(userId, "Plan approved.", now);
            approvalRepository.Update(approval);
        }

        if (task.Status == AgentTaskStatus.WaitingPlanApproval)
        {
            task.ApprovePlan(now);
        }

        repository.Update(task);
        if (approval is not null)
        {
            await auditRecorder.RecordApprovalDecisionAsync(
                approval,
                task,
                AuditResults.Succeeded,
                "Agent task plan approved.",
                cancellationToken);
        }

        await repository.SaveChangesAsync(cancellationToken);
        return Result.Success(await AgentTaskDtoComposer.MapAsync(task, workspaceRepository, approvalRepository, cancellationToken));
    }
}

public sealed class RunAgentTaskCommandHandler(
    IRepository<AgentTask> repository,
    IReadRepository<ArtifactWorkspace> workspaceRepository,
    IReadRepository<ApprovalRequest> approvalRepository,
    IReadRepository<AgentTaskRunQueueItem> queueRepository,
    IAgentTaskRunQueue runQueue,
    ICurrentUser currentUser)
    : ICommandHandler<RunAgentTaskCommand, Result<AgentTaskDto>>
{
    public async Task<Result<AgentTaskDto>> Handle(RunAgentTaskCommand request, CancellationToken cancellationToken)
    {
        var taskResult = await AgentTaskCommandLoader.LoadTaskAsync(repository, currentUser, request.Id, cancellationToken);
        if (!taskResult.IsSuccess)
        {
            return Result.From(taskResult);
        }

        var task = taskResult.Value!;
        if (task.Status is not AgentTaskStatus.PlanApproved and not AgentTaskStatus.WaitingToolApproval)
        {
            return Result.Invalid("Only approved or waiting-approval agent tasks can be queued for execution.");
        }

        var queued = await runQueue.EnqueueAsync(
            task,
            AgentTaskRunTriggerType.Manual,
            currentUser.Id!.Value,
            cancellationToken);
        return queued.IsSuccess
            ? Result.Success(await AgentTaskDtoComposer.MapAsync(
                task,
                workspaceRepository,
                approvalRepository,
                queueRepository,
                cancellationToken))
            : Result.From(queued);
    }
}

public sealed class RetryAgentTaskCommandHandler(
    IRepository<AgentTask> repository,
    IReadRepository<ArtifactWorkspace> workspaceRepository,
    IRepository<ApprovalRequest> approvalRepository,
    IReadRepository<AgentTaskRunQueueItem> queueReadRepository,
    IAgentTaskRunQueue runQueue,
    ICurrentUser currentUser)
    : ICommandHandler<RetryAgentTaskCommand, Result<AgentTaskDto>>
{
    public async Task<Result<AgentTaskDto>> Handle(RetryAgentTaskCommand request, CancellationToken cancellationToken)
    {
        var taskResult = await AgentTaskCommandLoader.LoadTaskAsync(repository, currentUser, request.Id, cancellationToken);
        if (!taskResult.IsSuccess)
        {
            return Result.From(taskResult);
        }

        var task = taskResult.Value!;
        var activeQueue = await queueReadRepository.FirstOrDefaultAsync(
            new ActiveAgentTaskRunQueueItemByTaskSpec(task.Id),
            cancellationToken);
        if (activeQueue is not null)
        {
            return Result.Failure(new ApiProblemDescriptor(
                AppProblemCodes.AgentTaskRunInProgress,
                "Agent task already has an active queued or leased run."));
        }

        if (task.Status != AgentTaskStatus.Failed)
        {
            return Result.Failure(new ApiProblemDescriptor(
                AppProblemCodes.AgentTaskRetryNotAllowed,
                "Only failed agent tasks can be retried. Completed, finalized, rejected, and cancelled tasks require a new task."));
        }

        if (task.WorkspaceId is not null)
        {
            var workspace = await workspaceRepository.FirstOrDefaultAsync(
                new ArtifactWorkspaceByIdSpec(task.WorkspaceId.Value, includeArtifacts: false),
                cancellationToken);
            if (workspace?.Status == ArtifactWorkspaceStatus.Finalized)
            {
                return Result.Failure(new ApiProblemDescriptor(
                    AppProblemCodes.AgentTaskRetryNotAllowed,
                    "Finalized workspaces cannot be retried. Create a new agent task instead."));
            }
        }

        var now = DateTimeOffset.UtcNow;
        await CancelPendingApprovalsAsync(task, approvalRepository, now, cancellationToken);
        task.PrepareRetry(now);
        repository.Update(task);
        await repository.SaveChangesAsync(cancellationToken);

        var queued = await runQueue.EnqueueAsync(
            task,
            AgentTaskRunTriggerType.Retry,
            currentUser.Id!.Value,
            cancellationToken);
        return queued.IsSuccess
            ? Result.Success(await AgentTaskDtoComposer.MapAsync(
                task,
                workspaceRepository,
                approvalRepository,
                queueReadRepository,
                cancellationToken))
            : Result.From(queued);
    }

    private static async Task CancelPendingApprovalsAsync(
        AgentTask task,
        IRepository<ApprovalRequest> approvalRepository,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var approvals = await approvalRepository.ListAsync(
            new ApprovalRequestsByTaskSpec(task.Id, pendingOnly: true),
            cancellationToken);
        foreach (var approval in approvals)
        {
            approval.Cancel(now);
            approvalRepository.Update(approval);
        }
    }
}

public sealed class CancelAgentTaskCommandHandler(
    IRepository<AgentTask> repository,
    IReadRepository<ArtifactWorkspace> workspaceRepository,
    IRepository<ApprovalRequest> approvalRepository,
    IRepository<AgentTaskRunAttempt> runAttemptRepository,
    IReadRepository<AgentTaskRunQueueItem> queueReadRepository,
    IAgentTaskRunQueue runQueue,
    ICurrentUser currentUser)
    : ICommandHandler<CancelAgentTaskCommand, Result<AgentTaskDto>>
{
    public async Task<Result<AgentTaskDto>> Handle(CancelAgentTaskCommand request, CancellationToken cancellationToken)
    {
        var taskResult = await AgentTaskCommandLoader.LoadTaskAsync(repository, currentUser, request.Id, cancellationToken);
        if (!taskResult.IsSuccess)
        {
            return Result.From(taskResult);
        }

        var task = taskResult.Value!;
        var now = DateTimeOffset.UtcNow;
        await runQueue.CancelActiveAsync(task, now, "Agent task cancellation requested.", cancellationToken);
        var approvals = await approvalRepository.ListAsync(
            new ApprovalRequestsByTaskSpec(task.Id, pendingOnly: true),
            cancellationToken);
        foreach (var approval in approvals)
        {
            approval.Cancel(now);
            approvalRepository.Update(approval);
        }

        if (task.ActiveRunAttemptId is not null)
        {
            var attempt = await runAttemptRepository.FirstOrDefaultAsync(
                new AgentTaskRunAttemptByIdSpec(task.ActiveRunAttemptId.Value),
                cancellationToken);
            if (attempt is not null && !attempt.IsTerminal)
            {
                attempt.Cancel(now, "Agent task cancellation requested.");
                runAttemptRepository.Update(attempt);
            }
        }

        task.Cancel(now);
        repository.Update(task);
        await repository.SaveChangesAsync(cancellationToken);
        return Result.Success(await AgentTaskDtoComposer.MapAsync(
            task,
            workspaceRepository,
            approvalRepository,
            queueReadRepository,
            cancellationToken));
    }
}

internal static class AgentTaskCommandLoader
{
    public static async Task<Result<AgentTask>> LoadTaskAsync(
        IRepository<AgentTask> repository,
        ICurrentUser currentUser,
        Guid id,
        CancellationToken cancellationToken)
    {
        if (currentUser.Id is not { } userId)
        {
            return Result.Unauthorized(new ApiProblemDescriptor(
                AuthProblemCodes.Unauthorized,
                "Current user id is missing or invalid."));
        }

        if (id == Guid.Empty)
        {
            return Result.Invalid("Agent task id is required.");
        }

        var task = await repository.FirstOrDefaultAsync(
            new AgentTaskByIdForUserSpec(new AgentTaskId(id), userId, includeSteps: true),
            cancellationToken);
        return task is null ? Result.NotFound() : Result.Success(task);
    }
}
