using System.Text.Json;
using System.Text.Json.Serialization;
using AICopilot.AiGatewayService.Runtime;
using AICopilot.AiGatewayService.Sessions;
using AICopilot.AiGatewayService.Skills;
using AICopilot.AiGatewayService.Tools;
using AICopilot.AiGatewayService.Workspaces;
using AICopilot.Core.AiGateway.Aggregates.AgentTasks;
using AICopilot.Core.AiGateway.Aggregates.Approvals;
using AICopilot.Core.AiGateway.Aggregates.Artifacts;
using AICopilot.Core.AiGateway.Aggregates.ConversationTemplate;
using AICopilot.Core.AiGateway.Aggregates.LanguageModel;
using AICopilot.Core.AiGateway.Aggregates.Sessions;
using AICopilot.Core.AiGateway.Aggregates.Skills;
using AICopilot.Core.AiGateway.Aggregates.Tools;
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
using Microsoft.Extensions.Options;

namespace AICopilot.AiGatewayService.AgentTasks;

[AuthorizeRequirement("AiGateway.PlanAgentTask")]
public sealed record PlanAgentTaskCommand(
    Guid SessionId,
    string Goal,
    AgentTaskType TaskType,
    Guid? ModelId,
    IReadOnlyCollection<Guid>? UploadIds = null,
    IReadOnlyCollection<Guid>? KnowledgeBaseIds = null,
    IReadOnlyCollection<Guid>? DataSourceIds = null,
    IReadOnlyCollection<string>? BusinessDomains = null,
    string? QueryMode = null,
    bool RequiresDataApproval = false,
    IReadOnlyCollection<string>? ArtifactTypes = null,
    string? PlannerMode = null,
    bool ForceStaticPlanner = false,
    string? SkillCode = null,
    IReadOnlyCollection<string>? PreferredToolCodes = null) : ICommand<Result<AgentTaskDto>>;

public enum AgentPlannerMode
{
    Auto,
    DynamicOnly,
    StaticOnly
}
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
    ICurrentUser currentUser,
    IBusinessDatabaseReadService? businessDatabaseReadService = null,
    MessageTimelineProjectionWriter? timelineProjectionWriter = null,
    SkillDefinitionGuard? skillDefinitionGuard = null,
    IAgentSkillAutoSelector? skillAutoSelector = null)
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

        var preparationService = new AgentTaskPlanPreparationService(
            sessionRepository,
            uploadRepository,
            knowledgeBaseAccessCheckers,
            businessDatabaseReadService);
        var preparationResult = await preparationService.PrepareAsync(
            request,
            userId,
            IsAdmin(),
            cancellationToken);
        if (!preparationResult.IsSuccess)
        {
            return Result.From(preparationResult);
        }

        var preparation = preparationResult.Value!;
        var skillSelection = await ResolveEffectiveSkillAsync(request, cancellationToken);
        if (AutoSkillSelectionRequired(request) && string.IsNullOrWhiteSpace(skillSelection?.SkillCode))
        {
            return Result.Failure(new ApiProblemDescriptor(
                AppProblemCodes.AgentSkillSelectionRequired,
                skillSelection?.Reason?.Trim() is { Length: > 0 } reason
                    ? reason
                    : "无法自动识别合适的 Skill。请补充任务目标，或手动选择一个 Skill 后重新生成计划。"));
        }

        var effectiveSkillCode = skillSelection?.SkillCode;
        var skillResult = skillDefinitionGuard is null
            ? Result.Success<SkillDefinition?>(null)
            : await skillDefinitionGuard.ResolveAsync(effectiveSkillCode, cancellationToken);
        if (!skillResult.IsSuccess)
        {
            return Result.From(skillResult);
        }

        var selectedSkill = skillResult.Value;
        var effectiveTaskType = ResolveEffectiveTaskType(request.TaskType, selectedSkill);
        var effectiveQueryMode = request.QueryMode ??
            (effectiveTaskType == AgentTaskType.CloudDataReport ? "CloudReadonly" : "TextToSql");
        var effectivePlanSource = selectedSkill is null ? "FreeGoal" : $"Skill.{selectedSkill.SkillCode}";

        _ = await templateRepository.FirstOrDefaultAsync(
            new ConversationTemplateByCodeSpec("agent_planner"),
            cancellationToken);
        var runtimeSettings = await runtimeSettingsProvider.GetAsync(cancellationToken);
        var riskLevel = AgentTaskPlanMetadataBuilder.DetermineRiskLevel(effectiveTaskType);
        var requestedArtifactTypes = AgentTaskPlanStepBuilder.NormalizeArtifactTypes(request.ArtifactTypes)?.ToArray();
        var skillDefaultArtifactTypes = AgentTaskPlanStepBuilder.NormalizeArtifactTypes(selectedSkill?.OutputComponentTypes)?.ToArray();
        var effectiveArtifactTypes = requestedArtifactTypes ?? skillDefaultArtifactTypes;
        var requestedPlannerMode = AgentTaskPlanMetadataBuilder.NormalizePlannerMode(request.PlannerMode, request.ForceStaticPlanner);
        var plannerModelResult = requestedPlannerMode == AgentPlannerMode.StaticOnly
            ? Result.Success<LanguageModel?>(null)
            : await ResolvePlannerModelAsync(request.ModelId, cancellationToken);
        if (!plannerModelResult.IsSuccess)
        {
            return Result.From(plannerModelResult);
        }

        var plannerModel = plannerModelResult.Value;
        if (plannerModel is null && requestedPlannerMode == AgentPlannerMode.DynamicOnly)
        {
            return Result.Failure(new ApiProblemDescriptor(
                AppProblemCodes.PlannerModelUnavailable,
                "No enabled planner model is available for DynamicOnly planning."));
        }

        var effectivePlannerMode = requestedPlannerMode == AgentPlannerMode.StaticOnly ? "Static" : "StaticFallback";
        string? plannerFallbackReason = plannerModel is null && requestedPlannerMode == AgentPlannerMode.Auto
            ? "No enabled planner model is available; static fallback was used."
            : null;
        PlannerToolCatalog? plannerToolCatalog = null;
        IReadOnlyCollection<AgentStepPlanDto> steps;
        if (plannerModel is not null)
        {
            var catalogResult = await planToolGuard.GetAvailableToolCatalogAsync(
                userId,
                preparation.IsSimulationOnlyPlan,
                preparation.BusinessDomains,
                cancellationToken,
                selectedSkill?.SkillCode ?? effectiveSkillCode);
            if (!catalogResult.IsSuccess)
            {
                return Result.From(catalogResult);
            }

            plannerToolCatalog = catalogResult.Value!;
            var preferredCatalogResult = ApplyPreferredToolCodes(plannerToolCatalog, request.PreferredToolCodes);
            if (!preferredCatalogResult.IsSuccess)
            {
                return Result.From(preferredCatalogResult);
            }

            plannerToolCatalog = preferredCatalogResult.Value!;
            if (plannerToolCatalog.AvailableToolCount == 0)
            {
                return Result.Failure(new ApiProblemDescriptor(
                    AppProblemCodes.PlannerToolCatalogEmpty,
                    "Planner model is available, but no enabled and authorized tools are available for the current user."));
            }

            var dynamicPlanResult = await dynamicPlanner.CreatePlanAsync(
                new AgentDynamicPlannerRequest(
                    request.Goal,
                    effectiveTaskType,
                    preparation.UploadIds,
                    preparation.KnowledgeBaseIds,
                    plannerToolCatalog,
                    plannerModel,
                    runtimeSettings,
                    AgentTaskPlanMetadataBuilder.BuildPlannerDataSourceSummaries(preparation.SelectedDataSources),
                    preparation.BusinessDomains,
                    effectiveQueryMode,
                    effectiveArtifactTypes,
                    request.RequiresDataApproval,
                    selectedSkill?.SkillCode,
                    selectedSkill?.DisplayName,
                    selectedSkill?.Description),
                cancellationToken);
            if (!dynamicPlanResult.IsSuccess)
            {
                if (requestedPlannerMode == AgentPlannerMode.Auto && CanFallbackToStaticPlan(dynamicPlanResult))
                {
                    effectivePlannerMode = "StaticFallback";
                    plannerFallbackReason = BuildPlannerFallbackReason(dynamicPlanResult);
                    steps = AgentTaskPlanStepBuilder.BuildPlanSteps(
                        preparation.UploadIds.Length > 0,
                        preparation.KnowledgeBaseIds.Length > 0,
                        preparation.HasBusinessDataSourcesForPlan,
                        effectiveTaskType,
                        riskLevel,
                        effectiveArtifactTypes);
                    goto ValidateAndPersistPlan;
                }

                return Result.From(dynamicPlanResult);
            }

            steps = dynamicPlanResult.Value!;
            effectivePlannerMode = "Dynamic";
            plannerFallbackReason = null;
        }
        else
        {
            steps = AgentTaskPlanStepBuilder.BuildPlanSteps(
                preparation.UploadIds.Length > 0,
                preparation.KnowledgeBaseIds.Length > 0,
                preparation.HasBusinessDataSourcesForPlan,
                effectiveTaskType,
                riskLevel,
                effectiveArtifactTypes);
        }

ValidateAndPersistPlan:
        var originalToolCodes = steps
            .Select(step => step.ToolCode)
            .Where(toolCode => !string.IsNullOrWhiteSpace(toolCode))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        steps = AgentTaskPlanStepBuilder.EnsureMandatorySteps(
            steps,
            preparation.UploadIds.Length > 0,
            preparation.KnowledgeBaseIds.Length > 0,
            preparation.HasBusinessDataSourcesForPlan,
            effectiveTaskType,
            request.RequiresDataApproval,
            effectiveArtifactTypes);
        var forcedStepCodes = steps
            .Select(step => step.ToolCode)
            .Where(toolCode => !string.IsNullOrWhiteSpace(toolCode) && !originalToolCodes.Contains(toolCode!))
            .Select(toolCode => toolCode!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (plannerToolCatalog is null)
        {
            var staticCatalogResult = await planToolGuard.GetAvailableToolCatalogAsync(
                userId,
                preparation.IsSimulationOnlyPlan,
                preparation.BusinessDomains,
                cancellationToken,
                selectedSkill?.SkillCode ?? effectiveSkillCode);
            if (!staticCatalogResult.IsSuccess)
            {
                return Result.From(staticCatalogResult);
            }

            plannerToolCatalog = staticCatalogResult.Value!;
            var preferredCatalogResult = ApplyPreferredToolCodes(plannerToolCatalog, request.PreferredToolCodes);
            if (!preferredCatalogResult.IsSuccess)
            {
                return Result.From(preferredCatalogResult);
            }

            plannerToolCatalog = preferredCatalogResult.Value!;
        }

        var guardedStepsResult = await planToolGuard.ValidateStepsAsync(
            steps,
            effectiveTaskType,
            userId,
            preparation.IsSimulationOnlyPlan,
            preparation.BusinessDomains,
            cancellationToken,
            selectedSkill?.SkillCode ?? effectiveSkillCode);
        if (!guardedStepsResult.IsSuccess)
        {
            return Result.From(guardedStepsResult);
        }

        steps = riskLevel >= AgentTaskRiskLevel.High
            ? guardedStepsResult.Value!.Select(step => step with { RequiresApproval = true }).ToArray()
            : guardedStepsResult.Value!;
        var approvalCheckpoints = steps
            .Where(step => step.RequiresApproval)
            .Select(step => string.IsNullOrWhiteSpace(step.ToolCode) ? step.Title : step.ToolCode!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var toolApprovalCheckpoints = steps
            .Where(step => step.RequiresApproval && !string.IsNullOrWhiteSpace(step.ToolCode))
            .Select(step => step.ToolCode!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var toolRiskSummary = AgentTaskPlanMetadataBuilder.BuildToolRiskSummary(plannerToolCatalog);
        AgentTaskPlanCloudReadonlyIntentDocument? cloudReadonlyIntent = null;
        if (effectiveTaskType == AgentTaskType.CloudDataReport)
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
            effectiveTaskType.ToString(),
            riskLevel.ToString(),
            preparation.UploadIds,
            preparation.KnowledgeBaseIds,
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
            effectivePlannerMode,
            plannerFallbackReason,
            plannerModel?.Id.Value,
            PlannerValidationVersion,
            plannerToolCatalog?.Version ?? PlannerToolCatalog.CurrentVersion,
            plannerToolCatalog?.AvailableToolCount ?? 0,
            preparation.DataSourceIds,
            preparation.BusinessDomains,
            effectiveQueryMode,
            request.RequiresDataApproval,
            effectiveArtifactTypes,
            new AgentTaskPlanSafetySummaryDocument(
                effectivePlanSource,
                effectivePlannerMode,
                plannerModel is null ? null : $"{plannerModel.Provider}/{plannerModel.Name}",
                plannerToolCatalog?.Version ?? PlannerToolCatalog.CurrentVersion,
                plannerToolCatalog?.AvailableToolCount ?? 0,
                preparation.IsSimulationOnlyPlan,
                request.RequiresDataApproval,
                toolRiskSummary,
                MockMcpOnly: true),
            forcedStepCodes,
            approvalCheckpoints,
            AgentTaskPlanMetadataBuilder.BuildPlanDataSourceSummaries(preparation.SelectedDataSources),
            plannerToolCatalog?.Version ?? PlannerToolCatalog.CurrentVersion,
            plannerToolCatalog?.AvailableToolCount ?? 0,
            toolRiskSummary,
            MockMcpOnly: true,
            toolApprovalCheckpoints,
            SkillCode: selectedSkill?.SkillCode,
            SkillName: selectedSkill?.DisplayName,
            SkillRoutingReason: skillSelection?.Reason);

        var now = DateTimeOffset.UtcNow;
        var task = new AgentTask(
            new SessionId(request.SessionId),
            userId,
            AgentTaskPlanMetadataBuilder.BuildTitle(request.Goal),
            request.Goal,
            effectiveTaskType,
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
        if (timelineProjectionWriter is not null)
        {
            await timelineProjectionWriter.StageAgentTaskPlanCreatedAsync(task, approval, cancellationToken);
        }

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

    private bool IsAdmin()
    {
        return string.Equals(currentUser.Role, "Admin", StringComparison.OrdinalIgnoreCase);
    }

    private async Task<AgentSkillSelection?> ResolveEffectiveSkillAsync(
        PlanAgentTaskCommand request,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(request.SkillCode))
        {
            return new AgentSkillSelection(request.SkillCode.Trim(), "用户手动选择 Skill。");
        }

        return skillAutoSelector is null
            ? null
            : await skillAutoSelector.SelectSkillAsync(request.SessionId, request.Goal, cancellationToken);
    }

    private bool AutoSkillSelectionRequired(PlanAgentTaskCommand request)
    {
        return skillAutoSelector is not null &&
               string.IsNullOrWhiteSpace(request.SkillCode);
    }

    private static AgentTaskType ResolveEffectiveTaskType(AgentTaskType requestedTaskType, SkillDefinition? skill)
    {
        return skill?.SkillCode switch
        {
            "cloud_readonly" => AgentTaskType.CloudDataReport,
            "data_analysis" => AgentTaskType.DataAnalysis,
            "knowledge_research" => AgentTaskType.RagAnswer,
            _ => requestedTaskType
        };
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

    private static bool CanFallbackToStaticPlan(Result<IReadOnlyCollection<AgentStepPlanDto>> dynamicPlanResult)
    {
        return dynamicPlanResult.Errors?.OfType<ApiProblemDescriptor>().Any(error =>
            string.Equals(error.Code, AppProblemCodes.PlannerModelUnavailable, StringComparison.Ordinal) ||
            string.Equals(error.Code, AppProblemCodes.AgentPlanInvalid, StringComparison.Ordinal)) == true;
    }

    private static string BuildPlannerFallbackReason(Result<IReadOnlyCollection<AgentStepPlanDto>> dynamicPlanResult)
    {
        var detail = dynamicPlanResult.Errors?
            .OfType<ApiProblemDescriptor>()
            .Select(error => error.Detail)
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

        return string.IsNullOrWhiteSpace(detail)
            ? "Planner model failed before producing a valid plan; static fallback was used."
            : $"Planner model did not produce a valid executable plan; static fallback was used. Detail: {detail}";
    }

    private static Result<PlannerToolCatalog> ApplyPreferredToolCodes(
        PlannerToolCatalog catalog,
        IReadOnlyCollection<string>? preferredToolCodes)
    {
        var preferred = preferredToolCodes?
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .Select(code => code.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (preferred is null || preferred.Length == 0)
        {
            return Result.Success(catalog);
        }

        var tools = catalog.Tools
            .Where(tool => preferred.Contains(tool.ToolCode, StringComparer.OrdinalIgnoreCase))
            .ToArray();
        var missing = preferred
            .Where(code => tools.All(tool => !string.Equals(tool.ToolCode, code, StringComparison.OrdinalIgnoreCase)))
            .ToArray();

        if (missing.Length > 0)
        {
            return Result.Failure(new ApiProblemDescriptor(
                AppProblemCodes.AgentPlanToolDenied,
                $"Preferred tool is unavailable or outside the current skill boundary: {string.Join(", ", missing)}."));
        }

        return Result.Success(new PlannerToolCatalog(
            catalog.Version,
            tools.Length,
            tools));
    }
}
