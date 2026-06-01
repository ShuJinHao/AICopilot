using System.Text.Json;
using System.Text.Json.Serialization;
using AICopilot.AiGatewayService.CloudReadiness;
using AICopilot.AiGatewayService.Runtime;
using AICopilot.AiGatewayService.Tools;
using AICopilot.AiGatewayService.Workspaces;
using AICopilot.Core.AiGateway.Aggregates.AgentTasks;
using AICopilot.Core.AiGateway.Aggregates.Approvals;
using AICopilot.Core.AiGateway.Aggregates.Artifacts;
using AICopilot.Core.AiGateway.Aggregates.ConversationTemplate;
using AICopilot.Core.AiGateway.Aggregates.LanguageModel;
using AICopilot.Core.AiGateway.Aggregates.Sessions;
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
    string? TrialScenarioId = null,
    string? TrialScenarioTitle = null,
    bool IsSimulationTrial = false,
    string? PlannerMode = null,
    bool ForceStaticPlanner = false,
    bool IsCloudSandboxTrial = false,
    bool IsCloudSandboxControlledTrial = false,
    CloudSandboxGoalIntentDto? CloudSandboxGoalIntent = null,
    bool IsCloudProductionPilotTrial = false,
    bool IsCloudProductionControlledPilotTrial = false,
    CloudProductionGoalIntentDto? CloudProductionGoalIntent = null) : ICommand<Result<AgentTaskDto>>;

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
    CloudReadonlySandboxControlledTrialService? cloudSandboxControlledTrialService = null,
    CloudReadonlyProductionControlledPilotService? cloudProductionControlledPilotService = null,
    CloudReadonlyProductionPilotService? cloudReadonlyProductionPilotService = null,
    CloudReadonlyPilotReadinessService? cloudReadonlyPilotReadinessService = null,
    IReadRepository<ToolRegistration>? toolReadRepository = null)
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
            businessDatabaseReadService,
            cloudSandboxControlledTrialService,
            cloudProductionControlledPilotService,
            cloudReadonlyProductionPilotService,
            cloudReadonlyPilotReadinessService,
            toolReadRepository);
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

        _ = await templateRepository.FirstOrDefaultAsync(
            new ConversationTemplateByCodeSpec("agent_planner"),
            cancellationToken);
        var runtimeSettings = await runtimeSettingsProvider.GetAsync(cancellationToken);
        var riskLevel = AgentTaskPlanMetadataBuilder.DetermineRiskLevel(request.TaskType);
        var requestedPlannerMode = AgentTaskPlanMetadataBuilder.NormalizePlannerMode(request.PlannerMode, request.ForceStaticPlanner);
        var plannerModelResult = requestedPlannerMode == AgentPlannerMode.StaticOnly ||
                                 preparation.IsCloudProductionPilotTrialPlan ||
                                 preparation.IsCloudProductionControlledPilotPlan
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
                preparation.IsCloudSandboxTrialPlan,
                preparation.IsCloudProductionPilotTrialPlan,
                preparation.IsCloudProductionControlledPilotPlan,
                cancellationToken);
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
                    preparation.UploadIds,
                    preparation.KnowledgeBaseIds,
                    plannerToolCatalog,
                    plannerModel,
                    runtimeSettings,
                    AgentTaskPlanMetadataBuilder.BuildPlannerDataSourceSummaries(preparation.SelectedDataSources),
                    preparation.BusinessDomains,
                    request.QueryMode ?? "TextToSql",
                    AgentTaskPlanStepBuilder.NormalizeArtifactTypes(request.ArtifactTypes)?.ToArray(),
                request.TrialScenarioId,
                request.TrialScenarioTitle,
                request.IsSimulationTrial,
                request.RequiresDataApproval),
                cancellationToken);
            if (!dynamicPlanResult.IsSuccess)
            {
                if (requestedPlannerMode == AgentPlannerMode.Auto &&
                    dynamicPlanResult.Errors?.OfType<ApiProblemDescriptor>().Any(error =>
                        string.Equals(error.Code, AppProblemCodes.PlannerModelUnavailable, StringComparison.Ordinal)) == true)
                {
                    effectivePlannerMode = "StaticFallback";
                    plannerFallbackReason = "Planner model failed before producing a valid plan; static fallback was used.";
                    steps = AgentTaskPlanStepBuilder.BuildPlanSteps(
                        preparation.UploadIds.Length > 0,
                        preparation.KnowledgeBaseIds.Length > 0,
                        preparation.HasBusinessDataSourcesForPlan,
                        request.TaskType,
                        riskLevel,
                        request.ArtifactTypes,
                        preparation.IsCloudSandboxTrialPlan,
                        preparation.IsCloudProductionPilotTrialPlan,
                        preparation.IsCloudProductionControlledPilotPlan);
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
                request.TaskType,
                riskLevel,
                request.ArtifactTypes,
                preparation.IsCloudSandboxTrialPlan,
                preparation.IsCloudProductionPilotTrialPlan,
                preparation.IsCloudProductionControlledPilotPlan);
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
            request.TaskType,
            request.RequiresDataApproval,
            request.ArtifactTypes,
            preparation.IsCloudSandboxTrialPlan,
            preparation.IsCloudProductionPilotTrialPlan,
            preparation.IsCloudProductionControlledPilotPlan);
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
                preparation.IsCloudSandboxTrialPlan,
                preparation.IsCloudProductionPilotTrialPlan,
                preparation.IsCloudProductionControlledPilotPlan,
                cancellationToken);
            if (!staticCatalogResult.IsSuccess)
            {
                return Result.From(staticCatalogResult);
            }

            plannerToolCatalog = staticCatalogResult.Value!;
        }

        var guardedStepsResult = await planToolGuard.ValidateStepsAsync(
            steps,
            request.TaskType,
            userId,
            preparation.IsSimulationOnlyPlan,
            preparation.BusinessDomains,
            preparation.IsCloudSandboxTrialPlan,
            preparation.IsCloudProductionPilotTrialPlan,
            preparation.IsCloudProductionControlledPilotPlan,
            cancellationToken);
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
        if (request.TaskType == AgentTaskType.CloudDataReport &&
            !preparation.IsCloudSandboxTrialPlan &&
            !preparation.IsCloudProductionPilotTrialPlan &&
            !preparation.IsCloudProductionControlledPilotPlan)
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
            request.QueryMode ?? "TextToSql",
            request.RequiresDataApproval,
            AgentTaskPlanStepBuilder.NormalizeArtifactTypes(request.ArtifactTypes)?.ToArray(),
            request.TrialScenarioId,
            request.TrialScenarioTitle,
            request.IsSimulationTrial,
            preparation.IsCloudSandboxControlledTrialPlan,
            request.CloudSandboxGoalIntent,
            preparation.IsCloudProductionControlledPilotPlan,
            request.CloudProductionGoalIntent,
            new AgentTaskPlanSafetySummaryDocument(
                preparation.IsCloudProductionControlledPilotPlan
                    ? "CloudProductionControlledGoal"
                    : preparation.IsCloudProductionPilotTrialPlan
                    ? "CloudProductionPilotFixedScenario"
                    : preparation.IsCloudSandboxControlledTrialPlan
                    ? "CloudSandboxControlledGoal"
                    : string.IsNullOrWhiteSpace(request.TrialScenarioId)
                        ? "FreeGoal"
                        : "TrialScenario",
                effectivePlannerMode,
                plannerModel is null ? null : $"{plannerModel.Provider}/{plannerModel.Name}",
                plannerToolCatalog?.Version ?? PlannerToolCatalog.CurrentVersion,
                plannerToolCatalog?.AvailableToolCount ?? 0,
                preparation.IsSimulationOnlyPlan,
                request.RequiresDataApproval,
                toolRiskSummary,
                MockMcpOnly: !preparation.IsCloudSandboxTrialPlan && !preparation.IsCloudProductionPilotTrialPlan && !preparation.IsCloudProductionControlledPilotPlan),
            forcedStepCodes,
            approvalCheckpoints,
            AgentTaskPlanMetadataBuilder.BuildPlanDataSourceSummaries(preparation.SelectedDataSources),
            plannerToolCatalog?.Version ?? PlannerToolCatalog.CurrentVersion,
            plannerToolCatalog?.AvailableToolCount ?? 0,
            toolRiskSummary,
            MockMcpOnly: !preparation.IsCloudSandboxTrialPlan && !preparation.IsCloudProductionPilotTrialPlan && !preparation.IsCloudProductionControlledPilotPlan,
            toolApprovalCheckpoints,
            preparation.IsCloudProductionPilotTrialPlan);

        var now = DateTimeOffset.UtcNow;
        var task = new AgentTask(
            new SessionId(request.SessionId),
            userId,
            AgentTaskPlanMetadataBuilder.BuildTitle(request.Goal),
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
}
