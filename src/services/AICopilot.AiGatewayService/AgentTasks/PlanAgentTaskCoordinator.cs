using System.Text.Json;
using AICopilot.AiGatewayService.Agents;
using AICopilot.AiGatewayService.Models;
using AICopilot.AiGatewayService.Sessions;
using AICopilot.AiGatewayService.Tools;
using AICopilot.AiGatewayService.Workflows;
using AICopilot.Core.AiGateway.Aggregates.AgentTasks;
using AICopilot.Core.AiGateway.Ids;
using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Ai;
using AICopilot.SharedKernel.Repository;
using AICopilot.SharedKernel.Result;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace AICopilot.AiGatewayService.AgentTasks;

internal sealed class PlanAgentTaskCoordinator(
    IRepository<AgentTask> repository,
    AgentTaskPlanPreparationService preparationService,
    AgentAuditRecorder auditRecorder,
    ICurrentUser currentUser,
    MessageTimelineProjectionWriter? timelineProjectionWriter = null, AgentWorkflowPipeline? workflowPipeline = null,
    AgentPlanToolGuard? planToolGuard = null, ICloudReadonlyAgentPlanService? cloudReadonlyPlanService = null,
    AgentPlanDraftContractAuthority? planDraftContractAuthority = null, IOptions<CloudReadonlyOptions>? cloudReadonlyOptions = null,
    IHostEnvironment? hostEnvironment = null, ConfiguredAgentRuntimeFactory? configuredAgentRuntimeFactory = null)
{
    private const int PlanDraftValidationVersion = 1;

    public async Task<Result<AgentTaskDto>> PlanAsync(
        PlanAgentTaskCommand request,
        CancellationToken cancellationToken)
    {
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
        var requestedArtifactTargets = request.ArtifactTargets ??
            (useDevelopmentSimulationProfile ? ["markdown"] : null);
        var artifactTargetsResult = AgentArtifactTargetAuthority.Resolve(requestedArtifactTargets);
        if (!artifactTargetsResult.IsSuccess)
        {
            return Result.From(artifactTargetsResult);
        }

        var effectiveArtifactTargets = artifactTargetsResult.Value!
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
        var plannerToolCatalog = new PlannerToolCatalog(PlannerToolCatalog.CurrentVersion, 0, []);
        if (planToolGuard is not null)
        {
            var catalogResult = await planToolGuard.GetAvailableToolCatalogAsync(
                userId,
                preparation.IsSimulationOnlyPlan,
                preparation.BusinessDomains,
                cancellationToken,
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
        IReadOnlyCollection<AgentTaskPlanCloudReadonlyIntentDocument> cloudReadonlyIntents = [];
        IReadOnlyCollection<CloudReadonlyAgentPlanIntent> resolvedCloudIntents = [];
        if (effectiveTaskType == AgentTaskType.CloudDataReport)
        {
            if (cloudReadonlyPlanService is null)
            {
                capabilityGaps.Add(AgentPlanCapabilityGapCodes.CloudReadonlyResolverUnavailable);
            }
            else
            {
                Result<IReadOnlyCollection<CloudReadonlyAgentPlanIntent>> cloudIntentResult = workflowDraft is null
                    ? Result.Failure(new ApiProblemDescriptor(
                        AppProblemCodes.CloudReadonlyIntentUnsupported,
                        "The authoritative routing result is unavailable."))
                    : cloudReadonlyPlanService.CreateIntentsFromRouted(
                    request.Goal,
                    workflowDraft.Intents);
                if (cloudIntentResult.IsSuccess)
                {
                    resolvedCloudIntents = cloudIntentResult.Value!
                        .OrderBy(intent => intent.Intent, StringComparer.Ordinal)
                        .ToArray();
                    cloudReadonlyIntents = resolvedCloudIntents
                        .Select(AgentTaskPlanCloudReadonlyIntentDocument.From)
                        .ToArray();
                }
                else
                {
                    capabilityGaps.Add(AgentPlanCapabilityGapCodes.CloudReadonlyIntentUnavailable);
                }
            }
        }

        var plan = new AgentTaskPlanDocument(
            Version: 1,
            PlannerTemplateCode: "agent_planner",
            Goal: request.Goal,
            TaskType: effectiveTaskType.ToString(),
            RiskLevel: riskLevel.ToString(),
            UploadIds: preparation.UploadIds,
            KnowledgeBaseIds: preparation.KnowledgeBaseIds,
            CloudReadonlyIntents: cloudReadonlyIntents,
            Steps: [],
            RuntimeSettings: new AgentTaskPlanRuntimeSettingsDocument(0, 0),
            PlannerModelId: effectivePlannerModelId,
            PlannerValidationVersion: PlanDraftValidationVersion,
            PlannerToolCatalogVersion: plannerToolCatalogVersion,
            PlannerAvailableToolCount: plannerToolCount,
            DataSourceIds: preparation.DataSourceIds,
            BusinessDomains: preparation.BusinessDomains,
            QueryMode: effectiveQueryMode,
            RequiresDataApproval: request.RequiresDataApproval,
            PlannerSafetySummary: new AgentTaskPlanSafetySummaryDocument(
                effectivePlanSource,
                plannerToolCatalogVersion,
                plannerToolCount,
                preparation.IsSimulationOnlyPlan,
                request.RequiresDataApproval,
                toolRiskSummary,
                mockMcpOnly),
            ForcedStepCodes: [],
            ApprovalCheckpoints: [],
            DataSourceSummaries: AgentTaskPlanMetadataBuilder.BuildPlanDataSourceSummaries(preparation.SelectedDataSources),
            ToolCatalogVersion: plannerToolCatalogVersion,
            VisibleToolCount: plannerToolCount,
            ToolRiskSummary: toolRiskSummary,
            MockMcpOnly: mockMcpOnly,
            ToolApprovalCheckpoints: [],
            PlanKind: AgentTaskPlanKinds.PlanDraft,
            IsExecutable: false,
            CapabilityGaps: capabilityGaps
                .Where(gap => !string.IsNullOrWhiteSpace(gap))
                .Distinct(StringComparer.Ordinal)
                .ToArray(),
            ArtifactTargets: effectiveArtifactTargets);

        var routedIntents = new List<IntentResult>();
        if (useDevelopmentSimulationProfile && preparation.IsSimulationOnlyPlan)
        {
            // The explicit local profile has one fixed, read-only capability envelope.
            // Dynamic routing still produced the frozen configuration snapshot above,
            // but cannot expand this plan into Cloud, MCP, plugin, or control intents.
            routedIntents.Add(new IntentResult { Intent = "General.Chat", Confidence = 1 });
            routedIntents.Add(new IntentResult { Intent = "Analysis.GovernedQuery", Confidence = 1 });
        }
        else if (resolvedCloudIntents.Count != 0)
        {
            routedIntents.AddRange(resolvedCloudIntents.Select(resolvedCloudIntent => new IntentResult
            {
                Intent = resolvedCloudIntent.Intent,
                Query = BuildTypedIntentRegistryQuery(resolvedCloudIntent.SemanticPlan),
                Confidence = resolvedCloudIntent.Confidence
            }));
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

        var addedDerivedGeneralIntent = false;
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
            addedDerivedGeneralIntent = true;
        }
        var contractAuthority = planDraftContractAuthority ?? new AgentPlanDraftContractAuthority(
            new AgentIntentRegistryProjector(),
            new AgentPlanCanonicalizer());
        RuntimeAgentConfigurationSnapshot? reasoningConfiguration = null;
        if (configuredAgentRuntimeFactory is not null)
        {
            try
            {
                reasoningConfiguration = await configuredAgentRuntimeFactory.ReadConfigurationSnapshotAsync(
                    AgentReasoningPolicyAuthority.TemplateCode,
                    modelOverride: null,
                    configureOptions: AgentReasoningPolicyAuthority.ConfigureOptions,
                    cancellationToken: cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception)
            {
                // The compiler only requires this snapshot when the deterministic
                // skeleton contains an AgentReasoningNode; that path fails closed.
            }
        }

        var sealedPlanResult = contractAuthority.SealDraft(new AgentPlanDraftContractRequest(
            request.Goal,
            plan,
            routedIntents,
            new AgentIntentRegistryContext(
                preparation.UploadIds,
                preparation.KnowledgeBaseIds,
                preparation.SelectedDataSources,
                effectiveArtifactTargets,
                ResolveAuthorizedActionIntentCodes(workflowDraft),
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                addedDerivedGeneralIntent ? ["General.Chat"] : [],
                workflowDraft?.RegistrySnapshot),
            plannerToolCatalog,
            request.PluginSelectionMode,
            request.SelectedPluginIds,
            request.CapabilitySelectionMode,
            request.RequestedCapabilityCodes,
            workflowDraft?.ExecutionMetadata.RoutingConfiguration,
            reasoningConfiguration,
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
                : "Agent task PlanDraft generated by the authoritative PlanCompiler; no Tool, Cloud, MCP, or Worker execution occurred before confirmation.",
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

    private static string BuildTypedIntentRegistryQuery(SemanticQueryPlan plan)
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
