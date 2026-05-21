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
    private const string SimulationBusinessSourceLabel = "AI 独立模拟业务库";

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

        var isCloudSandboxFixedTrialPlan = request.IsCloudSandboxTrial ||
                                           CloudReadonlySandboxAgentTrialService.IsScenarioId(request.TrialScenarioId);
        var isCloudSandboxControlledTrialPlan = request.IsCloudSandboxControlledTrial ||
                                                request.CloudSandboxGoalIntent is not null;
        var isCloudSandboxTrialPlan = isCloudSandboxFixedTrialPlan || isCloudSandboxControlledTrialPlan;
        var isCloudProductionPilotTrialPlan = request.IsCloudProductionPilotTrial ||
                                             CloudReadonlyProductionPilotService.IsScenarioId(request.TrialScenarioId);
        var isCloudProductionControlledPilotPlan = request.IsCloudProductionControlledPilotTrial ||
                                                   request.CloudProductionGoalIntent is not null;
        if (isCloudSandboxFixedTrialPlan && !CloudReadonlySandboxAgentTrialService.IsScenarioId(request.TrialScenarioId))
        {
            return Result.Invalid("P7 CloudReadonlySandbox agent trial only allows fixed trial scenarios.");
        }

        if (isCloudProductionPilotTrialPlan && !CloudReadonlyProductionPilotService.IsScenarioId(request.TrialScenarioId))
        {
            return Result.Invalid("P12 CloudReadonlyProductionPilot only allows fixed Pilot scenarios.");
        }

        if ((isCloudProductionPilotTrialPlan || isCloudProductionControlledPilotPlan) && isCloudSandboxTrialPlan)
        {
            return Result.Invalid("CloudReadonlySandbox and CloudReadonlyProductionPilot scenarios cannot be mixed in one plan.");
        }

        if (isCloudProductionPilotTrialPlan && isCloudProductionControlledPilotPlan)
        {
            return Result.Invalid("P12 fixed production Pilot and P13 controlled production Pilot cannot be mixed in one plan.");
        }

        if (isCloudSandboxFixedTrialPlan && isCloudSandboxControlledTrialPlan)
        {
            return Result.Invalid("CloudReadonlySandbox fixed scenarios and controlled goals cannot be mixed in one plan.");
        }

        CloudReadonlyProductionPilotStatusDto? p12StatusForControlledPilot = null;
        IReadOnlyCollection<ToolRegistration>? protectedToolsForControlledPilot = null;
        if (isCloudProductionControlledPilotPlan)
        {
            if (cloudProductionControlledPilotService is null ||
                cloudReadonlyProductionPilotService is null ||
                cloudReadonlyPilotReadinessService is null ||
                toolReadRepository is null)
            {
                return Result.Failure("CloudReadonlyProductionControlledPilot services are not configured.");
            }

            protectedToolsForControlledPilot = await GetCloudReadonlyPilotReadinessQueryHandler.LoadProtectedToolRegistrationsAsync(
                toolReadRepository,
                cancellationToken);
            p12StatusForControlledPilot = cloudReadonlyProductionPilotService.BuildStatus(
                cloudReadonlyPilotReadinessService.BuildStatus(protectedToolsForControlledPilot),
                protectedToolsForControlledPilot);
            var intentValidation = cloudProductionControlledPilotService.ValidateIntentForPlan(
                request.CloudProductionGoalIntent,
                p12StatusForControlledPilot,
                protectedToolsForControlledPilot);
            if (!intentValidation.IsSuccess)
            {
                return Result.From(intentValidation);
            }
        }

        if (isCloudSandboxControlledTrialPlan)
        {
            if (cloudSandboxControlledTrialService is null)
            {
                return Result.Failure("CloudReadonlySandbox controlled trial service is not configured.");
            }

            var intentValidation = cloudSandboxControlledTrialService.ValidateIntentForPlan(request.CloudSandboxGoalIntent);
            if (!intentValidation.IsSuccess)
            {
                return Result.From(intentValidation);
            }
        }

        var dataSourceIds = (request.DataSourceIds ?? [])
            .Where(id => id != Guid.Empty)
            .Distinct()
            .ToArray();
        BusinessDatabaseDescriptor[] selectedDataSources = [];
        if (dataSourceIds.Length > 0)
        {
            if (businessDatabaseReadService is null)
            {
                return Result.Failure("Business database read service is not configured.");
            }

            var visibleDataSources = await businessDatabaseReadService.ListEnabledAsync(cancellationToken);
            selectedDataSources = visibleDataSources
                .Where(source => source.IsSelectableInAgent)
                .Where(source => dataSourceIds.Contains(source.Id))
                .ToArray();
            if (selectedDataSources.Length != dataSourceIds.Length)
            {
                return Result.NotFound();
            }

            if (isCloudSandboxTrialPlan || isCloudProductionPilotTrialPlan || isCloudProductionControlledPilotPlan)
            {
                return Result.Invalid("CloudReadonly agent trial cannot bind BusinessDatabase data sources.");
            }

            if (selectedDataSources.Any(source => source.ExternalSystemType != DataSourceExternalSystemType.SimulationBusiness))
            {
                return Result.Invalid("P3 dynamic planner data tasks can only use SimulationBusiness data sources.");
            }
        }

        var businessDomains = (request.BusinessDomains ?? [])
            .Where(domain => !string.IsNullOrWhiteSpace(domain))
            .Select(domain => domain.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (isCloudSandboxTrialPlan && businessDomains.Length == 0)
        {
            if (isCloudSandboxControlledTrialPlan)
            {
                businessDomains = request.CloudSandboxGoalIntent?.EndpointCodes.ToArray() ?? [];
            }
            else
            {
                var trialDomain = CloudReadonlySandboxAgentTrialService.ResolveScenarioDomain(request.TrialScenarioId);
                businessDomains = string.IsNullOrWhiteSpace(trialDomain) ? [] : [trialDomain];
            }
        }
        else if (isCloudProductionPilotTrialPlan && businessDomains.Length == 0)
        {
            var trialDomain = CloudReadonlyProductionPilotService.ResolveScenarioDomain(request.TrialScenarioId);
            businessDomains = string.IsNullOrWhiteSpace(trialDomain) ? [] : [trialDomain];
        }
        else if (isCloudProductionControlledPilotPlan && businessDomains.Length == 0)
        {
            businessDomains = request.CloudProductionGoalIntent?.EndpointCodes.ToArray() ?? [];
        }
        var isSimulationOnlyPlan = request.IsSimulationTrial ||
                                   selectedDataSources.Any(source =>
                                       source.ExternalSystemType == DataSourceExternalSystemType.SimulationBusiness);
        var hasBusinessDataSourcesForPlan = !isCloudSandboxTrialPlan &&
                                            !isCloudProductionPilotTrialPlan &&
                                            !isCloudProductionControlledPilotPlan &&
                                            (dataSourceIds.Length > 0 || businessDomains.Length > 0);

        _ = await templateRepository.FirstOrDefaultAsync(
            new ConversationTemplateByCodeSpec("agent_planner"),
            cancellationToken);
        var runtimeSettings = await runtimeSettingsProvider.GetAsync(cancellationToken);
        var riskLevel = DetermineRiskLevel(request.TaskType);
        var requestedPlannerMode = NormalizePlannerMode(request.PlannerMode, request.ForceStaticPlanner);
        var plannerModelResult = requestedPlannerMode == AgentPlannerMode.StaticOnly ||
                                 isCloudProductionPilotTrialPlan ||
                                 isCloudProductionControlledPilotPlan
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
                isSimulationOnlyPlan,
                businessDomains,
                isCloudSandboxTrialPlan,
                isCloudProductionPilotTrialPlan,
                isCloudProductionControlledPilotPlan,
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
                    uploadIds,
                    knowledgeBaseIds,
                    plannerToolCatalog,
                    plannerModel,
                    runtimeSettings,
                    BuildPlannerDataSourceSummaries(selectedDataSources),
                    businessDomains,
                    request.QueryMode ?? "TextToSql",
                    NormalizeArtifactTypes(request.ArtifactTypes)?.ToArray(),
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
                    steps = BuildPlanSteps(
                        uploadIds.Length > 0,
                        knowledgeBaseIds.Length > 0,
                        hasBusinessDataSourcesForPlan,
                        request.TaskType,
                        riskLevel,
                        request.ArtifactTypes,
                        isCloudSandboxTrialPlan,
                        isCloudProductionPilotTrialPlan,
                        isCloudProductionControlledPilotPlan);
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
            steps = BuildPlanSteps(
                uploadIds.Length > 0,
                knowledgeBaseIds.Length > 0,
                hasBusinessDataSourcesForPlan,
                request.TaskType,
                riskLevel,
                request.ArtifactTypes,
                isCloudSandboxTrialPlan,
                isCloudProductionPilotTrialPlan,
                isCloudProductionControlledPilotPlan);
        }

ValidateAndPersistPlan:
        var originalToolCodes = steps
            .Select(step => step.ToolCode)
            .Where(toolCode => !string.IsNullOrWhiteSpace(toolCode))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        steps = EnsureMandatorySteps(
            steps,
            uploadIds.Length > 0,
            knowledgeBaseIds.Length > 0,
            hasBusinessDataSourcesForPlan,
            request.TaskType,
            request.RequiresDataApproval,
            request.ArtifactTypes,
            isCloudSandboxTrialPlan,
            isCloudProductionPilotTrialPlan,
            isCloudProductionControlledPilotPlan);
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
                isSimulationOnlyPlan,
                businessDomains,
                isCloudSandboxTrialPlan,
                isCloudProductionPilotTrialPlan,
                isCloudProductionControlledPilotPlan,
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
            isSimulationOnlyPlan,
            businessDomains,
            isCloudSandboxTrialPlan,
            isCloudProductionPilotTrialPlan,
            isCloudProductionControlledPilotPlan,
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
        var toolRiskSummary = BuildToolRiskSummary(plannerToolCatalog);
        AgentTaskPlanCloudReadonlyIntentDocument? cloudReadonlyIntent = null;
        if (request.TaskType == AgentTaskType.CloudDataReport &&
            !isCloudSandboxTrialPlan &&
            !isCloudProductionPilotTrialPlan &&
            !isCloudProductionControlledPilotPlan)
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
            effectivePlannerMode,
            plannerFallbackReason,
            plannerModel?.Id.Value,
            PlannerValidationVersion,
            plannerToolCatalog?.Version ?? PlannerToolCatalog.CurrentVersion,
            plannerToolCatalog?.AvailableToolCount ?? 0,
            dataSourceIds,
            businessDomains,
            request.QueryMode ?? "TextToSql",
            request.RequiresDataApproval,
            NormalizeArtifactTypes(request.ArtifactTypes)?.ToArray(),
            request.TrialScenarioId,
            request.TrialScenarioTitle,
            request.IsSimulationTrial,
            isCloudSandboxControlledTrialPlan,
            request.CloudSandboxGoalIntent,
            isCloudProductionControlledPilotPlan,
            request.CloudProductionGoalIntent,
            new AgentTaskPlanSafetySummaryDocument(
                isCloudProductionControlledPilotPlan
                    ? "CloudProductionControlledGoal"
                    : isCloudProductionPilotTrialPlan
                    ? "CloudProductionPilotFixedScenario"
                    : isCloudSandboxControlledTrialPlan
                    ? "CloudSandboxControlledGoal"
                    : string.IsNullOrWhiteSpace(request.TrialScenarioId)
                        ? "FreeGoal"
                        : "TrialScenario",
                effectivePlannerMode,
                plannerModel is null ? null : $"{plannerModel.Provider}/{plannerModel.Name}",
                plannerToolCatalog?.Version ?? PlannerToolCatalog.CurrentVersion,
                plannerToolCatalog?.AvailableToolCount ?? 0,
                isSimulationOnlyPlan,
                request.RequiresDataApproval,
                toolRiskSummary,
                MockMcpOnly: !isCloudSandboxTrialPlan && !isCloudProductionPilotTrialPlan && !isCloudProductionControlledPilotPlan),
            forcedStepCodes,
            approvalCheckpoints,
            BuildPlanDataSourceSummaries(selectedDataSources),
            plannerToolCatalog?.Version ?? PlannerToolCatalog.CurrentVersion,
            plannerToolCatalog?.AvailableToolCount ?? 0,
            toolRiskSummary,
            MockMcpOnly: !isCloudSandboxTrialPlan && !isCloudProductionPilotTrialPlan && !isCloudProductionControlledPilotPlan,
            toolApprovalCheckpoints,
            isCloudProductionPilotTrialPlan);

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

    private static AgentPlannerMode NormalizePlannerMode(string? plannerMode, bool forceStaticPlanner)
    {
        if (forceStaticPlanner)
        {
            return AgentPlannerMode.StaticOnly;
        }

        if (string.IsNullOrWhiteSpace(plannerMode))
        {
            return AgentPlannerMode.Auto;
        }

        return Enum.TryParse<AgentPlannerMode>(plannerMode.Trim(), ignoreCase: true, out var mode)
            ? mode
            : AgentPlannerMode.Auto;
    }

    private static IReadOnlyCollection<AgentPlannerDataSourceSummary> BuildPlannerDataSourceSummaries(
        IReadOnlyCollection<BusinessDatabaseDescriptor> dataSources)
    {
        return dataSources
            .Select(source => new AgentPlannerDataSourceSummary(
                source.Id,
                source.Name,
                source.ExternalSystemType.ToString(),
                source.BusinessDomain,
                source.ExternalSystemType == DataSourceExternalSystemType.SimulationBusiness,
                ResolveSourceLabel(source)))
            .ToArray();
    }

    private static IReadOnlyCollection<AgentTaskPlanDataSourceSummaryDocument> BuildPlanDataSourceSummaries(
        IReadOnlyCollection<BusinessDatabaseDescriptor> dataSources)
    {
        return dataSources
            .Select(source => new AgentTaskPlanDataSourceSummaryDocument(
                source.Id,
                source.Name,
                source.ExternalSystemType.ToString(),
                source.ExternalSystemType == DataSourceExternalSystemType.SimulationBusiness,
                ResolveSourceLabel(source),
                source.BusinessDomain))
            .ToArray();
    }

    private static string ResolveSourceLabel(BusinessDatabaseDescriptor source)
    {
        return source.ExternalSystemType == DataSourceExternalSystemType.SimulationBusiness
            ? SimulationBusinessSourceLabel
            : source.ExternalSystemType.ToString();
    }

    private static IReadOnlyDictionary<string, int> BuildToolRiskSummary(PlannerToolCatalog? catalog)
    {
        return (catalog?.Tools ?? [])
            .GroupBy(tool => tool.RiskLevel, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);
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
            return "A鍔╃悊浠诲姟";
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
        bool hasBusinessDataSources,
        AgentTaskType taskType,
        bool requiresDataApproval,
        IReadOnlyCollection<string>? artifactTypes,
        bool isCloudSandboxTrial,
        bool isCloudProductionPilotTrial,
        bool isCloudProductionControlledPilotTrial)
    {
        var result = steps.ToList();
        var normalizedArtifactTypes = NormalizeArtifactTypes(artifactTypes);
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
            var cloudToolCode = isCloudProductionControlledPilotTrial
                ? CloudReadonlyProductionControlledPilotMarkers.ToolCode
                : isCloudProductionPilotTrial
                ? CloudReadonlyProductionPilotMarkers.ToolCode
                : isCloudSandboxTrial
                    ? CloudReadonlySandboxAgentTrialMarkers.ToolCode
                    : "query_cloud_data_readonly";
            var title = isCloudProductionControlledPilotTrial
                ? "Query Cloud production controlled readonly data"
                : isCloudProductionPilotTrial
                ? "Query Cloud production Pilot readonly data"
                : isCloudSandboxTrial
                    ? "Query Cloud sandbox readonly data"
                    : "Query Cloud readonly data";
            var description = isCloudProductionControlledPilotTrial
                ? "Read Cloud production data only through the controlled free-goal ProductionControlledPilot boundary."
                : isCloudProductionPilotTrial
                ? "Read Cloud production data only through the fixed-template ProductionPilot boundary."
                : isCloudSandboxTrial
                    ? "Read Cloud sandbox/staging data only through the SandboxAgentTrial boundary."
                    : "Read Cloud business data only through the AiRead readonly boundary.";
            InsertBeforeOutputs(
                result,
                cloudToolCode,
                new AgentStepPlanDto(
                    title,
                    description,
                    AgentStepType.DataQuery,
                    cloudToolCode,
                    isCloudSandboxTrial || isCloudProductionPilotTrial || isCloudProductionControlledPilotTrial));
        }

        if (hasBusinessDataSources)
        {
            InsertBeforeOutputs(
                result,
                "query_business_database_readonly",
                new AgentStepPlanDto(
                    "Query business database",
                    "Run Text-to-SQL only through authorized BusinessDatabase readonly guardrails.",
                    AgentStepType.DataQuery,
                    "query_business_database_readonly",
                    requiresDataApproval));
            InsertBeforeOutputs(
                result,
                "summarize_business_query_result",
                new AgentStepPlanDto(
                    "Summarize business query result",
                    "Summarize readonly business query output with source labels and query hashes.",
                    AgentStepType.Analysis,
                    "summarize_business_query_result",
                    false));
        }

        EnsureArtifactSteps(result, hasBusinessDataSources, normalizedArtifactTypes);

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
        bool hasBusinessDataSources,
        AgentTaskType taskType,
        AgentTaskRiskLevel riskLevel,
        IReadOnlyCollection<string>? artifactTypes,
        bool isCloudSandboxTrial,
        bool isCloudProductionPilotTrial,
        bool isCloudProductionControlledPilotTrial)
    {
        var steps = new List<AgentStepPlanDto>();
        var normalizedArtifactTypes = NormalizeArtifactTypes(artifactTypes);
        if (hasUploads)
        {
            steps.Add(new AgentStepPlanDto("Read uploaded files", "Read task uploads into the controlled workspace source area.", AgentStepType.FileRead, "read_uploaded_file", false));
            steps.Add(new AgentStepPlanDto("Parse table files", "Parse CSV, JSON, or XLSX uploads into normalized data.", AgentStepType.Analysis, "parse_table_file", false));
        }

        if (hasKnowledgeBases)
        {
            steps.Add(new AgentStepPlanDto("Search knowledge bases", "Retrieve only authorized knowledge base context for the task.", AgentStepType.RagSearch, "rag_search", false));
        }

        if (taskType == AgentTaskType.CloudDataReport)
        {
            steps.Add(isCloudProductionControlledPilotTrial
                ? new AgentStepPlanDto("Query Cloud production controlled readonly data", "Read Cloud production data only through the controlled free-goal ProductionControlledPilot boundary.", AgentStepType.DataQuery, CloudReadonlyProductionControlledPilotMarkers.ToolCode, true)
                : isCloudProductionPilotTrial
                ? new AgentStepPlanDto("Query Cloud production Pilot readonly data", "Read Cloud production data only through the fixed-template ProductionPilot boundary.", AgentStepType.DataQuery, CloudReadonlyProductionPilotMarkers.ToolCode, true)
                : isCloudSandboxTrial
                    ? new AgentStepPlanDto("Query Cloud sandbox readonly data", "Read Cloud sandbox/staging data only through the SandboxAgentTrial boundary.", AgentStepType.DataQuery, CloudReadonlySandboxAgentTrialMarkers.ToolCode, true)
                    : new AgentStepPlanDto("Query Cloud readonly data", "Read Cloud business data only through the AiRead readonly boundary.", AgentStepType.DataQuery, "query_cloud_data_readonly", false));
        }

        steps.Add(new AgentStepPlanDto("Generate chart data", "Generate frontend chart preview data from controlled task inputs.", AgentStepType.ChartGeneration, "generate_chart_data", false));
        steps.Add(new AgentStepPlanDto("Generate Markdown report", "Generate a Markdown draft in the controlled workspace.", AgentStepType.ArtifactGeneration, "generate_markdown_report", false));
        steps.Add(new AgentStepPlanDto("Generate HTML report", "Generate an HTML draft in the controlled workspace.", AgentStepType.ArtifactGeneration, "generate_html_report", false));
        steps.Add(new AgentStepPlanDto("Generate PDF draft", "Generate a basic PDF report draft in the controlled workspace.", AgentStepType.ArtifactGeneration, "generate_pdf", true));
        steps.Add(new AgentStepPlanDto("Generate PPTX draft", "Generate a basic PPTX presentation draft in the controlled workspace.", AgentStepType.ArtifactGeneration, "generate_pptx", true));
        steps.Add(new AgentStepPlanDto("Generate XLSX draft", "Generate a basic XLSX data draft in the controlled workspace.", AgentStepType.ArtifactGeneration, "generate_xlsx", true));
        steps.Add(new AgentStepPlanDto("Confirm final output", "Wait for user approval before moving draft artifacts to final output.", AgentStepType.Finalize, "finalize_artifacts", true));

        if (normalizedArtifactTypes is not null)
        {
            steps = steps
                .Where(step => ShouldKeepStepForArtifacts(step, normalizedArtifactTypes))
                .ToList();
        }

        return riskLevel >= AgentTaskRiskLevel.High
            ? steps.Select(step => step with { RequiresApproval = true }).ToArray()
            : steps;
    }
    private static bool ShouldKeepStepForArtifacts(
        AgentStepPlanDto step,
        IReadOnlySet<string> artifactTypes)
    {
        return step.ToolCode switch
        {
            "generate_chart_data" or "generate_business_chart" => artifactTypes.Contains("chart"),
            "generate_markdown_report" => artifactTypes.Contains("markdown"),
            "generate_html_report" => artifactTypes.Contains("html"),
            "generate_pdf" => artifactTypes.Contains("pdf"),
            "generate_pptx" => artifactTypes.Contains("pptx"),
            "generate_xlsx" => artifactTypes.Contains("xlsx"),
            _ => true
        };
    }

    private static void EnsureArtifactSteps(
        List<AgentStepPlanDto> steps,
        bool hasBusinessDataSources,
        IReadOnlySet<string>? artifactTypes)
    {
        var hasPlannedOutputs = steps.Any(step =>
            step.StepType is AgentStepType.ChartGeneration or AgentStepType.ArtifactGeneration);
        if (artifactTypes is null && hasPlannedOutputs)
        {
            artifactTypes = hasBusinessDataSources
                ? new HashSet<string>(["chart"], StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }
        else if (artifactTypes is null)
        {
            artifactTypes = hasBusinessDataSources
                ? new HashSet<string>(["chart", "markdown"], StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        if (hasBusinessDataSources)
        {
            steps.RemoveAll(step => string.Equals(step.ToolCode, "generate_chart_data", StringComparison.OrdinalIgnoreCase));
        }

        if (ShouldIncludeArtifact(artifactTypes, "chart"))
        {
            var toolCode = hasBusinessDataSources ? "generate_business_chart" : "generate_chart_data";
            InsertBeforeOutputs(
                steps,
                toolCode,
                new AgentStepPlanDto(
                    "Generate chart data",
                    hasBusinessDataSources
                        ? "Generate controlled chart data from approved BusinessDatabase readonly query results."
                        : "Generate chart preview data from controlled task inputs.",
                    AgentStepType.ChartGeneration,
                    toolCode,
                    false));
        }

        if (ShouldIncludeArtifact(artifactTypes, "markdown"))
        {
            InsertBeforeOutputs(
                steps,
                "generate_markdown_report",
                new AgentStepPlanDto(
                    "Generate Markdown report",
                    "Generate a Markdown draft in the controlled workspace.",
                    AgentStepType.ArtifactGeneration,
                    "generate_markdown_report",
                    false));
        }

        if (ShouldIncludeArtifact(artifactTypes, "html"))
        {
            InsertBeforeOutputs(
                steps,
                "generate_html_report",
                new AgentStepPlanDto(
                    "Generate HTML report",
                    "Generate an HTML draft in the controlled workspace.",
                    AgentStepType.ArtifactGeneration,
                    "generate_html_report",
                    false));
        }

        if (ShouldIncludeArtifact(artifactTypes, "pdf"))
        {
            InsertBeforeOutputs(
                steps,
                "generate_pdf",
                new AgentStepPlanDto(
                    "Generate PDF draft",
                    "Generate a PDF draft in the controlled workspace.",
                    AgentStepType.ArtifactGeneration,
                    "generate_pdf",
                    true));
        }

        if (ShouldIncludeArtifact(artifactTypes, "pptx"))
        {
            InsertBeforeOutputs(
                steps,
                "generate_pptx",
                new AgentStepPlanDto(
                    "Generate PPTX draft",
                    "Generate a PPTX draft in the controlled workspace.",
                    AgentStepType.ArtifactGeneration,
                    "generate_pptx",
                    true));
        }

        if (ShouldIncludeArtifact(artifactTypes, "xlsx"))
        {
            InsertBeforeOutputs(
                steps,
                "generate_xlsx",
                new AgentStepPlanDto(
                    "Generate XLSX draft",
                    "Generate an XLSX draft in the controlled workspace.",
                    AgentStepType.ArtifactGeneration,
                    "generate_xlsx",
                    true));
        }
    }

    private static bool ShouldIncludeArtifact(IReadOnlySet<string>? artifactTypes, string artifactType)
    {
        return artifactTypes is null || artifactTypes.Contains(artifactType);
    }

    private static IReadOnlySet<string>? NormalizeArtifactTypes(IReadOnlyCollection<string>? artifactTypes)
    {
        if (artifactTypes is null || artifactTypes.Count == 0)
        {
            return null;
        }

        var normalized = artifactTypes
            .Select(NormalizeArtifactType)
            .Where(item => item is not null)
            .Select(item => item!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return normalized.Count == 0 ? null : normalized;
    }

    private static string? NormalizeArtifactType(string? artifactType)
    {
        var value = artifactType?.Trim().ToLowerInvariant();
        return value switch
        {
            "chart" or "chartdata" or "chart-data" or "json" => "chart",
            "markdown" or "md" => "markdown",
            "html" => "html",
            "pdf" => "pdf",
            "ppt" or "pptx" or "presentation" => "pptx",
            "xls" or "xlsx" or "excel" or "spreadsheet" => "xlsx",
            _ => null
        };
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
    ICurrentUser currentUser,
    IOptions<AgentRunQueueOptions>? options = null,
    AgentAuditRecorder? auditRecorder = null)
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

        var queueItems = await queueReadRepository.ListAsync(
            new AgentTaskRunQueueItemsByTaskSpec(task.Id),
            cancellationToken);
        var previousRetryCount = queueItems.Count(item => item.TriggerType == AgentTaskRunTriggerType.Retry);
        var runQueueOptions = options?.Value ?? new AgentRunQueueOptions();
        if (previousRetryCount >= runQueueOptions.EffectiveMaxRetryAttempts)
        {
            return Result.Failure(new ApiProblemDescriptor(
                AppProblemCodes.AgentTaskRetryNotAllowed,
                $"Agent task retry limit exceeded. Maximum retry attempts: {runQueueOptions.EffectiveMaxRetryAttempts}."));
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
        var retryAttemptNo = previousRetryCount + 1;
        var availableAt = now.Add(runQueueOptions.GetRetryBackoff(retryAttemptNo));
        await CancelPendingApprovalsAsync(task, approvalRepository, now, cancellationToken);
        task.PrepareRetry(now);
        repository.Update(task);
        await repository.SaveChangesAsync(cancellationToken);

        var queued = await runQueue.EnqueueAsync(
            task,
            AgentTaskRunTriggerType.Retry,
            currentUser.Id!.Value,
            cancellationToken,
            availableAt);
        if (!queued.IsSuccess)
        {
            return Result.From(queued);
        }

        if (auditRecorder is not null)
        {
            await auditRecorder.RecordRunQueueOperationAsync(
                "Agent.RunQueueRetry",
                queued.Value!,
                AuditResults.Succeeded,
                "Agent task retry queued with backoff.",
                AgentTaskStatus.Failed.ToString(),
                attempt: null,
                retryAttemptNo,
                cancellationToken);
        }

        return Result.Success(await AgentTaskDtoComposer.MapAsync(
            task,
            workspaceRepository,
            approvalRepository,
            queueReadRepository,
            cancellationToken));
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
    ICurrentUser currentUser,
    AgentAuditRecorder? auditRecorder = null)
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
        if (IsTerminal(task.Status))
        {
            return Result.Success(await AgentTaskDtoComposer.MapAsync(
                task,
                workspaceRepository,
                approvalRepository,
                queueReadRepository,
                cancellationToken));
        }

        var now = DateTimeOffset.UtcNow;
        var activeBeforeCancel = await queueReadRepository.ListAsync(
            new ActiveAgentTaskRunQueueItemByTaskSpec(task.Id),
            cancellationToken);
        var oldStatuses = activeBeforeCancel.ToDictionary(
            item => item.Id,
            item => item.Status.ToString());
        var cancelledItems = await runQueue.CancelActiveAsync(
            task,
            now,
            "Agent task cancellation requested.",
            cancellationToken);
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
        if (auditRecorder is not null)
        {
            foreach (var item in cancelledItems)
            {
                await auditRecorder.RecordRunQueueOperationAsync(
                    "Agent.RunQueueCancel",
                    item,
                    AuditResults.Succeeded,
                    "Agent task run queue item cancelled.",
                    oldStatuses.GetValueOrDefault(item.Id, AgentTaskRunQueueStatus.Queued.ToString()),
                    attempt: null,
                    retryAttemptNo: null,
                    cancellationToken);
            }
        }

        return Result.Success(await AgentTaskDtoComposer.MapAsync(
            task,
            workspaceRepository,
            approvalRepository,
            queueReadRepository,
            cancellationToken));
    }

    private static bool IsTerminal(AgentTaskStatus status)
    {
        return status is AgentTaskStatus.Completed
            or AgentTaskStatus.Finalized
            or AgentTaskStatus.Failed
            or AgentTaskStatus.Rejected
            or AgentTaskStatus.Cancelled;
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
