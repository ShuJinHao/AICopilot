using System.Text.Json;
using AICopilot.AiGatewayService.Agents;
using AICopilot.AiGatewayService.Models;
using AICopilot.AiGatewayService.Sessions;
using AICopilot.AiGatewayService.Tools;
using AICopilot.AiGatewayService.Workflows;
using AICopilot.Core.AiGateway.Aggregates.AgentTasks;
using AICopilot.Core.AiGateway.Aggregates.Sessions;
using AICopilot.Core.AiGateway.Aggregates.Skills;
using AICopilot.Core.AiGateway.Aggregates.Uploads;
using AICopilot.Core.AiGateway.Ids;
using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Ai;
using AICopilot.SharedKernel.Repository;
using AICopilot.SharedKernel.Result;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace AICopilot.AiGatewayService.AgentTasks;

public sealed class PlanAgentTaskCoordinator(
    IRepository<AgentTask> repository,
    IReadRepository<Session> sessionRepository,
    IReadRepository<UploadRecord> uploadRepository,
    AgentAuditRecorder auditRecorder,
    IEnumerable<IKnowledgeBaseAccessChecker> knowledgeBaseAccessCheckers,
    ICurrentUser currentUser,
    IBusinessDatabaseReadService? businessDatabaseReadService = null,
    MessageTimelineProjectionWriter? timelineProjectionWriter = null,
    AgentWorkflowPipeline? workflowPipeline = null,
    AgentPlanToolGuard? planToolGuard = null,
    ICloudReadonlyAgentPlanService? cloudReadonlyPlanService = null,
    AgentPlanDraftContractAuthority? planDraftContractAuthority = null,
    IOptions<CloudReadonlyOptions>? cloudReadonlyOptions = null,
    IHostEnvironment? hostEnvironment = null,
    IBusinessDatabaseAuthorizationReadService? businessDatabaseAuthorizationReadService = null)
{
    private const int PlanDraftValidationVersion = 1;

    public async Task<Result<AgentTaskDto>> PlanAsync(
        PlanAgentTaskCommand request,
        CancellationToken cancellationToken)
    {
        var retiredSelection = AgentPlanRetiredSelectionContract.Validate(
            request.SkillCode,
            request.PreferredToolCodes);
        if (retiredSelection is not null)
        {
            return Result.Failure(retiredSelection);
        }

        if (currentUser.Id is not { } userId)
        {
            return MissingUser();
        }

        var useDevelopmentSimulationProfile = IsDevelopmentSimulationProfile();
        if (useDevelopmentSimulationProfile &&
            (request.TaskType == AgentTaskType.CloudDataReport ||
             request.QueryMode is not null &&
             !string.Equals(request.QueryMode, "TextToSql", StringComparison.Ordinal)))
        {
            return Result.Invalid(
                "The Development Simulation profile accepts the governed TextToSql report chain only; CloudReadonly never falls back to Simulation.");
        }

        var preparationService = new AgentTaskPlanPreparationService(
            sessionRepository,
            uploadRepository,
            knowledgeBaseAccessCheckers,
            businessDatabaseReadService,
            businessDatabaseAuthorizationReadService);
        var preparationResult = await preparationService.PrepareAsync(
            request,
            userId,
            IsAdmin(),
            cancellationToken,
            useDevelopmentSimulationProfile);
        if (!preparationResult.IsSuccess)
        {
            return Result.From(preparationResult);
        }

        var preparation = preparationResult.Value!;
        var capabilityGaps = new List<string>();
        AgentPlanDraftWorkflowResult? workflowDraft = null;
        if (workflowPipeline is not null)
        {
            var workflowRequest = new ChatStreamRequest(request.SessionId, request.Goal);
            workflowDraft = useDevelopmentSimulationProfile
                ? await workflowPipeline.RunPlanDraftRoutingOnlyAsync(workflowRequest, cancellationToken)
                : await workflowPipeline.RunPlanDraftWorkflowAsync(workflowRequest, cancellationToken);
        }

        var effectiveTaskType = request.TaskType;
        var effectiveQueryMode = request.QueryMode ??
            (effectiveTaskType == AgentTaskType.CloudDataReport ? "CloudReadonly" : "TextToSql");
        var effectivePlanSource = "PlanV2Contract";
        var effectivePlannerModelId = useDevelopmentSimulationProfile
            ? workflowDraft?.ExecutionMetadata.RoutingConfiguration?.ModelId ?? request.ModelId
            : request.ModelId;

        var riskLevel = AgentTaskPlanMetadataBuilder.DetermineRiskLevel(effectiveTaskType);
        var requestedArtifactTypes = request.ArtifactTypes ??
            (useDevelopmentSimulationProfile ? ["markdown"] : null);
        var artifactTypesResult = AgentTaskPlanStepBuilder.ResolveArtifactTypes(requestedArtifactTypes);
        if (!artifactTypesResult.IsSuccess)
        {
            return Result.From(artifactTypesResult);
        }

        var effectiveArtifactTypes = artifactTypesResult.Value!
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
        var effectivePlannerMode = "PlanDraft";
        string? plannerFallbackReason = null;
        var plannerToolCatalog = new PlannerToolCatalog(PlannerToolCatalog.CurrentVersion, 0, []);
        if (planToolGuard is not null)
        {
            var catalogResult = await planToolGuard.GetAvailableToolCatalogAsync(
                userId,
                preparation.IsSimulationOnlyPlan,
                preparation.BusinessDomains,
                cancellationToken,
                skillCode: null,
                pluginSelectionMode: request.PluginSelectionMode ?? AgentPluginSelectionMode.BuiltInOnly);
            if (catalogResult.IsSuccess)
            {
                plannerToolCatalog = catalogResult.Value!;
            }
            else
            {
                capabilityGaps.Add(AgentPlanCapabilityGapCodes.ToolCatalogUnavailable);
                plannerToolCatalog = new PlannerToolCatalog(PlannerToolCatalog.CurrentVersion, 0, []);
            }
        }
        else
        {
            // The registry is the only authority for executable tool identity and
            // schema. Runtime/plugin discovery cannot synthesize a parallel catalog.
            capabilityGaps.Add(AgentPlanCapabilityGapCodes.ToolCatalogUnavailable);
        }

        var plannerToolCatalogVersion = plannerToolCatalog.Version;
        var plannerToolCount = plannerToolCatalog.AvailableToolCount;
        var toolRiskSummary = AgentTaskPlanMetadataBuilder.BuildToolRiskSummary(plannerToolCatalog);
        var mockMcpOnly = PlannerToolCatalogMetadata.IsMockMcpOnly(plannerToolCatalog.Tools);
        var steps = AgentTaskPlanStepBuilder.BuildPlanSteps(
            preparation.UploadIds.Length > 0,
            preparation.KnowledgeBaseIds.Length > 0,
            preparation.HasBusinessDataSourcesForPlan,
            effectiveTaskType,
            riskLevel,
            effectiveArtifactTypes);

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

        steps = riskLevel >= AgentTaskRiskLevel.High
            ? steps.Select(step => step with { RequiresApproval = true }).ToArray()
            : steps;
        if (planToolGuard is not null && steps.Count != 0)
        {
            var guardedStepsResult = await planToolGuard.ValidateStepsAsync(
                steps,
                effectiveTaskType,
                userId,
                preparation.IsSimulationOnlyPlan,
                preparation.BusinessDomains,
                cancellationToken,
                skillCode: null,
                pluginSelectionMode: request.PluginSelectionMode ?? AgentPluginSelectionMode.BuiltInOnly);
            if (guardedStepsResult.IsSuccess)
            {
                steps = guardedStepsResult.Value!;
            }
            else
            {
                capabilityGaps.Add(AgentPlanCapabilityGapCodes.PlannedToolUnavailable);
            }
        }

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
        AgentTaskPlanCloudReadonlyIntentDocument? cloudReadonlyIntent = null;
        CloudReadonlyAgentPlanIntent? resolvedCloudIntent = null;
        if (effectiveTaskType == AgentTaskType.CloudDataReport)
        {
            if (cloudReadonlyPlanService is null)
            {
                capabilityGaps.Add(AgentPlanCapabilityGapCodes.CloudReadonlyResolverUnavailable);
            }
            else
            {
                Result<CloudReadonlyAgentPlanIntent> cloudIntentResult = workflowDraft is null
                    ? Result.Failure(new ApiProblemDescriptor(
                        AppProblemCodes.CloudReadonlyIntentUnsupported,
                        "The single authoritative routing result is unavailable."))
                    : cloudReadonlyPlanService.CreateIntentFromRouted(
                    request.Goal,
                    workflowDraft.Intents);
                if (cloudIntentResult.IsSuccess)
                {
                    resolvedCloudIntent = cloudIntentResult.Value!;
                    cloudReadonlyIntent = AgentTaskPlanCloudReadonlyIntentDocument.From(resolvedCloudIntent);
                }
                else
                {
                    capabilityGaps.Add(AgentPlanCapabilityGapCodes.CloudReadonlyIntentUnavailable);
                }
            }
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
            new AgentTaskPlanRuntimeSettingsDocument(0, 0),
            effectivePlannerMode,
            plannerFallbackReason,
            effectivePlannerModelId,
            PlanDraftValidationVersion,
            plannerToolCatalogVersion,
            plannerToolCount,
            preparation.DataSourceIds,
            preparation.BusinessDomains,
            effectiveQueryMode,
            request.RequiresDataApproval,
            effectiveArtifactTypes,
            new AgentTaskPlanSafetySummaryDocument(
                effectivePlanSource,
                effectivePlannerMode,
                PlannerModelSummary: null,
                plannerToolCatalogVersion,
                plannerToolCount,
                preparation.IsSimulationOnlyPlan,
                request.RequiresDataApproval,
                toolRiskSummary,
                mockMcpOnly),
            forcedStepCodes,
            approvalCheckpoints,
            AgentTaskPlanMetadataBuilder.BuildPlanDataSourceSummaries(preparation.SelectedDataSources),
            plannerToolCatalogVersion,
            plannerToolCount,
            toolRiskSummary,
            mockMcpOnly,
            toolApprovalCheckpoints,
            SkillCode: null,
            SkillName: null,
            SkillRoutingReason: null,
            PlanKind: AgentTaskPlanKinds.PlanDraft,
            IsExecutable: false,
            CapabilityGaps: capabilityGaps
                .Where(gap => !string.IsNullOrWhiteSpace(gap))
                .Distinct(StringComparer.Ordinal)
                .ToArray());

        var routedIntents = new List<IntentResult>();
        if (useDevelopmentSimulationProfile && preparation.IsSimulationOnlyPlan)
        {
            // The explicit local profile has one fixed, read-only capability envelope.
            // Dynamic routing still produced the frozen configuration snapshot above,
            // but cannot expand this plan into Cloud, MCP, plugin, or control intents.
            routedIntents.Add(new IntentResult { Intent = "General.Chat", Confidence = 1 });
            routedIntents.Add(new IntentResult { Intent = "Analysis.GovernedQuery", Confidence = 1 });
        }
        else if (resolvedCloudIntent is not null)
        {
            routedIntents.Add(new IntentResult
            {
                Intent = resolvedCloudIntent.Intent,
                Query = BuildTypedIntentAdapterQuery(resolvedCloudIntent.SemanticPlan),
                Confidence = resolvedCloudIntent.Confidence
            });
        }
        else if (workflowDraft is not null)
        {
            routedIntents.AddRange(workflowDraft.Intents.Where(intent =>
                !intent.Intent.StartsWith("Knowledge.", StringComparison.Ordinal) &&
                !preparation.SelectedDataSources.Any(source =>
                    string.Equals(intent.Intent, $"Analysis.{source.Name}", StringComparison.OrdinalIgnoreCase))));
        }

        if (preparation.KnowledgeBaseIds.Length > 0)
        {
            routedIntents.Add(new IntentResult { Intent = "Knowledge.Retrieve", Confidence = 1 });
        }

        if (preparation.SelectedDataSources.Any() &&
            !routedIntents.Any(intent => string.Equals(
                intent.Intent,
                "Analysis.GovernedQuery",
                StringComparison.Ordinal)))
        {
            routedIntents.Add(new IntentResult { Intent = "Analysis.GovernedQuery", Confidence = 1 });
        }

        if ((request.CapabilitySelectionMode ?? AgentCapabilitySelectionMode.InferredFromGoal) ==
                AgentCapabilitySelectionMode.InferredFromGoal &&
            !routedIntents.Any(intent => string.Equals(
                intent.Intent,
                "General.Chat",
                StringComparison.Ordinal)))
        {
            // Linear deterministic report/file/artifact nodes need a non-data
            // synthesis capability. This is a server-owned derived dependency for
            // inferred planning; an explicit allowlist remains a hard upper bound
            // and is never silently expanded here.
            routedIntents.Add(new IntentResult { Intent = "General.Chat", Confidence = 1 });
        }
        var contractAuthority = planDraftContractAuthority ?? new AgentPlanDraftContractAuthority(
            new IntentResultToCandidateAdapter(),
            new AgentPlanCanonicalizer());
        var sealedPlanResult = contractAuthority.SealDraft(new AgentPlanDraftContractRequest(
            request.Goal,
            plan,
            routedIntents,
            new AgentIntentAdapterContext(
                preparation.UploadIds,
                preparation.KnowledgeBaseIds,
                preparation.SelectedDataSources,
                effectiveArtifactTypes ?? [],
                ResolveRoutedSkillCodes(workflowDraft),
                ResolveAuthorizedActionIntentCodes(workflowDraft),
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)),
            plannerToolCatalog,
            request.PluginSelectionMode,
            request.SelectedPluginIds,
            request.CapabilitySelectionMode,
            request.RequestedCapabilityCodes,
            workflowDraft?.ExecutionMetadata.RoutingConfiguration,
            AllowDevelopmentSimulationExecution: useDevelopmentSimulationProfile));
        if (!sealedPlanResult.IsSuccess)
        {
            return Result.From(sealedPlanResult);
        }

        var sealedPlan = sealedPlanResult.Value!;

        var now = DateTimeOffset.UtcNow;
        var task = new AgentTask(
            new SessionId(request.SessionId),
            userId,
            AgentTaskPlanMetadataBuilder.BuildTitle(request.Goal),
            request.Goal,
            effectiveTaskType,
            riskLevel,
            effectivePlannerModelId.HasValue ? new(effectivePlannerModelId.Value) : null,
            sealedPlan.CanonicalJson,
            now);

        foreach (var step in sealedPlan.Document.Steps)
        {
            task.AddStep(step.Title, step.Description, step.StepType, step.ToolCode, step.RequiresApproval, now, step.InputJson);
        }

        repository.Add(task);
        if (timelineProjectionWriter is not null)
        {
            await timelineProjectionWriter.StageAgentTaskPlanCreatedAsync(task, approval: null, cancellationToken);
        }

        await auditRecorder.RecordPlanAsync(
            task,
            AuditResults.Succeeded,
            useDevelopmentSimulationProfile && preparation.IsSimulationOnlyPlan
                ? "Development Simulation PlanDraft generated with an explicit read-only execution graph; no tool, Cloud, MCP, or Worker execution occurred before confirmation."
                : "Agent task PlanDraft contract generated; execution remains blocked until the trusted P2 PlanCompiler is available.",
            pendingApprovalCount: 0,
            cancellationToken);
        await repository.SaveChangesAsync(cancellationToken);
        return Result.Success(AgentTaskDtoMapper.Map(task, pendingApprovalCount: 0));
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

    private bool IsDevelopmentSimulationProfile()
    {
        var options = cloudReadonlyOptions?.Value;
        return hostEnvironment?.IsDevelopment() == true &&
               options is not null &&
               options.Mode == CloudReadonlyDataSourceMode.Simulation &&
               options.Simulation.Enabled &&
               options.Simulation.AlwaysMarkAsSimulation;
    }

    private static string[] ResolveAuthorizedActionIntentCodes(AgentPlanDraftWorkflowResult? workflowDraft)
    {
        if (workflowDraft is null || workflowDraft.Tools.Count == 0)
        {
            return [];
        }

        var loadedTargets = workflowDraft.Tools
            .Select(tool => tool.TargetName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return workflowDraft.Intents
            .Select(intent => intent.Intent?.Trim())
            .Where(code => code is not null && code.StartsWith("Action.", StringComparison.Ordinal))
            .Select(code => code!)
            .Where(code => loadedTargets.Contains(code["Action.".Length..]))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(code => code, StringComparer.Ordinal)
            .ToArray();
    }

    private static string[] ResolveRoutedSkillCodes(AgentPlanDraftWorkflowResult? workflowDraft)
    {
        return workflowDraft?.Intents
            .Select(intent => !string.IsNullOrWhiteSpace(intent.SkillCode)
                ? intent.SkillCode!.Trim()
                : intent.Intent.StartsWith("Skill.", StringComparison.Ordinal)
                    ? intent.Intent["Skill.".Length..].Trim()
                    : null)
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .Select(code => code!)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(code => code, StringComparer.Ordinal)
            .ToArray() ?? [];
    }

    private static string BuildTypedIntentAdapterQuery(SemanticQueryPlan plan)
    {
        var payload = new
        {
            filters = plan.Filters.Select(filter => new
            {
                field = filter.Field,
                @operator = filter.Operator switch
                {
                    SemanticFilterOperator.Contains => "contains",
                    SemanticFilterOperator.GreaterOrEqual => "gte",
                    SemanticFilterOperator.LessOrEqual => "lte",
                    SemanticFilterOperator.In => "in",
                    _ => "eq"
                },
                value = filter.Value
            }).ToArray(),
            timeRange = plan.TimeRange is null
                ? null
                : new
                {
                    fromUtc = plan.TimeRange.Start,
                    toUtc = plan.TimeRange.End,
                    timeZone = plan.TimeRange.TimeZone
                }
        };
        return JsonSerializer.Serialize(payload, CanonicalJson.SerializerOptions);
    }

}
