using System.Text.Json;
using AICopilot.AiGatewayService.Models;
using AICopilot.Core.AiGateway.Aggregates.AgentTasks;
using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Result;

namespace AICopilot.AiGatewayService.AgentTasks;

internal sealed record AgentPlanV2CompilationRequest(
    string RawGoal,
    AgentTaskPlanDocument LegacyPlan,
    IReadOnlyCollection<AgentStepPlanDto> Steps,
    IReadOnlyCollection<IntentResult> RoutedIntents,
    AgentIntentAdapterContext IntentContext,
    PlannerToolCatalog ToolCatalog,
    AgentPluginSelectionMode? PluginSelectionMode,
    IReadOnlyCollection<Guid>? SelectedPluginIds,
    AgentCapabilitySelectionMode? CapabilitySelectionMode,
    IReadOnlyCollection<string>? RequestedCapabilityCodes);

internal sealed class AgentPlanV2Compiler(
    IntentResultToCandidateAdapter intentAdapter,
    AgentPlanCanonicalizer canonicalizer)
{
    public Result<CanonicalAgentPlan> Compile(AgentPlanV2CompilationRequest request)
    {
        var pluginMode = request.PluginSelectionMode ?? AgentPluginSelectionMode.BuiltInOnly;
        var selectedPluginIds = CanonicalGuids(request.SelectedPluginIds ?? []);
        if (pluginMode == AgentPluginSelectionMode.ExplicitAllowlist)
        {
            return Invalid(
                "P0 has no verified stable plugin roster; ExplicitAllowlist is fail-closed until its P1 owner is implemented.");
        }

        if (selectedPluginIds.Length != 0)
        {
            return Invalid("BuiltInOnly requires selectedPluginIds=[].");
        }

        var capabilityMode = request.CapabilitySelectionMode ?? AgentCapabilitySelectionMode.InferredFromGoal;
        var routedIntents = request.RoutedIntents.Count == 0
            ? new[] { new IntentResult { Intent = "General.Chat", Confidence = 1 } }
            : request.RoutedIntents.ToArray();
        var explicitCapabilityCodes = CanonicalStrings(request.RequestedCapabilityCodes ?? []);
        if (capabilityMode == AgentCapabilitySelectionMode.ExplicitAllowlist)
        {
            var routedCodes = routedIntents
                .Select(intent => intent.Intent?.Trim())
                .Where(code => !string.IsNullOrWhiteSpace(code))
                .Select(code => code!)
                .ToHashSet(StringComparer.Ordinal);
            routedIntents = routedIntents
                .Concat(explicitCapabilityCodes
                    .Where(code => !routedCodes.Contains(code))
                    .Select(code => new IntentResult { Intent = code, Confidence = 1 }))
                .ToArray();
        }

        var adapted = intentAdapter.Adapt(routedIntents, request.IntentContext);
        if (!adapted.IsSuccess)
        {
            return Result.From(adapted);
        }

        var allCandidates = adapted.Value!.ToArray();
        var requestedCapabilityCodes = capabilityMode == AgentCapabilitySelectionMode.ExplicitAllowlist
            ? explicitCapabilityCodes
            : CanonicalStrings(allCandidates.Select(candidate => candidate.IntentCode));
        var candidates = capabilityMode == AgentCapabilitySelectionMode.ExplicitAllowlist
            ? allCandidates
                .Where(candidate => requestedCapabilityCodes.Contains(candidate.IntentCode, StringComparer.Ordinal))
                .OrderBy(candidate => candidate.IntentCode, StringComparer.Ordinal)
                .ToArray()
            : allCandidates;
        var availableCandidates = candidates
            .Where(candidate => candidate.Availability == AgentIntentAvailability.Available)
            .ToArray();

        var normalizedStepsResult = NormalizeSteps(request.Steps);
        if (!normalizedStepsResult.IsSuccess)
        {
            return Result.From(normalizedStepsResult);
        }

        var normalizedSteps = normalizedStepsResult.Value!;
        var canCompileNodes = requestedCapabilityCodes.Length != 0 &&
                              availableCandidates.Length != 0 &&
                              HasRequiredTypedSources(
                                  normalizedSteps,
                                  availableCandidates,
                                  request.LegacyPlan.CloudReadonlyIntent);
        var executableSteps = canCompileNodes ? normalizedSteps : [];
        var nodes = BuildNodes(executableSteps, availableCandidates, request.LegacyPlan);
        var approvalCheckpoints = executableSteps
            .Where(step => step.RequiresApproval)
            .Select(step => string.IsNullOrWhiteSpace(step.ToolCode) ? step.Title : step.ToolCode!)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
        var toolApprovalCheckpoints = executableSteps
            .Where(step => step.RequiresApproval && !string.IsNullOrWhiteSpace(step.ToolCode))
            .Select(step => step.ToolCode!)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
        var capabilityGaps = candidates
            .Where(candidate => candidate.CapabilityGap is not null)
            .Select(candidate => $"{candidate.CapabilityGap!.Code}: {candidate.CapabilityGap.Detail}")
            .Concat(canCompileNodes || normalizedSteps.Count == 0
                ? []
                : ["typed_source_unavailable: Required stable typed source resolution is incomplete; the PlanDraft is non-executable."])
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();

        var toolCatalogDigest = HashToolCatalog(request.ToolCatalog.Tools);
        var providerCatalogDigest = HashProviderCatalog(request.ToolCatalog.Tools);
        var pluginCatalogDigest = HashToolSubset(request.ToolCatalog.Tools, "Plugin");
        var mcpCatalogDigest = HashToolSubset(request.ToolCatalog.Tools, "Mcp");
        var artifactTargets = CanonicalStrings(request.LegacyPlan.ArtifactTypes ?? []);
        var plan = request.LegacyPlan with
        {
            Version = 2,
            Goal = BuildSafeGoalSummary(request.LegacyPlan.TaskType, request.RawGoal),
            UploadIds = CanonicalGuids(request.LegacyPlan.UploadIds),
            KnowledgeBaseIds = CanonicalGuids(request.LegacyPlan.KnowledgeBaseIds),
            Steps = executableSteps
                .Select(ToDocument)
                .ToArray(),
            DataSourceIds = CanonicalGuids(request.LegacyPlan.DataSourceIds ?? []),
            BusinessDomains = CanonicalStrings(request.LegacyPlan.BusinessDomains ?? []),
            ArtifactTypes = artifactTargets,
            ForcedStepCodes = CanonicalStrings(request.LegacyPlan.ForcedStepCodes ?? []),
            ApprovalCheckpoints = approvalCheckpoints,
            ToolApprovalCheckpoints = toolApprovalCheckpoints,
            SkillRoutingReason = null,
            PlanKind = AgentTaskPlanKinds.PlanDraft,
            IsExecutable = false,
            CapabilityGaps = capabilityGaps,
            SchemaVersion = AgentPlanContractVersions.PlanV2,
            PlanId = Guid.NewGuid(),
            PlanVersion = 1,
            PlanDigest = null,
            TopologyProfile = "LinearV1",
            IntentCandidates = candidates,
            CapabilitySelectionMode = capabilityMode,
            RequestedCapabilityCodes = requestedCapabilityCodes,
            PluginSelectionMode = pluginMode,
            SelectedPluginIds = selectedPluginIds,
            ArtifactTargets = artifactTargets,
            Nodes = nodes,
            JoinPolicies = [],
            Budgets = new AgentPlanBudgetDocument(
                "budget-policy:v1",
                16,
                1800,
                AgentPlanContractVersions.MaxPlanCanonicalBytes),
            ApprovalSummary = new AgentPlanApprovalSummaryDocument(
                true,
                approvalCheckpoints),
            ExecutionSnapshot = new AgentExecutionSnapshotDocument(
                AgentPlanContractVersions.ExecutionSnapshotV1,
                AgentPlanContractVersions.PlanPolicyV1,
                request.ToolCatalog.Version,
                toolCatalogDigest,
                providerCatalogDigest,
                AgentIntentCatalogV1.CatalogVersion,
                AgentIntentCatalogV1.CatalogDigest,
                "agent_planner",
                "agent-planner:v2",
                CanonicalJson.ComputeSha256("agent_planner|agent-planner:v2"),
                request.LegacyPlan.PlannerModelId,
                null,
                null,
                pluginCatalogDigest,
                mcpCatalogDigest,
                "data-contract:v1",
                "knowledge-contract:v1",
                "agent-policy:v1",
                "agent-guard:v1",
                "budget-policy:v1",
                AgentPlanContractVersions.MaxPlanCanonicalBytes),
            SecuritySummary = new AgentPlanSecuritySummaryDocument(
                true,
                false,
                false,
                false,
                false,
                false)
        };

        return canonicalizer.Seal(plan);
    }

    private static Result<IReadOnlyCollection<AgentStepPlanDto>> NormalizeSteps(
        IReadOnlyCollection<AgentStepPlanDto> steps)
    {
        var normalized = new List<AgentStepPlanDto>(steps.Count);
        foreach (var step in steps)
        {
            string? inputJson = null;
            if (!string.IsNullOrWhiteSpace(step.InputJson))
            {
                try
                {
                    inputJson = CanonicalJson.Canonicalize(step.InputJson);
                }
                catch (JsonException)
                {
                    return Result.Failure(new ApiProblemDescriptor(
                        AppProblemCodes.AgentPlanInvalid,
                        $"Tool input for '{step.ToolCode ?? step.Title}' is not valid JSON."));
                }
            }

            normalized.Add(step with { InputJson = inputJson });
        }

        return Result.Success<IReadOnlyCollection<AgentStepPlanDto>>(normalized);
    }

    private static bool HasRequiredTypedSources(
        IReadOnlyCollection<AgentStepPlanDto> steps,
        IReadOnlyCollection<AgentIntentCandidateDocument> candidates,
        AgentTaskPlanCloudReadonlyIntentDocument? cloudReadonlyIntent)
    {
        if (steps.Any(step => string.Equals(step.ToolCode, "query_cloud_data_readonly", StringComparison.Ordinal)) &&
            (cloudReadonlyIntent is null ||
             !candidates.Any(candidate => candidate.IntentClass == AgentIntentClass.CloudOnly)))
        {
            return false;
        }

        if (steps.Any(step => string.Equals(step.ToolCode, "query_business_database_readonly", StringComparison.Ordinal)) &&
            !candidates.Any(candidate =>
                candidate.IntentClass == AgentIntentClass.GovernedExploration &&
                candidate.RequestedResources.DataSourceIds.Count != 0))
        {
            return false;
        }

        return true;
    }

    private static AgentPlanNodeDocument[] BuildNodes(
        IReadOnlyCollection<AgentStepPlanDto> steps,
        IReadOnlyCollection<AgentIntentCandidateDocument> candidates,
        AgentTaskPlanDocument plan)
    {
        var result = new List<AgentPlanNodeDocument>(steps.Count);
        var cloudCapabilities = CanonicalStrings(candidates
            .Where(candidate => candidate.IntentClass == AgentIntentClass.CloudOnly)
            .Select(candidate => candidate.IntentCode));
        var governedCapabilities = CanonicalStrings(candidates
            .Where(candidate => candidate.IntentClass == AgentIntentClass.GovernedExploration)
            .Select(candidate => candidate.IntentCode));
        var knowledgeCapabilities = CanonicalStrings(candidates
            .Where(candidate => candidate.IntentClass == AgentIntentClass.Knowledge)
            .Select(candidate => candidate.IntentCode));
        var generalCapabilities = CanonicalStrings(candidates
            .Where(candidate => candidate.IntentClass is not AgentIntentClass.CloudOnly and not AgentIntentClass.GovernedExploration)
            .Select(candidate => candidate.IntentCode));
        var dataScopes = CanonicalGuids(plan.DataSourceIds ?? []);
        var knowledgeScopes = CanonicalGuids(plan.KnowledgeBaseIds);

        var index = 0;
        foreach (var step in steps)
        {
            index++;
            var nodeId = $"node-{index:000}";
            var nodeKind = ResolveNodeKind(step);
            var requestedCapabilities = nodeKind switch
            {
                "CloudReadNode" => cloudCapabilities,
                "GovernedDataReadNode" => governedCapabilities,
                "KnowledgeReadNode" when knowledgeCapabilities.Length != 0 => knowledgeCapabilities,
                _ => generalCapabilities
            };
            var input = BuildNodeInput(nodeKind, step, plan, candidates);
            result.Add(new AgentPlanNodeDocument(
                AgentPlanContractVersions.NodeV1,
                nodeId,
                nodeKind,
                index == 1 ? [] : [$"node-{index - 1:000}"],
                true,
                "node-input:v1",
                ResolveOutputSchema(nodeKind),
                string.IsNullOrWhiteSpace(step.ToolCode) ? [] : [step.ToolCode!],
                requestedCapabilities,
                dataScopes,
                knowledgeScopes,
                [],
                input,
                null,
                new AgentPlanTimeoutPolicyDocument("timeout-policy:v1", 120),
                new AgentPlanRetryPolicyDocument("retry-policy:v1", 1, "None"),
                new AgentPlanNodeBudgetDocument(0, 0, 0),
                new AgentPlanApprovalPolicyDocument(
                    step.RequiresApproval,
                    step.RequiresApproval ? "UserApprovalRequired" : "None"),
                new AgentPlanIdempotencyPolicyDocument("idempotency-policy:v1", "Deterministic"),
                ResolveSideEffectClass(nodeKind),
                null));
        }

        return result.ToArray();
    }

    private static AgentPlanNodeInputDocument BuildNodeInput(
        string nodeKind,
        AgentStepPlanDto step,
        AgentTaskPlanDocument plan,
        IReadOnlyCollection<AgentIntentCandidateDocument> candidates)
    {
        if (nodeKind == "CloudReadNode")
        {
            var intent = plan.CloudReadonlyIntent!;
            var semanticQuery = intent.Query ?? "{}";
            return new AgentPlanNodeInputDocument(
                intent.Intent,
                CanonicalJson.ComputeSha256(semanticQuery),
                "CloudAiRead",
                CanonicalStrings([intent.Target, intent.Kind]),
                null,
                null,
                null,
                [],
                null,
                "CloudReadOnly",
                step.InputJson);
        }

        if (nodeKind == "GovernedDataReadNode")
        {
            var sourceId = candidates
                .Where(candidate => candidate.IntentClass == AgentIntentClass.GovernedExploration)
                .SelectMany(candidate => candidate.RequestedResources.DataSourceIds)
                .OrderBy(id => id.ToString("D"), StringComparer.Ordinal)
                .First();
            return new AgentPlanNodeInputDocument(
                null,
                null,
                null,
                [],
                null,
                "TextToSql",
                sourceId,
                CanonicalStrings(plan.BusinessDomains ?? []),
                CanonicalJson.ComputeSha256($"governed-source:{sourceId:D}"),
                "ReadOnly",
                step.InputJson);
        }

        return new AgentPlanNodeInputDocument(
            null,
            null,
            null,
            [],
            null,
            null,
            null,
            CanonicalStrings(plan.BusinessDomains ?? []),
            null,
            null,
            step.InputJson);
    }

    private static string ResolveNodeKind(AgentStepPlanDto step)
    {
        return step.ToolCode switch
        {
            "query_cloud_data_readonly" => "CloudReadNode",
            "query_business_database_readonly" => "GovernedDataReadNode",
            "rag_search" => "KnowledgeReadNode",
            "read_uploaded_file" => "FileReadNode",
            "generate_chart_data" or "generate_business_chart" or
                "generate_markdown_report" or "generate_html_report" or
                "generate_pdf" or "generate_pptx" or "generate_xlsx" => "ArtifactBuildNode",
            "finalize_artifacts" => "ApprovalCheckpointNode",
            _ => "DeterministicTransformNode"
        };
    }

    private static string ResolveOutputSchema(string nodeKind)
    {
        return nodeKind switch
        {
            "CloudReadNode" => "evidence:cloud-read:v1",
            "GovernedDataReadNode" => "evidence:governed-data-read:v1",
            "KnowledgeReadNode" => "evidence:knowledge-read:v1",
            "FileReadNode" => "evidence:file-read:v1",
            "ArtifactBuildNode" => "evidence:artifact-draft:v1",
            "ApprovalCheckpointNode" => "evidence:approval:v1",
            _ => "evidence:derived-metric:v1"
        };
    }

    private static string ResolveSideEffectClass(string nodeKind)
    {
        return nodeKind switch
        {
            "ArtifactBuildNode" => "ArtifactDraftOnly",
            "ApprovalCheckpointNode" => "ApprovalOnly",
            _ => "ReadOnly"
        };
    }

    private static AgentTaskPlanStepDocument ToDocument(AgentStepPlanDto step)
    {
        return new AgentTaskPlanStepDocument(
            step.Title,
            step.Description,
            step.StepType,
            step.ToolCode,
            step.RequiresApproval,
            step.InputJson);
    }

    private static string BuildSafeGoalSummary(string taskType, string rawGoal)
    {
        var digest = CanonicalJson.ComputeSha256(rawGoal.Trim());
        return $"{taskType} task; goalSha256={digest}";
    }

    internal static string HashToolCatalog(IEnumerable<AgentPlannerToolSummary> tools)
    {
        var inventory = tools
            .OrderBy(tool => tool.ToolCode, StringComparer.Ordinal)
            .Select(tool => new
            {
                tool.ToolCode,
                tool.ProviderKind,
                tool.TargetType,
                tool.TargetName,
                tool.InputSchemaJson,
                tool.RequiresApproval,
                tool.RiskLevel
            })
            .ToArray();
        return CanonicalJson.ComputeSha256(CanonicalJson.Serialize(inventory));
    }

    internal static string HashProviderCatalog(IEnumerable<AgentPlannerToolSummary> tools)
    {
        var inventory = tools
            .Select(tool => $"{tool.ProviderKind}|{tool.TargetType}|{tool.TargetName}")
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
        return CanonicalJson.ComputeSha256(CanonicalJson.Serialize(inventory));
    }

    internal static string HashToolSubset(
        IEnumerable<AgentPlannerToolSummary> tools,
        string providerKind)
    {
        var inventory = tools
            .Where(tool => string.Equals(tool.ProviderKind, providerKind, StringComparison.OrdinalIgnoreCase))
            .Select(tool => tool.ToolCode)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
        return CanonicalJson.ComputeSha256(CanonicalJson.Serialize(inventory));
    }

    private static string[] CanonicalStrings(IEnumerable<string> values)
    {
        return values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
    }

    private static Guid[] CanonicalGuids(IEnumerable<Guid> values)
    {
        return values
            .Where(value => value != Guid.Empty)
            .Distinct()
            .OrderBy(value => value.ToString("D"), StringComparer.Ordinal)
            .ToArray();
    }

    private static Result<CanonicalAgentPlan> Invalid(string detail)
    {
        return Result.Failure(new ApiProblemDescriptor(AppProblemCodes.AgentPlanInvalid, detail));
    }
}
