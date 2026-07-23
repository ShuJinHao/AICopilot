using System.Text;
using System.Text.Json;
using AICopilot.Core.AiGateway.Aggregates.AgentTasks;
using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Ai;
using AICopilot.SharedKernel.Result;

namespace AICopilot.AiGatewayService.AgentTasks;

public sealed record AgentPlanContractMetadata(
    string SchemaVersion,
    string PlanDigest,
    string TopologyProfile,
    bool IsExecutable,
    int CanonicalByteCount);

public interface IAgentPlanIntegrityValidator
{
    Result<AgentPlanContractMetadata> ValidatePersisted(
        string planJson,
        bool requireExecutable = false);

    Result<IReadOnlyCollection<AgentTaskPlanExecutionStepContract>> ReadExecutionContract(
        string planJson,
        bool requireExecutable = false);
}

internal sealed record CanonicalAgentPlan(
    AgentTaskPlanDocument Document,
    string CanonicalJson,
    string Digest,
    int CanonicalByteCount);

internal sealed class AgentPlanCanonicalizer : IAgentPlanIntegrityValidator
{
    private static readonly IReadOnlySet<string> FrozenCoreToolCodes = new HashSet<string>(
        [
            "read_uploaded_file",
            "parse_table_file",
            "query_cloud_data_readonly",
            "query_business_database_readonly",
            "summarize_business_query_result",
            "join_evidence",
            "assess_cloud_health",
            "agent_reasoning",
            "rag_search",
            "generate_business_chart",
            "generate_chart_data",
            "generate_markdown_report",
            "generate_html_report",
            "generate_pdf",
            "generate_pptx",
            "generate_xlsx",
            "finalize_artifacts"
        ],
        StringComparer.Ordinal);

    private static readonly IReadOnlySet<string> ArtifactToolCodes = new HashSet<string>(
        [
            "generate_chart_data",
            "generate_business_chart",
            "generate_markdown_report",
            "generate_html_report",
            "generate_pdf",
            "generate_pptx",
            "generate_xlsx",
            "finalize_artifacts"
        ],
        StringComparer.Ordinal);

    private static readonly IReadOnlySet<string> FrozenForbiddenExecutionSemanticTokens = new HashSet<string>(
        [
            "cloudwrite",
            "cloud_write",
            "cloudmutation",
            "cloud_mutation",
            "mutation",
            "write",
            "update",
            "plc",
            "control",
            "controlnode",
            "control_action",
            "recipe_update",
            "recipe_write",
            "device_disable",
            "disable",
            "delete"
        ],
        StringComparer.Ordinal);

    public Result<CanonicalAgentPlan> Seal(AgentTaskPlanDocument plan)
    {
        var structure = ValidateStructure(plan, verifyDigest: false, requireExecutable: false);
        if (!structure.IsSuccess)
        {
            return Result.From(structure);
        }

        try
        {
            var digestPlaceholder = plan with { PlanDigest = new string('0', 64) };
            var boundedCanonicalByteCount = CanonicalJson.MeasureCanonicalUtf8Bytes(
                JsonSerializer.Serialize(digestPlaceholder, CanonicalJson.SerializerOptions),
                AgentPlanContractVersions.MaxPlanCanonicalBytes);
            if (boundedCanonicalByteCount > AgentPlanContractVersions.MaxPlanCanonicalBytes)
            {
                return PayloadTooLarge();
            }

            var withoutDigest = plan with { PlanDigest = null };
            var digestSource = CanonicalJson.Canonicalize(
                JsonSerializer.Serialize(withoutDigest, CanonicalJson.SerializerOptions),
                AgentPlanContractSchemaAuthority.DigestExcludedRootProperties);
            var digest = CanonicalJson.ComputeSha256(digestSource);
            var sealedPlan = plan with { PlanDigest = digest };
            var canonicalJson = CanonicalJson.Serialize(sealedPlan);
            var byteCount = Encoding.UTF8.GetByteCount(canonicalJson);
            if (byteCount != boundedCanonicalByteCount)
            {
                return Invalid("Plan v2 digest placeholder and final SHA-256 changed canonical byte count.");
            }

            var roundTrip = DeserializeStrict(canonicalJson);
            if (!roundTrip.IsSuccess)
            {
                return Result.From(roundTrip);
            }

            var roundTripJson = CanonicalJson.Serialize(roundTrip.Value!);
            if (!string.Equals(canonicalJson, roundTripJson, StringComparison.Ordinal))
            {
                return Invalid("Plan v2 failed canonical serialize/deserialize round-trip verification.");
            }

            return Result.Success(new CanonicalAgentPlan(
                roundTrip.Value!,
                canonicalJson,
                digest,
                byteCount));
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException)
        {
            return Invalid("Plan v2 cannot be canonicalized with the frozen contract.");
        }
    }

    public Result<AgentPlanContractMetadata> ValidatePersisted(
        string planJson,
        bool requireExecutable = false)
    {
        if (string.IsNullOrWhiteSpace(planJson))
        {
            return InvalidMetadata("Plan JSON is required.");
        }

        var byteCount = Encoding.UTF8.GetByteCount(planJson);
        if (byteCount > AgentPlanContractVersions.MaxPlanCanonicalBytes)
        {
            return Result.Failure(new ApiProblemDescriptor(
                AppProblemCodes.PlanPayloadTooLarge,
                $"Plan v2 canonical payload is {byteCount} UTF-8 bytes; maximum is {AgentPlanContractVersions.MaxPlanCanonicalBytes}."));
        }

        try
        {
            var canonicalJson = CanonicalJson.Canonicalize(
                planJson,
                AgentPlanContractVersions.MaxPlanCanonicalBytes);
            if (!string.Equals(planJson, canonicalJson, StringComparison.Ordinal))
            {
                return InvalidMetadata("Persisted Plan v2 JSON is not in canonical form.");
            }

            var deserialized = DeserializeStrict(canonicalJson);
            if (!deserialized.IsSuccess)
            {
                return Result.From(deserialized);
            }

            var plan = deserialized.Value!;
            var structure = ValidateStructure(plan, verifyDigest: true, requireExecutable);
            if (!structure.IsSuccess)
            {
                return Result.From(structure);
            }

            var roundTripJson = CanonicalJson.Serialize(plan);
            if (!string.Equals(canonicalJson, roundTripJson, StringComparison.Ordinal))
            {
                return InvalidMetadata("Persisted Plan v2 failed fresh deserialize/canonicalize round-trip verification.");
            }

            var digestSource = CanonicalJson.Canonicalize(
                canonicalJson,
                AgentPlanContractSchemaAuthority.DigestExcludedRootProperties);
            var actualDigest = CanonicalJson.ComputeSha256(digestSource);
            if (!string.Equals(plan.PlanDigest, actualDigest, StringComparison.Ordinal))
            {
                return InvalidMetadata("Persisted Plan v2 digest does not match its canonical execution contract.");
            }

            return Result.Success(new AgentPlanContractMetadata(
                plan.SchemaVersion,
                actualDigest,
                plan.TopologyProfile!,
                plan.IsExecutable,
                byteCount));
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException)
        {
            return InvalidMetadata("Persisted Plan v2 JSON is invalid or contains duplicate/unsupported fields.");
        }
    }

    public Result<IReadOnlyCollection<AgentTaskPlanExecutionStepContract>> ReadExecutionContract(
        string planJson,
        bool requireExecutable = false)
    {
        var validation = ValidatePersisted(planJson, requireExecutable);
        if (!validation.IsSuccess)
        {
            return Result.From(validation);
        }

        var deserialized = DeserializeStrict(planJson);
        if (!deserialized.IsSuccess)
        {
            return Result.From(deserialized);
        }

        return Result.Success<IReadOnlyCollection<AgentTaskPlanExecutionStepContract>>(
            deserialized.Value!.Steps
                .Select((step, index) => new AgentTaskPlanExecutionStepContract(
                    index + 1,
                    step.Title,
                    step.Description,
                    step.StepType,
                    step.ToolCode,
                    step.RequiresApproval,
                    step.InputJson))
                .ToArray());
    }

    private static Result ValidateStructure(
        AgentTaskPlanDocument plan,
        bool verifyDigest,
        bool requireExecutable)
    {
        if (plan.Version != 2 ||
            !string.Equals(plan.SchemaVersion, AgentPlanContractVersions.PlanV2, StringComparison.Ordinal))
        {
            return InvalidResult($"Unsupported agent plan schemaVersion '{plan.SchemaVersion}'. Active plans require 2.0.");
        }

        if (plan.PlanId is null || plan.PlanId == Guid.Empty || plan.PlanVersion <= 0)
        {
            return InvalidResult("Plan v2 requires a non-empty planId and positive planVersion.");
        }

        if (verifyDigest && !IsSha256(plan.PlanDigest))
        {
            return InvalidResult("Active Plan v2 requires a lowercase SHA-256 planDigest.");
        }

        if (plan.TopologyProfile is not ("LinearV1" or "DagV1"))
        {
            return InvalidResult("Plan v2 requires an explicit topologyProfile of LinearV1 or DagV1.");
        }

        var validDraftLifecycle =
            string.Equals(plan.PlanKind, AgentTaskPlanKinds.PlanDraft, StringComparison.Ordinal) &&
            !plan.IsExecutable &&
            string.Equals(plan.LifecycleSealPadding, "0000", StringComparison.Ordinal);
        var validExecutableLifecycle =
            string.Equals(plan.PlanKind, AgentTaskPlanKinds.ExecutablePlan, StringComparison.Ordinal) &&
            plan.IsExecutable &&
            string.Equals(plan.LifecycleSealPadding, string.Empty, StringComparison.Ordinal);
        if (!validDraftLifecycle && !validExecutableLifecycle)
        {
            return InvalidResult(
                "Plan lifecycle must be exactly (PlanDraft,false,0000) or (ExecutablePlan,true,empty padding).");
        }

        if (requireExecutable && !validExecutableLifecycle)
        {
            return InvalidResult("Runtime requires a confirmed ExecutablePlan v2.");
        }

        if (plan.UploadIds is null ||
            plan.KnowledgeBaseIds is null ||
            plan.CloudReadonlyIntents is null ||
            plan.Steps is null ||
            plan.RuntimeSettings is null ||
            plan.DataSourceIds is null ||
            plan.BusinessDomains is null ||
            plan.ForcedStepCodes is null ||
            plan.ApprovalCheckpoints is null ||
            plan.DataSourceSummaries is null ||
            plan.ToolRiskSummary is null ||
            plan.ToolApprovalCheckpoints is null ||
            plan.CapabilityGaps is null ||
            plan.IntentCandidates is null ||
            plan.RequestedCapabilityCodes is null ||
            plan.SelectedPluginIds is null ||
            plan.ArtifactTargets is null ||
            plan.Nodes is null ||
            plan.JoinPolicies is null ||
            plan.Budgets is null ||
            plan.ConcurrencyPolicy is null ||
            plan.ApprovalSummary is null ||
            plan.ExecutionSnapshot is null ||
            plan.SecuritySummary is null ||
            plan.CapabilitySelectionMode is null ||
            plan.PluginSelectionMode is null)
        {
            return InvalidResult("Plan v2 arrays, selection modes, budgets, approval, snapshot, and security summaries must be explicit and non-null.");
        }

        if (!Enum.IsDefined(plan.CapabilitySelectionMode.Value) ||
            !Enum.IsDefined(plan.PluginSelectionMode.Value) ||
            !Enum.TryParse<AgentTaskType>(plan.TaskType, ignoreCase: false, out var taskType) ||
            !Enum.IsDefined(taskType) ||
            !string.Equals(plan.TaskType, taskType.ToString(), StringComparison.Ordinal) ||
            !Enum.TryParse<AgentTaskRiskLevel>(plan.RiskLevel, ignoreCase: false, out var riskLevel) ||
            !Enum.IsDefined(riskLevel) ||
            !string.Equals(plan.RiskLevel, riskLevel.ToString(), StringComparison.Ordinal))
        {
            return InvalidResult("Plan v2 contains an unknown task, risk, or selection-mode enum value.");
        }

        if (plan.PlannerSafetySummary is null ||
            !string.Equals(plan.PlannerSafetySummary.PlanSource, "PlanV2Contract", StringComparison.Ordinal))
        {
            return InvalidResult("Plan v2 requires the single PlanV2Contract source authority.");
        }

        if (!IsCanonicalGoalSummary(plan.TaskType, plan.Goal) ||
            string.IsNullOrWhiteSpace(plan.PlannerTemplateCode) ||
            plan.RuntimeSettings.AgentPlanningHistoryCount < 0 ||
            plan.RuntimeSettings.ContextTokenLimit < 0)
        {
            return InvalidResult("Plan v2 goal/template/runtime/fallback fields are outside the frozen server-owned contract.");
        }

        var rootSets = new[]
        {
            ValidateCanonicalGuidSet(plan.UploadIds, "uploadIds"),
            ValidateCanonicalGuidSet(plan.KnowledgeBaseIds, "knowledgeBaseIds"),
            ValidateCanonicalGuidSet(plan.DataSourceIds, "dataSourceIds"),
            ValidateCanonicalStringSet(plan.BusinessDomains, "businessDomains"),
            ValidateCanonicalStringSet(plan.ForcedStepCodes, "forcedStepCodes"),
            ValidateCanonicalStringSet(plan.ApprovalCheckpoints, "approvalCheckpoints"),
            ValidateCanonicalStringSet(plan.ToolApprovalCheckpoints, "toolApprovalCheckpoints"),
            ValidateCanonicalStringSet(plan.CapabilityGaps, "capabilityGaps"),
            ValidateCanonicalStringSet(plan.RequestedCapabilityCodes, "requestedCapabilityCodes"),
            ValidateCanonicalGuidSet(plan.SelectedPluginIds, "selectedPluginIds"),
            ValidateCanonicalStringSet(plan.ArtifactTargets, "artifactTargets")
        };
        var failedRootSet = rootSets.FirstOrDefault(result => !result.IsSuccess);
        if (failedRootSet is not null)
        {
            return failedRootSet;
        }

        if (plan.CapabilityGaps.Any(gap => !AgentPlanCapabilityGapCodes.IsFrozen(gap)))
        {
            return InvalidResult("capabilityGaps must contain only frozen server-owned gap codes.");
        }

        var hasExactSimulationSourceBoundary =
            plan.DataSourceIds.Count == 1 &&
            plan.DataSourceSummaries.Count == 1 &&
            plan.DataSourceSummaries.Single() is { } simulationSource &&
            simulationSource.Id == plan.DataSourceIds.Single() &&
            simulationSource.IsSimulation &&
            string.Equals(
                simulationSource.SourceMode,
                DataSourceExternalSystemType.SimulationBusiness.ToString(),
                StringComparison.Ordinal);
        if (plan.PlannerSafetySummary.IsSimulationOnly != hasExactSimulationSourceBoundary)
        {
            return InvalidResult(
                "plannerSafetySummary.isSimulationOnly must exactly match one marked SimulationBusiness source.");
        }

        var hasUnresolvedCapabilityGaps = plan.CapabilityGaps.Count != 0;
        if (validDraftLifecycle && hasUnresolvedCapabilityGaps && plan.Nodes.Count != 0)
        {
            return InvalidResult(
                "A PlanDraft with unresolved capability gaps must remain node-free.");
        }

        if (validDraftLifecycle && !hasUnresolvedCapabilityGaps && plan.Nodes.Count == 0)
        {
            return InvalidResult(
                "A gap-free PlanDraft must contain the authoritative compiler graph.");
        }

        if (validExecutableLifecycle && (hasUnresolvedCapabilityGaps || plan.Nodes.Count == 0))
        {
            return InvalidResult(
                "ExecutablePlan v2 requires a gap-free authoritative compiler graph.");
        }

        if (plan.QueryMode is not ("CloudReadonly" or "TextToSql") ||
            taskType == AgentTaskType.CloudDataReport !=
            string.Equals(plan.QueryMode, "CloudReadonly", StringComparison.Ordinal))
        {
            return InvalidResult("queryMode must be the exact task-bound CloudReadonly/TextToSql value.");
        }

        var expectedArtifactTools = plan.ArtifactTargets
            .Select(target => target switch
            {
                "chart" => plan.DataSourceIds.Count > 0
                    ? "generate_business_chart"
                    : "generate_chart_data",
                "markdown" => "generate_markdown_report",
                "html" => "generate_html_report",
                "pdf" => "generate_pdf",
                "pptx" => "generate_pptx",
                "xlsx" => "generate_xlsx",
                _ => string.Empty
            })
            .Where(toolCode => toolCode.Length > 0)
            .OrderBy(toolCode => toolCode, StringComparer.Ordinal)
            .ToArray();
        var actualArtifactTools = plan.Steps
            .Select(step => step.ToolCode)
            .Where(toolCode => toolCode is not null &&
                               ArtifactToolCodes.Contains(toolCode) &&
                               !string.Equals(toolCode, "finalize_artifacts", StringComparison.Ordinal))
            .Select(toolCode => toolCode!)
            .OrderBy(toolCode => toolCode, StringComparer.Ordinal)
            .ToArray();
        var finalizationSteps = plan.Steps
            .Select((step, index) => (Step: step, Index: index))
            .Where(item => string.Equals(
                item.Step.ToolCode,
                "finalize_artifacts",
                StringComparison.Ordinal))
            .ToArray();
        var hasCanonicalFinalizationStep = plan.ArtifactTargets.Count == 0
            ? finalizationSteps.Length == 0 &&
              plan.Steps.All(step => step.StepType != AgentStepType.Finalize)
            : finalizationSteps.Length == 1 &&
              finalizationSteps[0].Index == plan.Steps.Count - 1 &&
              finalizationSteps[0].Step.StepType == AgentStepType.Finalize &&
              finalizationSteps[0].Step.RequiresApproval &&
              plan.Steps.Count(step => step.StepType == AgentStepType.Finalize) == 1;
        if (plan.ArtifactTargets.Any(target => target is not ("chart" or "markdown" or "html" or "pdf" or "pptx" or "xlsx")) ||
            !actualArtifactTools.SequenceEqual(expectedArtifactTools, StringComparer.Ordinal) ||
            !hasCanonicalFinalizationStep)
        {
            return InvalidResult(
                "artifactTargets must exactly and bidirectionally match artifact generation/finalization Steps.");
        }

        var stepToolCodes = plan.Steps
            .Select(step => step?.ToolCode)
            .Where(toolCode => !string.IsNullOrWhiteSpace(toolCode))
            .Select(toolCode => toolCode!)
            .ToHashSet(StringComparer.Ordinal);
        if (plan.ForcedStepCodes.Any(code => !stepToolCodes.Contains(code)))
        {
            return InvalidResult("forcedStepCodes must be a subset of the frozen runtime Step tools.");
        }

        if (plan.DataSourceSummaries.Any(summary =>
                summary is null ||
                summary.Id == Guid.Empty ||
                string.IsNullOrWhiteSpace(summary.Name) ||
                string.IsNullOrWhiteSpace(summary.SourceMode) ||
                string.IsNullOrWhiteSpace(summary.SourceLabel)) ||
            !IsCanonicalOrder(plan.DataSourceSummaries.Select(summary => summary.Id.ToString("D"))) ||
            !plan.DataSourceIds.SequenceEqual(
                plan.DataSourceSummaries.Select(summary => summary.Id),
                EqualityComparer<Guid>.Default) ||
            !plan.BusinessDomains.SequenceEqual(
                plan.DataSourceSummaries
                    .Select(summary => summary.BusinessDomain)
                    .Where(domain => !string.IsNullOrWhiteSpace(domain))
                    .Select(domain => domain!)
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(domain => domain, StringComparer.Ordinal),
                StringComparer.Ordinal))
        {
            return InvalidResult("dataSourceSummaries must exactly describe the canonical dataSourceIds/businessDomains roster.");
        }

        var validRiskNames = Enum.GetNames<AiToolRiskLevel>().ToHashSet(StringComparer.Ordinal);
        if (plan.ToolRiskSummary.Any(pair =>
                !validRiskNames.Contains(pair.Key) || pair.Value < 0) ||
            plan.PlannerSafetySummary!.ToolRiskSummary is null ||
            !plan.ToolRiskSummary.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .SequenceEqual(plan.PlannerSafetySummary.ToolRiskSummary.OrderBy(pair => pair.Key, StringComparer.Ordinal)) ||
            plan.ToolRiskSummary.Values.Sum() != plan.VisibleToolCount ||
            plan.VisibleToolCount != plan.PlannerAvailableToolCount)
        {
            return InvalidResult("tool risk/count summaries must exactly match the frozen planner-visible catalog summary.");
        }

        if (plan.PluginSelectionMode != AgentPluginSelectionMode.BuiltInOnly ||
            plan.SelectedPluginIds.Count != 0)
        {
            return InvalidResult("P0 requires pluginSelectionMode=BuiltInOnly and selectedPluginIds=[].");
        }

        if (plan.CapabilitySelectionMode == AgentCapabilitySelectionMode.InferredFromGoal &&
            plan.Nodes.Count > 0 &&
            plan.RequestedCapabilityCodes.Count == 0)
        {
            return InvalidResult("InferredFromGoal executable nodes require a non-empty canonical capability set.");
        }

        if (plan.CapabilitySelectionMode == AgentCapabilitySelectionMode.ExplicitAllowlist &&
            plan.RequestedCapabilityCodes.Count == 0 &&
            plan.Nodes.Count > 0)
        {
            return InvalidResult("An explicit empty capability allowlist cannot contain executable nodes.");
        }

        var expectedJoinPolicies = AgentPlanCanonicalCollections.Strings(
            plan.Nodes.Select(node => node.JoinPolicy).Where(policy => policy is not null).Select(policy => policy!));
        if (!plan.JoinPolicies.SequenceEqual(expectedJoinPolicies, StringComparer.Ordinal))
        {
            return InvalidResult("joinPolicies must exactly match the canonical policies used by Plan nodes.");
        }

        if (plan.TopologyProfile == "LinearV1" &&
            (plan.JoinPolicies.Count != 0 ||
             plan.ConcurrencyPolicy.MaxParallelism != 1 ||
             !string.Equals(
                 plan.ConcurrencyPolicy.PolicyVersion,
                 AgentPlanContractVersions.LinearConcurrencyPolicyV1,
                 StringComparison.Ordinal)) ||
            plan.TopologyProfile == "DagV1" &&
            (plan.ConcurrencyPolicy.MaxParallelism is < AgentPlanContractVersions.DagMinParallelism
                or > AgentPlanContractVersions.DagMaxParallelism ||
             !string.Equals(
                 plan.ConcurrencyPolicy.PolicyVersion,
                 AgentPlanContractVersions.DagConcurrencyPolicyV1,
                 StringComparison.Ordinal)))
        {
            return InvalidResult("Topology profile and bounded concurrency policy do not match.");
        }

        if (plan.Budgets.MaxNodes != AgentPlanContractVersions.DefaultMaxNodes ||
            plan.Budgets.MaxToolCalls != AgentPlanContractVersions.DefaultMaxToolCalls ||
            plan.Budgets.MaxModelCalls != AgentPlanContractVersions.DefaultMaxModelCalls ||
            plan.Budgets.MaxInputTokens != AgentPlanContractVersions.DefaultMaxInputTokens ||
            plan.Budgets.MaxOutputTokens != AgentPlanContractVersions.DefaultMaxOutputTokens ||
            plan.Budgets.MaxElapsedSeconds != AgentPlanContractVersions.DefaultMaxElapsedSeconds ||
            plan.Budgets.MaxCostAmount != AgentPlanContractVersions.DefaultMaxCostAmount ||
            !string.Equals(
                plan.Budgets.CostCurrency,
                AgentPlanContractVersions.DefaultCostCurrency,
                StringComparison.Ordinal) ||
            plan.Budgets.MaxRetries != AgentPlanContractVersions.DefaultMaxRetries ||
            plan.Budgets.MaxArtifactCount != AgentPlanContractVersions.DefaultMaxArtifactCount ||
            plan.Budgets.MaxArtifactBytes != AgentPlanContractVersions.DefaultMaxArtifactBytes ||
            plan.Budgets.MaxCanonicalBytes != AgentPlanContractVersions.MaxPlanCanonicalBytes ||
            !string.Equals(
                plan.Budgets.PolicyVersion,
                AgentPlanContractVersions.BudgetPolicyV1,
                StringComparison.Ordinal))
        {
            return InvalidResult("Plan v2 budgets are outside the frozen P0 range/policy.");
        }

        if (plan.Steps.Count > plan.Budgets.MaxNodes ||
            plan.Nodes.Count > plan.Budgets.MaxNodes ||
            plan.Nodes.Count > 0 && plan.Nodes.Count != plan.Steps.Count)
        {
            return InvalidResult(
                "Plan v2 Steps/Nodes must stay within maxNodes and use a one-to-one execution projection.");
        }

        if (plan.ExecutionSnapshot.ToolCatalogVersion <= 0 ||
            !IsSha256(plan.ExecutionSnapshot.ToolCatalogDigest) ||
            !IsSha256(plan.ExecutionSnapshot.ProviderCatalogDigest) ||
            !IsSha256(plan.ExecutionSnapshot.PluginCatalogDigest) ||
            !IsSha256(plan.ExecutionSnapshot.McpCatalogDigest) ||
            !AgentPlanCatalogSnapshotAuthority.HasValidFrozenNonCatalogFields(
                plan.ExecutionSnapshot,
                plan.PlannerModelId) ||
            !AgentPlanCatalogSnapshotAuthority.MatchesAuthorizedRosters(
                plan.ExecutionSnapshot,
                plan.DataSourceIds,
                plan.KnowledgeBaseIds,
                plan.IntentCandidates) ||
            !string.Equals(
                plan.ExecutionSnapshot.ConcurrencyPolicyVersion,
                plan.ConcurrencyPolicy.PolicyVersion,
                StringComparison.Ordinal))
        {
            return InvalidResult("ExecutionSnapshot is incomplete, stale, or outside the frozen P0 snapshot contract.");
        }

        if (plan.IsExecutable && plan.ExecutionSnapshot.ModelId is null)
        {
            return InvalidResult("ExecutablePlan requires an authoritative prompt/model/context-window snapshot.");
        }

        if (!plan.SecuritySummary.CloudReadOnly ||
            plan.SecuritySummary.CloudMutationAllowed ||
            plan.SecuritySummary.PlcControlAllowed ||
            plan.SecuritySummary.PreConfirmationExecutionAllowed ||
            plan.SecuritySummary.RawPromptPersisted ||
            plan.SecuritySummary.RawRouterReasoningPersisted)
        {
            return InvalidResult("Plan v2 securitySummary violates the permanent Cloud-readonly/PLC/raw-prompt boundary.");
        }

        var approvalCheckpoints = ValidateCanonicalStringSet(
            plan.ApprovalSummary.ApprovalCheckpoints,
            "approvalSummary.approvalCheckpoints");
        if (!approvalCheckpoints.IsSuccess || !plan.ApprovalSummary.RequiresPlanConfirmation)
        {
            return !approvalCheckpoints.IsSuccess
                ? approvalCheckpoints
                : InvalidResult("Plan v2 must require explicit plan confirmation.");
        }

        if (!IsCanonicalOrder(plan.IntentCandidates.Select(candidate => candidate?.IntentCode ?? string.Empty)))
        {
            return InvalidResult("intentCandidates must be a non-null, duplicate-free ordinal canonical array.");
        }

        var candidateCodes = new HashSet<string>(StringComparer.Ordinal);
        foreach (var candidate in plan.IntentCandidates)
        {
            if (candidate is null)
            {
                return InvalidResult("IntentCandidate array cannot contain null entries.");
            }

            var candidateResult = ValidateCandidate(candidate);
            if (!candidateResult.IsSuccess)
            {
                return candidateResult;
            }

            if (!candidateCodes.Add(candidate.IntentCode))
            {
                return InvalidResult($"Duplicate IntentCandidate '{candidate.IntentCode}' is not canonical.");
            }
        }

        var orderedCandidateCodes = candidateCodes.OrderBy(value => value, StringComparer.Ordinal).ToArray();
        if (!plan.RequestedCapabilityCodes.SequenceEqual(orderedCandidateCodes, StringComparer.Ordinal))
        {
            return InvalidResult(
                "requestedCapabilityCodes must exactly match the canonical IntentCandidate catalog subset.");
        }

        if (plan.IntentCandidates.Any(candidate =>
                candidate.RequestedResources.DataSourceIds.Any(id => !plan.DataSourceIds.Contains(id)) ||
                candidate.RequestedResources.KnowledgeBaseIds.Any(id => !plan.KnowledgeBaseIds.Contains(id)) ||
                candidate.RequestedResources.UploadIds.Any(id => !plan.UploadIds.Contains(id))))
        {
            return InvalidResult("IntentCandidate resource ids must be subsets of the Plan-authorized root resource roster.");
        }

        if (validExecutableLifecycle && plan.CapabilityGaps.Count != 0)
        {
            return InvalidResult("ExecutablePlan cannot retain unresolved capability gaps.");
        }

        if (validExecutableLifecycle && plan.IntentCandidates.Any(candidate =>
                candidate.Availability != AgentIntentAvailability.Available ||
                candidate.CapabilityGap is not null))
        {
            return InvalidResult("ExecutablePlan requires every requested IntentCandidate to be available and gap-free.");
        }

        var orderedCloudIntentCodes = plan.CloudReadonlyIntents
            .Select(intent => intent.Intent)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
        if (!plan.CloudReadonlyIntents
                .Select(intent => intent.Intent)
                .SequenceEqual(orderedCloudIntentCodes, StringComparer.Ordinal) ||
            orderedCloudIntentCodes.Length != orderedCloudIntentCodes.Distinct(StringComparer.Ordinal).Count())
        {
            return InvalidResult("Cloud readonly typed semantic plans must be unique and ordered by canonical intent code.");
        }

        foreach (var intent in plan.CloudReadonlyIntents)
        {
            var cloudIntent = ValidateCloudReadonlyIntent(intent, candidateCodes);
            if (!cloudIntent.IsSuccess)
            {
                return cloudIntent;
            }
        }

        var expectedCloudIntentCodes = plan.IntentCandidates
            .Where(candidate =>
                candidate.IntentClass == AgentIntentClass.CloudOnly &&
                candidate.Availability == AgentIntentAvailability.Available &&
                candidate.CapabilityGap is null)
            .Select(candidate => candidate.IntentCode)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
        if (validExecutableLifecycle &&
            !orderedCloudIntentCodes.SequenceEqual(expectedCloudIntentCodes, StringComparer.Ordinal))
        {
            return InvalidResult("Executable Plan Cloud intents must exactly match its available Cloud-only IntentCandidates.");
        }

        if (validExecutableLifecycle &&
            taskType == AgentTaskType.CloudDataReport &&
            plan.CloudReadonlyIntents.Count == 0)
        {
            return InvalidResult("Executable CloudDataReport requires at least one frozen typed Cloud readonly semantic plan.");
        }

        var nodesResult = ValidateNodes(
            plan.Nodes,
            plan.Steps,
            plan.RequestedCapabilityCodes,
            plan.IntentCandidates,
            plan.Budgets,
            plan.TopologyProfile,
            plan.ConcurrencyPolicy,
            plan.ExecutionSnapshot.ModelId,
            plan.ExecutionSnapshot.ModelParametersHash,
            plan.CloudReadonlyIntents);
        if (!nodesResult.IsSuccess)
        {
            return nodesResult;
        }

        if (validExecutableLifecycle)
        {
            var binding = ValidateExecutableStepCapabilityBindings(plan.Steps, plan.IntentCandidates);
            if (!binding.IsSuccess)
            {
                return binding;
            }
        }

        foreach (var candidate in plan.IntentCandidates)
        {
            var matchingNodes = plan.Nodes
                .Where(node => node.RequestedCapabilityCodes.Contains(candidate.IntentCode, StringComparer.Ordinal))
                .ToArray();
            if (candidate.IntentClass == AgentIntentClass.CloudOnly &&
                matchingNodes
                    .Where(IsDataProducerNode)
                    .Any(node => !string.Equals(node.NodeKind, "CloudReadNode", StringComparison.Ordinal)))
            {
                return InvalidResult($"Cloud-only intent '{candidate.IntentCode}' can only map to CloudReadNode.");
            }

            if (candidate.IntentClass == AgentIntentClass.GovernedExploration &&
                matchingNodes
                    .Where(IsDataProducerNode)
                    .Any(node => !string.Equals(node.NodeKind, "GovernedDataReadNode", StringComparison.Ordinal)))
            {
                return InvalidResult($"Governed exploration intent '{candidate.IntentCode}' can only map to GovernedDataReadNode.");
            }

            if (candidate.Availability != AgentIntentAvailability.Available && matchingNodes.Length > 0)
            {
                return InvalidResult($"Unavailable or unknown intent '{candidate.IntentCode}' cannot map to an executable node.");
            }
        }


        var expectedApprovalCheckpoints = plan.Steps
            .Where(step => step.RequiresApproval)
            .Select(step => string.IsNullOrWhiteSpace(step.ToolCode) ? step.Title : step.ToolCode!)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
        if (!plan.ApprovalSummary.ApprovalCheckpoints.SequenceEqual(
                expectedApprovalCheckpoints,
                StringComparer.Ordinal) ||
            !plan.ApprovalCheckpoints.SequenceEqual(expectedApprovalCheckpoints, StringComparer.Ordinal) ||
            !plan.ToolApprovalCheckpoints.SequenceEqual(
                plan.Steps
                    .Where(step => step.RequiresApproval && !string.IsNullOrWhiteSpace(step.ToolCode))
                    .Select(step => step.ToolCode!)
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(value => value, StringComparer.Ordinal),
                StringComparer.Ordinal))
        {
            return InvalidResult("approvalSummary does not exactly match the frozen runtime Steps.");
        }

        return Result.Success();
    }

    private static Result ValidateCandidate(AgentIntentCandidateDocument candidate)
    {
        if (!AllRequiredFieldsPresent(
                candidate.Required,
                candidate.RequestedResources,
                candidate.Filters,
                candidate.Provenance,
                candidate.RequestedArtifacts) ||
            !string.Equals(candidate.SchemaVersion, AgentPlanContractVersions.IntentV1, StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(candidate.IntentCode) ||
            string.IsNullOrWhiteSpace(candidate.ProviderCode) ||
            !Enum.IsDefined(candidate.IntentClass) ||
            !Enum.IsDefined(candidate.Availability) ||
            !Enum.IsDefined(candidate.Required.Source) ||
            double.IsNaN(candidate.Confidence) ||
            double.IsInfinity(candidate.Confidence) ||
            candidate.Confidence is < 0 or > 1)
        {
            return InvalidResult("IntentCandidate has an invalid version, identity, provider, or confidence.");
        }

        if (ContainsForbiddenExecutionSemantic(candidate.IntentCode) || ContainsForbiddenExecutionSemantic(candidate.ProviderCode))
        {
            return InvalidResult($"IntentCandidate '{candidate.IntentCode}' requests Cloud mutation or PLC/control execution.");
        }

        if (candidate.Required.Source != AgentIntentRequiredSource.ExplicitUserGoal &&
            string.IsNullOrWhiteSpace(candidate.Required.RuleId))
        {
            return InvalidResult($"IntentCandidate '{candidate.IntentCode}' requires a stable ruleId for {candidate.Required.Source}.");
        }

        if (candidate.Required.Source == AgentIntentRequiredSource.ExplicitUserGoal &&
            candidate.Required.RuleId is not null)
        {
            return InvalidResult($"IntentCandidate '{candidate.IntentCode}' must not attach a ruleId to ExplicitUserGoal provenance.");
        }

        var artifacts = ValidateCanonicalStringSet(candidate.RequestedArtifacts, $"{candidate.IntentCode}.requestedArtifacts");
        if (!artifacts.IsSuccess)
        {
            return artifacts;
        }

        if (!AllRequiredFieldsPresent(
                candidate.RequestedResources.Devices,
                candidate.RequestedResources.DataSourceIds,
                candidate.RequestedResources.KnowledgeBaseIds,
                candidate.RequestedResources.UploadIds,
                candidate.Filters.Predicates))
        {
            return InvalidResult($"IntentCandidate '{candidate.IntentCode}' typed collections must be explicit and non-null.");
        }

        var resourceSets = new[]
        {
            ValidateCanonicalGuidSet(candidate.RequestedResources.DataSourceIds, $"{candidate.IntentCode}.dataSourceIds"),
            ValidateCanonicalGuidSet(candidate.RequestedResources.KnowledgeBaseIds, $"{candidate.IntentCode}.knowledgeBaseIds"),
            ValidateCanonicalGuidSet(candidate.RequestedResources.UploadIds, $"{candidate.IntentCode}.uploadIds")
        };
        var failedResourceSet = resourceSets.FirstOrDefault(result => !result.IsSuccess);
        if (failedResourceSet is not null)
        {
            return failedResourceSet;
        }

        if (candidate.RequestedResources.Devices.Count != 0)
        {
            return InvalidResult(
                $"IntentCandidate '{candidate.IntentCode}' cannot freeze device resources before a fresh-readable device authorization roster is added to the snapshot contract.");
        }

        if (candidate.Filters.TimeRange is { } timeRange &&
            (!AgentIntentRegistryProjector.IsCanonicalTimeZone(timeRange.TimeZone) ||
             timeRange.FromUtc is null && timeRange.ToUtc is null ||
             timeRange.FromUtc > timeRange.ToUtc ||
             timeRange.FromUtc?.Offset != TimeSpan.Zero ||
             timeRange.ToUtc?.Offset != TimeSpan.Zero))
        {
            return InvalidResult($"IntentCandidate '{candidate.IntentCode}' has an invalid typed time range.");
        }

        if (candidate.Filters.Predicates.Any(predicate =>
                predicate is null ||
                string.IsNullOrWhiteSpace(predicate.FieldCode) ||
                string.IsNullOrWhiteSpace(predicate.Operator) ||
                string.IsNullOrWhiteSpace(predicate.Value) ||
                predicate.Operator is not ("eq" or "contains" or "gte" or "lte" or "in") ||
                !AgentIntentRegistryProjector.IsAllowedPredicateField(candidate.IntentCode, predicate.FieldCode) ||
                !AgentIntentRegistryProjector.IsCanonicalPredicate(
                    candidate.IntentCode,
                    predicate.FieldCode,
                    predicate.Operator,
                    predicate.Value) ||
                ContainsForbiddenExecutionSemantic(predicate.FieldCode) ||
                ContainsForbiddenExecutionSemantic(predicate.Value) ||
                CloudReadonlyAgentTextGuard.ContainsUnsafePersistedPayload(predicate.Value)))
        {
            return InvalidResult($"IntentCandidate '{candidate.IntentCode}' contains an invalid typed predicate.");
        }

        if (!IsCanonicalOrder(
                candidate.Filters.Predicates.Select(predicate => $"{predicate.FieldCode}\u001f{predicate.Operator}\u001f{predicate.Value}")))
        {
            return InvalidResult($"IntentCandidate '{candidate.IntentCode}' predicates are not in canonical order.");
        }

        if (!MatchesVersionedIntentRegistry(candidate))
        {
            return InvalidResult($"IntentCandidate '{candidate.IntentCode}' is not a valid member of its versioned Registry/provider family.");
        }

        if (candidate.Availability != AgentIntentAvailability.Available)
        {
            if (candidate.CapabilityGap is null ||
                string.IsNullOrWhiteSpace(candidate.CapabilityGap.Code) ||
                string.IsNullOrWhiteSpace(candidate.CapabilityGap.Detail) ||
                string.IsNullOrWhiteSpace(candidate.CapabilityGap.SuggestedAction) ||
                !AgentIntentRegistryProjector.MatchesCanonicalCapabilityGap(candidate))
            {
                return InvalidResult($"Unavailable/unknown IntentCandidate '{candidate.IntentCode}' requires its exact server-owned capability gap.");
            }
        }
        else if (candidate.CapabilityGap is not null)
        {
            return InvalidResult($"Available IntentCandidate '{candidate.IntentCode}' cannot carry a capability gap.");
        }

        if (!string.Equals(candidate.Provenance.RouterVersion, AgentIntentRegistryV1.RouterVersion, StringComparison.Ordinal) ||
            !string.Equals(candidate.Provenance.PromptVersion, AgentIntentRegistryV1.PromptVersion, StringComparison.Ordinal) ||
            !string.Equals(candidate.Provenance.CatalogVersion, AgentIntentRegistryV1.RegistryVersion, StringComparison.Ordinal) ||
            !IsSha256(candidate.Provenance.CatalogDigest))
        {
            return InvalidResult($"IntentCandidate '{candidate.IntentCode}' has stale or unknown catalog provenance.");
        }

        return Result.Success();
    }

    private static Result ValidateExecutableStepCapabilityBindings(
        IReadOnlyCollection<AgentTaskPlanStepDocument> steps,
        IReadOnlyCollection<AgentIntentCandidateDocument> candidates)
    {
        var availableCandidates = candidates
            .Where(candidate => candidate.Availability == AgentIntentAvailability.Available)
            .ToArray();
        if (availableCandidates.Length == 0)
        {
            return InvalidResult("ExecutablePlan cannot execute Steps without an available requested capability.");
        }

        foreach (var step in steps)
        {
            if (string.IsNullOrWhiteSpace(step.ToolCode) || !FrozenCoreToolCodes.Contains(step.ToolCode))
            {
                return InvalidResult($"Runtime Step tool '{step.ToolCode}' is outside the frozen P0 capability/tool binding roster.");
            }

            if (!availableCandidates.Any(candidate => IsToolAuthorizedByCandidate(candidate, step.ToolCode)))
            {
                return InvalidResult(
                    $"Runtime Step tool '{step.ToolCode}' is outside the strict intersection of requested available capabilities and their frozen tool allowlists.");
            }
        }

        return Result.Success();
    }

    private static bool IsToolAuthorizedByCandidate(
        AgentIntentCandidateDocument candidate,
        string toolCode)
    {
        if (candidate.Availability != AgentIntentAvailability.Available)
        {
            return false;
        }

        return AgentIntentRegistryV1.TryGetDescriptor(candidate.IntentCode, out var descriptor) &&
               descriptor.IntentClass == candidate.IntentClass &&
               descriptor.AllowedToolCodes.Contains(toolCode, StringComparer.Ordinal);
    }

    private static bool MatchesVersionedIntentRegistry(AgentIntentCandidateDocument candidate)
    {
        if (AgentIntentRegistryV1.TryGetDescriptor(candidate.IntentCode, out var frozen))
        {
            var resourceResolutionDowngrade =
                candidate.Availability == AgentIntentAvailability.Unknown &&
                string.Equals(candidate.CapabilityGap?.Code, "resource_resolution_required", StringComparison.Ordinal);
            return candidate.IntentClass == frozen.IntentClass &&
                   string.Equals(candidate.ProviderCode, frozen.ProviderCode, StringComparison.Ordinal) &&
                   (candidate.Availability == frozen.Availability || resourceResolutionDowngrade);
        }

        if (candidate.IntentCode.StartsWith("Action.", StringComparison.Ordinal))
        {
            return candidate.IntentClass == AgentIntentClass.PluginAction &&
                   candidate.Availability == AgentIntentAvailability.KnownButUnavailable &&
                   string.Equals(candidate.ProviderCode, "PluginActionRoster", StringComparison.Ordinal) &&
                   string.Equals(candidate.CapabilityGap?.Code, AgentPlanCapabilityGapCodes.KnownCapabilityUnavailable, StringComparison.Ordinal);
        }

        return candidate.IntentClass == AgentIntentClass.Unknown &&
               candidate.Availability == AgentIntentAvailability.Unknown &&
               string.Equals(candidate.ProviderCode, "None", StringComparison.Ordinal) &&
               string.Equals(candidate.CapabilityGap?.Code, "unknown_intent", StringComparison.Ordinal);
    }

    private static Result ValidateCloudReadonlyIntent(
        AgentTaskPlanCloudReadonlyIntentDocument intent,
        IReadOnlySet<string> candidateCodes)
    {
        if (!string.Equals(intent.SchemaVersion, "cloud-readonly-semantic-plan:v1", StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(intent.Intent) ||
            !candidateCodes.Contains(intent.Intent) ||
            !Enum.IsDefined(intent.Target) ||
            !Enum.IsDefined(intent.Kind) ||
            intent.Target == SemanticQueryTarget.Recipe ||
            !string.Equals(intent.Intent, $"Analysis.{intent.Target}.{intent.Kind}", StringComparison.Ordinal) ||
            !AgentIntentRegistryV1.TryGetDescriptor(intent.Intent, out var descriptor) ||
            descriptor.IntentClass != AgentIntentClass.CloudOnly ||
            descriptor.Availability != AgentIntentAvailability.Available ||
            intent.ProjectionFields is null ||
            intent.Filters is null ||
            intent.QueryScope is null ||
            double.IsNaN(intent.Confidence) ||
            double.IsInfinity(intent.Confidence) ||
            intent.Confidence is < 0 or > 1 ||
            !CloudAiReadRowLimitPolicy.IsWithinBounds(intent.Limit) ||
            !IsSha256(intent.SemanticPlanDigest))
        {
            return InvalidResult("Cloud readonly typed semantic plan has an invalid identity, enum, scope, or limit.");
        }

        if (!IsCanonicalOrder(intent.ProjectionFields) ||
            !IsCanonicalOrder(intent.QueryScope) ||
            intent.Filters.Any(filter =>
                filter is null ||
                string.IsNullOrWhiteSpace(filter.Field) ||
                string.IsNullOrWhiteSpace(filter.Value) ||
                !Enum.IsDefined(filter.Operator) ||
                ContainsForbiddenExecutionSemantic(filter.Field) ||
                ContainsForbiddenExecutionSemantic(filter.Value)) ||
            !IsCanonicalOrder(intent.Filters.Select(filter =>
                $"{filter.Field}\u001f{filter.Operator}\u001f{filter.Value}")))
        {
            return InvalidResult("Cloud readonly typed semantic plan collections are not canonical or contain unsafe semantics.");
        }

        if (intent.TimeRange is { } timeRange &&
            (string.IsNullOrWhiteSpace(timeRange.Field) ||
             !SemanticTimeZonePolicyV1.IsCanonical(timeRange.TimeZone) ||
             timeRange.Start is null && timeRange.End is null ||
             timeRange.Start > timeRange.End ||
             timeRange.Start?.Offset != TimeSpan.Zero ||
             timeRange.End?.Offset != TimeSpan.Zero))
        {
            return InvalidResult("Cloud readonly typed semantic plan has an invalid UTC time range.");
        }

        if (intent.Sort is { } sort &&
            (string.IsNullOrWhiteSpace(sort.Field) || !Enum.IsDefined(sort.Direction)))
        {
            return InvalidResult("Cloud readonly typed semantic plan has an invalid sort contract.");
        }

        var expectedScope = new[]
            {
                $"target:{intent.Target}",
                $"kind:{intent.Kind}"
            }
            .Concat(intent.ProjectionFields.Select(field => $"projection:{field}"))
            .Concat(intent.Filters.Select(filter => $"filter:{filter.Field}"))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
        if (!intent.QueryScope.SequenceEqual(expectedScope, StringComparer.Ordinal) ||
            !string.Equals(
                intent.SemanticPlanDigest,
                AgentTaskPlanCloudReadonlyIntentDocument.ComputeSemanticPlanDigest(intent),
                StringComparison.Ordinal))
        {
            return InvalidResult("Cloud readonly typed semantic plan scope or digest does not match its canonical fields.");
        }

        return Result.Success();
    }

    private static Result ValidateNodes(
        IReadOnlyCollection<AgentPlanNodeDocument> nodes,
        IReadOnlyCollection<AgentTaskPlanStepDocument> steps,
        IReadOnlyCollection<string> topLevelCapabilities,
        IReadOnlyCollection<AgentIntentCandidateDocument> candidates,
        AgentPlanBudgetDocument planBudgets,
        string topologyProfile,
        AgentPlanConcurrencyPolicyDocument concurrencyPolicy,
        Guid? snapshotModelId,
        string? snapshotModelParametersHash,
        IReadOnlyCollection<AgentTaskPlanCloudReadonlyIntentDocument> cloudReadonlyIntents)
    {
        var orderedNodes = nodes.ToArray();
        var orderedSteps = steps.ToArray();
        if (orderedNodes.Length > 0 && orderedNodes.Length != orderedSteps.Length)
        {
            return InvalidResult("Node v1 and runtime Steps must have exact one-to-one cardinality when Nodes are present.");
        }

        var topCapabilitySet = topLevelCapabilities.ToHashSet(StringComparer.Ordinal);
        var availableCandidateCodes = candidates
            .Where(candidate => candidate.Availability == AgentIntentAvailability.Available)
            .Select(candidate => candidate.IntentCode)
            .ToHashSet(StringComparer.Ordinal);
        var availableCandidatesByCode = candidates
            .Where(candidate => candidate.Availability == AgentIntentAvailability.Available)
            .ToDictionary(candidate => candidate.IntentCode, StringComparer.Ordinal);
        var allowedDataScopes = candidates.SelectMany(candidate => candidate.RequestedResources.DataSourceIds).ToHashSet();
        var allowedKnowledgeScopes = candidates.SelectMany(candidate => candidate.RequestedResources.KnowledgeBaseIds).ToHashSet();
        var cloudIntentsByCode = cloudReadonlyIntents.ToDictionary(
            intent => intent.Intent,
            StringComparer.Ordinal);
        var nodeIds = new HashSet<string>(StringComparer.Ordinal);
        long totalToolCalls = 0;
        long totalModelCalls = 0;
        long totalInputTokens = 0;
        long totalOutputTokens = 0;
        long totalRetries = 0;
        long totalArtifactCount = 0;
        long totalArtifactBytes = 0;
        decimal totalCostAmount = 0m;
        for (var index = 0; index < orderedNodes.Length; index++)
        {
            var node = orderedNodes[index];
            if (node is null ||
                node.DependsOn is null ||
                node.RequestedToolCodes is null ||
                node.RequestedCapabilityCodes is null ||
                node.DataScopes is null ||
                node.KnowledgeScopes is null ||
                node.EvidenceSelectors is null ||
                node.Input is null ||
                node.TimeoutPolicy is null ||
                node.RetryPolicy is null ||
                node.Budget is null ||
                node.ApprovalPolicy is null ||
                node.IdempotencyPolicy is null ||
                !string.Equals(node.SchemaVersion, AgentPlanContractVersions.NodeV1, StringComparison.Ordinal) ||
                string.IsNullOrWhiteSpace(node.NodeId) ||
                !nodeIds.Add(node.NodeId) ||
                !AgentPlanContractSchemaAuthority.AllowedNodeKinds.Contains(node.NodeKind) ||
                !string.Equals(node.InputSchemaRef, "node-input:v1", StringComparison.Ordinal) ||
                string.IsNullOrWhiteSpace(node.OutputSchemaRef) ||
                !node.OutputSchemaRef.StartsWith("evidence:", StringComparison.Ordinal) ||
                !node.OutputSchemaRef.EndsWith(":v1", StringComparison.Ordinal))
            {
                return InvalidResult($"Node at index {index} violates the active Node v1 contract.");
            }

            var cloudReadonlyIntent = node.Input.SemanticIntent is { } semanticIntent &&
                                      cloudIntentsByCode.TryGetValue(semanticIntent, out var matchedCloudIntent)
                ? matchedCloudIntent
                : null;

            if (node.Input.RequestedScope is null ||
                node.Input.BusinessDomains is null ||
                !IsCanonicalOrder(node.Input.RequestedScope) ||
                !IsCanonicalOrder(node.Input.BusinessDomains) ||
                node.Input.TimeRange is { } inputRange &&
                    (string.IsNullOrWhiteSpace(inputRange.TimeZone) ||
                     inputRange.FromUtc is null && inputRange.ToUtc is null ||
                     inputRange.FromUtc > inputRange.ToUtc) ||
                node.TimeoutPolicy.TimeoutSeconds is < 1 or > 3_600 ||
                !string.Equals(node.TimeoutPolicy.PolicyVersion, "timeout-policy:v1", StringComparison.Ordinal) ||
                node.RetryPolicy.MaxAttempts is < 1 or > 5 ||
                !string.Equals(node.RetryPolicy.PolicyVersion, "retry-policy:v1", StringComparison.Ordinal) ||
                node.RetryPolicy.BackoffClass is not ("None" or "Fixed" or "Exponential") ||
                node.Budget.MaxToolCalls is < 0 or > 5 ||
                node.Budget.MaxToolCalls < node.RetryPolicy.MaxAttempts ||
                node.Budget.MaxModelCalls < 0 ||
                node.Budget.MaxInputTokens < 0 ||
                node.Budget.MaxOutputTokens < 0 ||
                node.Budget.MaxRows < 0 ||
                node.Budget.MaxCostAmount < 0 ||
                node.Budget.MaxCostAmount > planBudgets.MaxCostAmount ||
                node.Budget.MaxArtifactCount < 0 ||
                node.Budget.MaxArtifactBytes < 0 ||
                string.IsNullOrWhiteSpace(node.ApprovalPolicy.PolicyCode) ||
                !string.Equals(node.IdempotencyPolicy.PolicyVersion, "idempotency-policy:v1", StringComparison.Ordinal) ||
                node.IdempotencyPolicy.Mode is not ("Deterministic" or "ReadOnly" or "Fenced") ||
                node.SideEffectClass is not ("ReadOnly" or "DeterministicInternal" or "ArtifactDraftOnly"))
            {
                return InvalidResult($"Node '{node.NodeId}' has an invalid input/policy/range/side-effect contract.");
            }

            var maxNodeArtifactCount = node.NodeKind == "ApprovalCheckpointNode"
                ? planBudgets.MaxArtifactCount
                : AgentPlanContractVersions.DefaultNodeMaxArtifactCount;
            var maxNodeArtifactBytes = node.NodeKind == "ApprovalCheckpointNode"
                ? planBudgets.MaxArtifactBytes
                : AgentPlanContractVersions.DefaultNodeMaxArtifactBytes;
            if ((node.SideEffectClass is "ReadOnly" or "DeterministicInternal") &&
                (node.Budget.MaxArtifactCount != 0 || node.Budget.MaxArtifactBytes != 0) ||
                node.SideEffectClass == "ArtifactDraftOnly" &&
                (node.Budget.MaxArtifactCount < 1 || node.Budget.MaxArtifactCount > maxNodeArtifactCount ||
                 node.Budget.MaxArtifactBytes < 1 || node.Budget.MaxArtifactBytes > maxNodeArtifactBytes))
            {
                return InvalidResult($"Node '{node.NodeId}' artifact budget does not match its side-effect class.");
            }

            totalToolCalls += node.Budget.MaxToolCalls;
            totalModelCalls += node.Budget.MaxModelCalls;
            totalInputTokens += node.Budget.MaxInputTokens;
            totalOutputTokens += node.Budget.MaxOutputTokens;
            totalRetries += node.RetryPolicy.MaxAttempts - 1;
            totalArtifactCount += node.Budget.MaxArtifactCount;
            totalArtifactBytes += node.Budget.MaxArtifactBytes;
            totalCostAmount += node.Budget.MaxCostAmount;

            if (ContainsForbiddenExecutionSemantic(node.NodeKind) ||
                node.RequestedToolCodes.Any(ContainsForbiddenExecutionSemantic) ||
                node.RequestedCapabilityCodes.Any(ContainsForbiddenExecutionSemantic) ||
                ContainsForbiddenExecutionSemantic(node.Input.SemanticIntent) ||
                ContainsForbiddenExecutionSemantic(node.Input.TypedProvider) ||
                ContainsForbiddenExecutionSemantic(node.Input.ExecutionMode) ||
                ContainsForbiddenExecutionSemantic(node.Input.RequestedPermission) ||
                node.Input.RequestedScope.Any(ContainsForbiddenExecutionSemantic) ||
                node.Input.BusinessDomains.Any(ContainsForbiddenExecutionSemantic))
            {
                return InvalidResult($"Node '{node.NodeId}' requests Cloud mutation or PLC/control execution.");
            }

            var expectedDependencies = index == 0
                ? Array.Empty<string>()
                : [orderedNodes[index - 1].NodeId];
            if (topologyProfile == "LinearV1" &&
                (!node.Required ||
                 node.JoinPolicy is not null ||
                 !node.DependsOn.SequenceEqual(expectedDependencies, StringComparer.Ordinal)))
            {
                return InvalidResult($"Node '{node.NodeId}' is not a strict required immediate-predecessor LinearV1 node.");
            }

            if (topologyProfile == "DagV1" &&
                (node.DependsOn.Contains(node.NodeId, StringComparer.Ordinal) ||
                 node.DependsOn.Any(dependency => !orderedNodes.Take(index).Any(parent =>
                     string.Equals(parent.NodeId, dependency, StringComparison.Ordinal))) ||
                 node.DependsOn.Count <= 1 && node.JoinPolicy is not null ||
                 node.DependsOn.Count > 1 && node.JoinPolicy is not ("AllRequired" or "OptionalBestEffort") ||
                 node.NodeKind == "JoinNode" &&
                 (node.DependsOn.Count < 2 || node.JoinPolicy is not ("AllRequired" or "OptionalBestEffort")) ||
                 node.NodeKind != "JoinNode" && node.RequestedToolCodes.Contains("join_evidence", StringComparer.Ordinal)))
            {
                return InvalidResult($"Node '{node.NodeId}' violates bounded DagV1 dependency/join semantics.");
            }

            if (!node.EvidenceSelectors.SequenceEqual(node.DependsOn, StringComparer.Ordinal))
            {
                return InvalidResult($"Node '{node.NodeId}' Evidence selectors must exactly match its dependencies.");
            }

            var sets = new[]
            {
                ValidateCanonicalStringSet(node.RequestedToolCodes, $"{node.NodeId}.requestedToolCodes"),
                ValidateCanonicalStringSet(node.RequestedCapabilityCodes, $"{node.NodeId}.requestedCapabilityCodes"),
                ValidateCanonicalStringSet(node.DependsOn, $"{node.NodeId}.dependsOn"),
                ValidateCanonicalGuidSet(node.DataScopes, $"{node.NodeId}.dataScopes"),
                ValidateCanonicalGuidSet(node.KnowledgeScopes, $"{node.NodeId}.knowledgeScopes"),
                ValidateCanonicalStringSet(node.EvidenceSelectors, $"{node.NodeId}.evidenceSelectors")
            };
            var failedSet = sets.FirstOrDefault(result => !result.IsSuccess);
            if (failedSet is not null)
            {
                return failedSet;
            }

            if (node.RequestedCapabilityCodes.Any(code =>
                    !topCapabilitySet.Contains(code) || !availableCandidateCodes.Contains(code)) ||
                node.DataScopes.Any(scope => !allowedDataScopes.Contains(scope)) ||
                node.KnowledgeScopes.Any(scope => !allowedKnowledgeScopes.Contains(scope)))
            {
                return InvalidResult($"Node '{node.NodeId}' expands capability/data/knowledge scope beyond the frozen candidate allowlist.");
            }

            if (node.RequestedToolCodes.Any(toolCode =>
                    !node.RequestedCapabilityCodes.Any(capabilityCode =>
                        availableCandidatesByCode.TryGetValue(capabilityCode, out var candidate) &&
                        IsToolAuthorizedByCandidate(candidate, toolCode))))
            {
                return InvalidResult(
                    $"Node '{node.NodeId}' requests a tool outside the strict intersection of its requested capability codes and frozen tool allowlists.");
            }

            if (node.Input.CanonicalInputJson is { } inputJson)
            {
                var inputResult = ValidateCanonicalNodeInput(inputJson, node.NodeId);
                if (!inputResult.IsSuccess)
                {
                    return inputResult;
                }
            }

            if (node.NodeKind == "CloudReadNode" &&
                (!string.Equals(node.Input.TypedProvider, "CloudAiRead", StringComparison.Ordinal) ||
                 string.IsNullOrWhiteSpace(node.Input.SemanticIntent) ||
                 cloudReadonlyIntent is null ||
                 !string.Equals(node.Input.SemanticIntent, cloudReadonlyIntent.Intent, StringComparison.Ordinal) ||
                 !node.RequestedCapabilityCodes.Contains(node.Input.SemanticIntent, StringComparer.Ordinal) ||
                 !IsSha256(node.Input.SemanticPlanDigest) ||
                 !string.Equals(node.Input.SemanticPlanDigest, cloudReadonlyIntent.SemanticPlanDigest, StringComparison.Ordinal) ||
                 node.Input.RequestedScope.Count == 0 ||
                 !node.Input.RequestedScope.SequenceEqual(cloudReadonlyIntent.QueryScope, StringComparer.Ordinal) ||
                 !MatchesTimeRange(node.Input.TimeRange, cloudReadonlyIntent.TimeRange) ||
                 !CloudAiReadRowLimitPolicy.IsWithinBounds(node.Input.MaxRows ?? 0) ||
                 node.Input.MaxRows != cloudReadonlyIntent.Limit ||
                 !string.IsNullOrWhiteSpace(node.Input.ExecutionMode) ||
                 node.Input.DataSourceId is not null ||
                 node.Input.BusinessDomains.Count != 0 ||
                 node.Input.CanonicalInputJson is not null ||
                 !string.Equals(node.SideEffectClass, "ReadOnly", StringComparison.Ordinal)))
            {
                return InvalidResult($"CloudReadNode '{node.NodeId}' must use the typed CloudAiRead input without SQL/data-source fallback.");
            }

            if (node.RequestedToolCodes.Contains("assess_cloud_health", StringComparer.Ordinal) &&
                (node.NodeKind != "DeterministicComputeNode" ||
                 node.RequestedToolCodes.Count != 1 ||
                 !string.Equals(node.OutputSchemaRef, "evidence:assess_cloud_health:v1", StringComparison.Ordinal) ||
                 node.DependsOn.Count != 1 ||
                 orderedNodes.Take(index).SingleOrDefault(parent =>
                     string.Equals(parent.NodeId, node.DependsOn.Single(), StringComparison.Ordinal)) is not { NodeKind: "CloudReadNode" } parentNode ||
                 !parentNode.RequestedToolCodes.Contains("query_cloud_data_readonly", StringComparer.Ordinal) ||
                 cloudReadonlyIntent is null ||
                 !string.Equals(cloudReadonlyIntent.Intent, "Analysis.Device.Status", StringComparison.Ordinal) ||
                 !string.Equals(parentNode.Input.SemanticIntent, cloudReadonlyIntent.Intent, StringComparison.Ordinal) ||
                 !string.Equals(parentNode.Input.SemanticPlanDigest, cloudReadonlyIntent.SemanticPlanDigest, StringComparison.Ordinal) ||
                 !string.Equals(node.Input.TypedProvider, "DeterministicHealthAssessment", StringComparison.Ordinal) ||
                 !string.Equals(node.Input.SemanticIntent, cloudReadonlyIntent.Intent, StringComparison.Ordinal) ||
                 !string.Equals(node.Input.SemanticPlanDigest, cloudReadonlyIntent.SemanticPlanDigest, StringComparison.Ordinal) ||
                 !node.Input.RequestedScope.SequenceEqual(cloudReadonlyIntent.QueryScope, StringComparer.Ordinal) ||
                 node.Input.MaxRows != cloudReadonlyIntent.Limit ||
                 !MatchesTimeRange(node.Input.TimeRange, cloudReadonlyIntent.TimeRange) ||
                 node.Input.ExecutionMode is not null ||
                 node.Input.DataSourceId is not null ||
                 node.Input.BusinessDomains.Count != 0 ||
                 node.Input.GovernedSchemaDigest is not null ||
                 node.Input.RequestedPermission is not null ||
                 node.Input.CanonicalInputJson is not null ||
                 node.SideEffectClass != "DeterministicInternal" ||
                 node.ApprovalPolicy.Required ||
                 node.ModelPolicy is not null ||
                 node.Budget.MaxModelCalls != 0 ||
                 node.Budget.MaxRows != cloudReadonlyIntent.Limit ||
                 node.Budget.MaxArtifactCount != 0 ||
                 node.Budget.MaxArtifactBytes != 0 ||
                 node.IdempotencyPolicy.Mode != "Deterministic"))
            {
                return InvalidResult(
                    $"Deterministic health node '{node.NodeId}' must consume exactly one Device.Status Cloud Evidence input under the frozen algorithm contract.");
            }

            if (node.NodeKind != "CloudReadNode" &&
                !node.RequestedToolCodes.Contains("assess_cloud_health", StringComparer.Ordinal) &&
                node.Input.TimeRange is not null)
            {
                return InvalidResult($"Node '{node.NodeId}' cannot carry a Cloud Evidence time range.");
            }

            if (node.NodeKind == "GovernedDataReadNode" &&
                (node.Input.DataSourceId is null ||
                 node.Input.ExecutionMode is not ("GovernedSql" or "TextToSql") ||
                 !string.IsNullOrWhiteSpace(node.Input.TypedProvider) ||
                 !IsSha256(node.Input.GovernedSchemaDigest) ||
                 string.IsNullOrWhiteSpace(node.Input.RequestedPermission) ||
                 node.Input.MaxRows is < 1 or > 200 ||
                 !string.Equals(node.SideEffectClass, "ReadOnly", StringComparison.Ordinal)))
            {
                return InvalidResult($"GovernedDataReadNode '{node.NodeId}' must use its distinct governed data input contract.");
            }

            if (node.NodeKind == "JoinNode" &&
                (node.RequestedToolCodes.Count != 1 ||
                 !node.RequestedToolCodes.Contains("join_evidence", StringComparer.Ordinal) ||
                 node.SideEffectClass != "DeterministicInternal" ||
                 node.ApprovalPolicy.Required ||
                 node.ModelPolicy is not null))
            {
                return InvalidResult($"JoinNode '{node.NodeId}' must use the deterministic join_evidence executor without approval or model access.");
            }

            if (node.NodeKind == "AgentReasoningNode" &&
                (!AgentReasoningPolicyAuthority.Matches(node.ModelPolicy) ||
                 node.ModelPolicy!.ModelId != snapshotModelId ||
                 !string.Equals(
                     node.ModelPolicy.ModelParametersHash,
                     snapshotModelParametersHash,
                     StringComparison.Ordinal) ||
                 node.DependsOn.Count < 2 ||
                 node.JoinPolicy is not ("AllRequired" or "OptionalBestEffort") ||
                 node.RequestedToolCodes.Count != 1 ||
                 !node.RequestedToolCodes.Contains("agent_reasoning", StringComparer.Ordinal) ||
                 !string.Equals(node.OutputSchemaRef, "evidence:agent_reasoning:v1", StringComparison.Ordinal) ||
                 node.SideEffectClass != "ReadOnly" ||
                 node.ApprovalPolicy.Required ||
                 node.RetryPolicy.MaxAttempts != 1 ||
                 node.RetryPolicy.BackoffClass != "None" ||
                 node.Budget.MaxToolCalls != 1 ||
                 node.Budget.MaxModelCalls != AgentReasoningPolicyAuthority.MaxTurns + AgentReasoningPolicyAuthority.RecoveryTurns ||
                 node.Budget.MaxInputTokens != AgentReasoningPolicyAuthority.MaxInputTokens ||
                 node.Budget.MaxOutputTokens != AgentReasoningPolicyAuthority.MaxOutputTokens ||
                 node.Budget.MaxCostAmount != AgentReasoningPolicyAuthority.MaxCostAmount ||
                 node.Budget.MaxArtifactCount != 0 ||
                 node.Budget.MaxArtifactBytes != 0 ||
                 node.IdempotencyPolicy.Mode != "ReadOnly"))
            {
                return InvalidResult(
                    $"AgentReasoningNode '{node.NodeId}' violates the depth-1 evidence-only model policy or independent budget.");
            }

            if (node.NodeKind != "AgentReasoningNode" && node.ModelPolicy is not null)
            {
                return InvalidResult($"Non-reasoning node '{node.NodeId}' cannot carry a model policy.");
            }


            var step = orderedSteps[index];
            if (step is null ||
                !IsExactBoundedText(step.Title, AgentPlanContractVersions.MaxStepTitleCharacters) ||
                !IsExactBoundedText(step.Description, AgentPlanContractVersions.MaxStepDescriptionCharacters) ||
                !Enum.IsDefined(step.StepType) ||
                !IsExactBoundedText(step.ToolCode, AgentPlanContractVersions.MaxStepToolCodeCharacters) ||
                !node.RequestedToolCodes.Contains(step.ToolCode, StringComparer.Ordinal))
            {
                return InvalidResult($"Runtime Step at index {index} does not match its frozen Node/tool contract.");
            }
        }

        var topology = ValidateTopology(orderedNodes, topologyProfile, concurrencyPolicy.MaxParallelism);
        if (!topology.IsSuccess)
        {
            return topology;
        }

        if (totalToolCalls > planBudgets.MaxToolCalls ||
            totalModelCalls > planBudgets.MaxModelCalls ||
            totalInputTokens > planBudgets.MaxInputTokens ||
            totalOutputTokens > planBudgets.MaxOutputTokens ||
            totalRetries > planBudgets.MaxRetries ||
            totalArtifactCount > planBudgets.MaxArtifactCount ||
            totalArtifactBytes > planBudgets.MaxArtifactBytes ||
            totalCostAmount > planBudgets.MaxCostAmount)
        {
            return InvalidResult("Node upper-bound budgets exceed the immutable task budget.");
        }

        var missingRequiredProducer = orderedNodes.Length == 0
            ? null
            : candidates
                .Where(candidate => candidate.Required.Value &&
                                    candidate.Availability == AgentIntentAvailability.Available)
                .FirstOrDefault(candidate => !orderedNodes.Any(node =>
                    node.RequestedCapabilityCodes.Contains(candidate.IntentCode, StringComparer.Ordinal)));
        if (missingRequiredProducer is not null)
        {
            return InvalidResult(
                $"Required available IntentCandidate '{missingRequiredProducer.IntentCode}' has no producer Node.");
        }

        foreach (var step in orderedSteps)
        {
            if (step is null ||
                !IsExactBoundedText(step.Title, AgentPlanContractVersions.MaxStepTitleCharacters) ||
                !IsExactBoundedText(step.Description, AgentPlanContractVersions.MaxStepDescriptionCharacters) ||
                !Enum.IsDefined(step.StepType) ||
                !IsExactBoundedText(step.ToolCode, AgentPlanContractVersions.MaxStepToolCodeCharacters) ||
                ContainsForbiddenExecutionSemantic(step.ToolCode))
            {
                return InvalidResult("Runtime Steps contain an invalid enum/tool or Cloud-write/PLC semantic.");
            }

            if (step.InputJson is { } inputJson)
            {
                var inputResult = ValidateCanonicalNodeInput(inputJson, step.Title);
                if (!inputResult.IsSuccess)
                {
                    return inputResult;
                }
            }
        }

        return Result.Success();
    }

    private static Result ValidateTopology(
        IReadOnlyList<AgentPlanNodeDocument> nodes,
        string topologyProfile,
        int maxParallelism)
    {
        if (nodes.Count == 0)
        {
            return Result.Success();
        }

        var byId = nodes.ToDictionary(node => node.NodeId, StringComparer.Ordinal);
        var visiting = new HashSet<string>(StringComparer.Ordinal);
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var levels = new Dictionary<string, int>(StringComparer.Ordinal);

        Result Visit(AgentPlanNodeDocument node)
        {
            if (visited.Contains(node.NodeId))
            {
                return Result.Success();
            }

            if (!visiting.Add(node.NodeId))
            {
                return InvalidResult($"DagV1 contains a dependency cycle at node '{node.NodeId}'.");
            }

            var level = 0;
            foreach (var dependencyId in node.DependsOn)
            {
                if (!byId.TryGetValue(dependencyId, out var dependency))
                {
                    return InvalidResult($"Node '{node.NodeId}' references unknown dependency '{dependencyId}'.");
                }

                var dependencyResult = Visit(dependency);
                if (!dependencyResult.IsSuccess)
                {
                    return dependencyResult;
                }

                level = Math.Max(level, levels[dependencyId] + 1);
            }

            visiting.Remove(node.NodeId);
            visited.Add(node.NodeId);
            levels[node.NodeId] = level;
            return Result.Success();
        }

        foreach (var node in nodes)
        {
            var result = Visit(node);
            if (!result.IsSuccess)
            {
                return result;
            }
        }

        if (topologyProfile == "LinearV1")
        {
            return Result.Success();
        }

        var childCounts = nodes
            .SelectMany(node => node.DependsOn)
            .GroupBy(nodeId => nodeId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        var hasDagShape = nodes.Count(node => node.DependsOn.Count == 0) > 1 ||
                          nodes.Any(node => node.DependsOn.Count > 1 || !node.Required) ||
                          childCounts.Values.Any(count => count > 1);
        if (!hasDagShape)
        {
            return InvalidResult("DagV1 must contain an explicit branch, join, parallel root, or optional node; LinearV1 is not silently relabelled.");
        }

        var maximumLevelWidth = levels.Values
            .GroupBy(level => level)
            .Select(group => group.Count())
            .DefaultIfEmpty(1)
            .Max();
        if (maximumLevelWidth > maxParallelism)
        {
            return InvalidResult(
                $"DagV1 runnable width {maximumLevelWidth} exceeds bounded maxParallelism {maxParallelism}.");
        }

        foreach (var join in nodes.Where(node => node.DependsOn.Count > 1))
        {
            var dependencies = join.DependsOn.Select(dependencyId => byId[dependencyId]).ToArray();
            if (!dependencies.Any(dependency => dependency.Required))
            {
                return InvalidResult($"Join node '{join.NodeId}' must retain at least one required Evidence dependency.");
            }
        }

        return Result.Success();
    }

    private static bool MatchesTimeRange(
        AgentIntentTimeRangeDocument? nodeRange,
        AgentTaskPlanSemanticTimeRangeDocument? planRange)
    {
        if (nodeRange is null || planRange is null)
        {
            return nodeRange is null && planRange is null;
        }

        return nodeRange.FromUtc == planRange.Start &&
               nodeRange.ToUtc == planRange.End &&
               string.Equals(nodeRange.TimeZone, planRange.TimeZone, StringComparison.Ordinal);
    }

    private static bool IsDataProducerNode(AgentPlanNodeDocument node) =>
        node.NodeKind is "CloudReadNode" or "GovernedDataReadNode";

    private static bool IsExactBoundedText(string? value, int maximumCharacters)
    {
        return value is { Length: > 0 } &&
               value.Length <= maximumCharacters &&
               !string.IsNullOrWhiteSpace(value) &&
               string.Equals(value, value.Trim(), StringComparison.Ordinal);
    }

    private static Result ValidateCanonicalNodeInput(string inputJson, string owner)
    {
        var validation = AgentNodeToolInputContractV1.Normalize(inputJson);
        if (!validation.IsValid)
        {
            return InvalidResult($"Node/tool input '{owner}' is invalid: {validation.Error}");
        }

        if (!string.Equals(inputJson, validation.CanonicalJson, StringComparison.Ordinal))
        {
            return InvalidResult($"Node/tool input '{owner}' is not canonical JSON.");
        }

        using var document = JsonDocument.Parse(validation.CanonicalJson!);
        return ContainsForbiddenJsonSemantic(document.RootElement)
            ? InvalidResult($"Node/tool input '{owner}' requests Cloud mutation or PLC/control execution.")
            : Result.Success();
    }

    private static bool ContainsForbiddenJsonSemantic(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                return element.EnumerateObject().Any(property =>
                    ContainsForbiddenExecutionSemantic(property.Name) ||
                    ContainsForbiddenJsonSemantic(property.Value));
            case JsonValueKind.Array:
                return element.EnumerateArray().Any(ContainsForbiddenJsonSemantic);
            case JsonValueKind.String:
                return ContainsForbiddenExecutionSemantic(element.GetString());
            default:
                return false;
        }
    }

    private static Result<AgentTaskPlanDocument> DeserializeStrict(string canonicalJson)
    {
        try
        {
            var plan = JsonSerializer.Deserialize<AgentTaskPlanDocument>(canonicalJson, CanonicalJson.SerializerOptions);
            return plan is null
                ? Invalid<AgentTaskPlanDocument>("Plan v2 JSON deserialized to null.")
                : Result.Success(plan);
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException)
        {
            return Invalid<AgentTaskPlanDocument>("Plan v2 JSON does not match the strict frozen schema.");
        }
    }

    private static bool ContainsForbiddenExecutionSemantic(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var normalized = value.Trim().ToLowerInvariant();
        return FrozenForbiddenExecutionSemanticTokens.Any(
            token => normalized.Contains(token, StringComparison.Ordinal));
    }

    private static bool IsSha256(string? value)
    {
        return value is { Length: 64 } && value.All(character =>
            character is >= '0' and <= '9' or >= 'a' and <= 'f');
    }

    private static bool IsCanonicalGoalSummary(string taskType, string? value)
    {
        var prefix = $"{taskType} task; goalSha256=";
        return value is not null &&
               value.StartsWith(prefix, StringComparison.Ordinal) &&
               value.Length == prefix.Length + 64 &&
               IsSha256(value[prefix.Length..]);
    }

    private static bool HasAnyNull(params object?[] values)
    {
        return values.Any(value => value is null);
    }

    private static bool AllRequiredFieldsPresent(params object?[] values)
    {
        return !HasAnyNull(values);
    }

    private static Result ValidateCanonicalStringSet(IReadOnlyCollection<string>? values, string name)
    {
        if (values is null || values.Any(string.IsNullOrWhiteSpace) || !IsCanonicalOrder(values))
        {
            return InvalidResult($"{name} must be a non-null, duplicate-free ordinal canonical string array.");
        }

        return Result.Success();
    }

    private static Result ValidateCanonicalGuidSet(IReadOnlyCollection<Guid>? values, string name)
    {
        if (values is null || values.Any(value => value == Guid.Empty) ||
            values.Select(value => value.ToString("D")).Distinct(StringComparer.Ordinal).Count() != values.Count ||
            !values.SequenceEqual(values.OrderBy(value => value.ToString("D"), StringComparer.Ordinal)))
        {
            return InvalidResult($"{name} must be a non-null, duplicate-free ordinal canonical stable-id array.");
        }

        return Result.Success();
    }

    private static bool IsCanonicalOrder(IEnumerable<string>? values)
    {
        if (values is null)
        {
            return false;
        }

        var array = values.ToArray();
        return array.Distinct(StringComparer.Ordinal).Count() == array.Length &&
               array.SequenceEqual(array.OrderBy(value => value, StringComparer.Ordinal), StringComparer.Ordinal);
    }

    private static Result<CanonicalAgentPlan> PayloadTooLarge()
    {
        return Result.Failure(new ApiProblemDescriptor(
            AppProblemCodes.PlanPayloadTooLarge,
            $"Plan v2 canonical payload exceeds the maximum of {AgentPlanContractVersions.MaxPlanCanonicalBytes} UTF-8 bytes."));
    }

    private static Result<CanonicalAgentPlan> Invalid(string detail) => Invalid<CanonicalAgentPlan>(detail);

    private static Result<AgentPlanContractMetadata> InvalidMetadata(string detail) => Invalid<AgentPlanContractMetadata>(detail);

    private static Result InvalidResult(string detail)
    {
        return Result.Failure(new ApiProblemDescriptor(AppProblemCodes.AgentPlanInvalid, detail));
    }

    private static Result<T> Invalid<T>(string detail)
    {
        return Result.Failure(new ApiProblemDescriptor(AppProblemCodes.AgentPlanInvalid, detail));
    }
}

internal sealed record CanonicalEvidenceEnvelope(
    AgentEvidenceEnvelopeDocument Document,
    string CanonicalJson,
    string Digest,
    int CanonicalByteCount);

internal static class AgentEvidenceCanonicalizer
{
    private static readonly IReadOnlySet<string> DigestExcludedRootProperties = new HashSet<string>(
        ["digest"],
        StringComparer.Ordinal);

    public static Result<CanonicalEvidenceEnvelope> Seal(AgentEvidenceEnvelopeDocument evidence)
    {
        if (evidence is null ||
            evidence.Producer is null ||
            evidence.Source is null ||
            evidence.Quality is null ||
            evidence.Payload is null ||
            evidence.Content is null ||
            evidence.Lineage is null ||
            evidence.Governance is null ||
            !string.Equals(evidence.SchemaVersion, AgentPlanContractVersions.EvidenceV1, StringComparison.Ordinal) ||
            evidence.EvidenceId == Guid.Empty ||
            evidence.UserId == Guid.Empty ||
            evidence.SessionId == Guid.Empty ||
            evidence.TaskId == Guid.Empty ||
            string.IsNullOrWhiteSpace(evidence.NodeId) ||
            evidence.CreatedAtUtc == default)
        {
            return Result.Failure(new ApiProblemDescriptor(
                AppProblemCodes.AgentPlanInvalid,
                "Evidence v1 identity and envelope fields are required."));
        }

        try
        {
            var evidenceKinds = new HashSet<string>(
                [
                    "DataQuery", "RagCitation", "UploadedFile", "DerivedMetric",
                    "ModelPrediction", "LlmInference", "PolicyDecision", "ArtifactReference"
                ],
                StringComparer.Ordinal);
            var truthClasses = new HashSet<string>(
                ["ObservedFact", "DerivedFact", "ModelPrediction", "LlmInference", "Recommendation"],
                StringComparer.Ordinal);
            if (!evidenceKinds.Contains(evidence.EvidenceKind) ||
                !truthClasses.Contains(evidence.TruthClass) ||
                string.IsNullOrWhiteSpace(evidence.Producer.NodeKind) ||
                string.IsNullOrWhiteSpace(evidence.Producer.ExecutorId) ||
                string.IsNullOrWhiteSpace(evidence.Source.SourceDomain) ||
                string.IsNullOrWhiteSpace(evidence.Source.OpaqueSourceRef) ||
                string.IsNullOrWhiteSpace(evidence.Source.SourceMode) ||
                evidence.Source.SanitizedScope is null ||
                evidence.Source.QueryScope is null ||
                evidence.Quality.QualityFlags is null ||
                evidence.Content.TypedMetrics is null ||
                evidence.Content.Findings is null ||
                evidence.Content.CitationRefs is null ||
                evidence.Content.ArtifactRefs is null ||
                evidence.Lineage.ParentEvidenceIds is null ||
                evidence.Governance.AllowedConsumerScope is null ||
                string.IsNullOrWhiteSpace(evidence.Content.SafeSummary) ||
                string.IsNullOrWhiteSpace(evidence.Quality.Freshness) ||
                evidence.Quality.RowCount is < 0 ||
                evidence.Quality.MissingRate is { } missingRate &&
                    (double.IsNaN(missingRate) || double.IsInfinity(missingRate) || missingRate is < 0 or > 1) ||
                evidence.Quality.Confidence is { } confidence &&
                    (double.IsNaN(confidence) || double.IsInfinity(confidence) || confidence is < 0 or > 1))
            {
                return InvalidEvidence("Evidence v1 nested contracts and collections must be explicit and non-null.");
            }

            if (!IsCanonicalStringSet(evidence.Source.SanitizedScope) ||
                !IsCanonicalStringSet(evidence.Source.QueryScope) ||
                !IsCanonicalStringSet(evidence.Quality.QualityFlags) ||
                !IsCanonicalStringSet(evidence.Content.CitationRefs) ||
                !IsCanonicalStringSet(evidence.Content.ArtifactRefs) ||
                !IsCanonicalGuidSet(evidence.Lineage.ParentEvidenceIds) ||
                !IsCanonicalStringSet(evidence.Governance.AllowedConsumerScope) ||
                evidence.Content.Findings.Any(string.IsNullOrWhiteSpace) ||
                evidence.Content.TypedMetrics.Keys.Any(string.IsNullOrWhiteSpace))
            {
                return InvalidEvidence(
                    "Evidence set-like scopes/flags/references/lineage must be canonical; findings remain an ordered sequence.");
            }

            if (!IsSha256(evidence.Lineage.InputDigest) ||
                !IsSha256(evidence.Lineage.OutputDigest) ||
                evidence.Lineage.EvidenceSetDigest is not null &&
                    !IsSha256(evidence.Lineage.EvidenceSetDigest) ||
                evidence.Lineage.ParentEvidenceIds.Count == 0 &&
                    evidence.Lineage.EvidenceSetDigest is not null ||
                string.IsNullOrWhiteSpace(evidence.Governance.Sensitivity) ||
                string.IsNullOrWhiteSpace(evidence.Governance.RedactionStatus) ||
                string.IsNullOrWhiteSpace(evidence.Governance.RetentionClass))
            {
                return InvalidEvidence("Evidence lineage and governance fields are incomplete.");
            }

            var expectedTruthClass = evidence.EvidenceKind switch
            {
                "DataQuery" or "RagCitation" or "UploadedFile" => "ObservedFact",
                "DerivedMetric" => "DerivedFact",
                "ModelPrediction" => "ModelPrediction",
                "LlmInference" => "LlmInference",
                "PolicyDecision" => "Recommendation",
                "ArtifactReference" => "ObservedFact",
                _ => null
            };
            if (!string.Equals(evidence.TruthClass, expectedTruthClass, StringComparison.Ordinal))
            {
                return InvalidEvidence("Evidence kind and truthClass do not match the frozen fact/prediction matrix.");
            }

            if (string.Equals(evidence.TruthClass, "DerivedFact", StringComparison.Ordinal) &&
                (evidence.Lineage.ParentEvidenceIds.Count == 0 ||
                 string.IsNullOrWhiteSpace(evidence.Producer.ToolCode) ||
                 !IsSha256(evidence.Producer.ToolSchemaHash)))
            {
                return InvalidEvidence(
                    "DerivedFact Evidence requires parent evidence and a versioned deterministic tool producer.");
            }

            if (evidence.EvidenceKind is not ("LlmInference" or "ModelPrediction") &&
                (evidence.Producer.ModelId is not null ||
                 evidence.Producer.ModelVersion is not null ||
                 evidence.Producer.PromptVersion is not null))
            {
                return InvalidEvidence(
                    "Non-model Evidence cannot claim model or Prompt provenance.");
            }

            if (string.Equals(evidence.EvidenceKind, "LlmInference", StringComparison.Ordinal) &&
                (!string.Equals(evidence.Producer.NodeKind, "AgentReasoningNode", StringComparison.Ordinal) ||
                 !evidence.Producer.ExecutorId.StartsWith("agent-child:", StringComparison.Ordinal) ||
                 evidence.Producer.ModelId is null ||
                 string.IsNullOrWhiteSpace(evidence.Producer.ModelVersion) ||
                 string.IsNullOrWhiteSpace(evidence.Producer.PromptVersion) ||
                 !string.Equals(evidence.Source.Provider, "ConfiguredModel", StringComparison.Ordinal) ||
                 !string.Equals(evidence.Source.ProviderOperationCode, "agent_reasoning", StringComparison.Ordinal) ||
                 evidence.Source.ObservedAtUtc is not null ||
                 evidence.Lineage.ParentEvidenceIds.Count == 0 ||
                 evidence.Content.CitationRefs.Count != evidence.Lineage.ParentEvidenceIds.Count ||
                 evidence.Prediction is not null))
            {
                return InvalidEvidence(
                    "LlmInference Evidence requires a depth-1 AgentReasoningNode producer, exact parent citations, and cannot claim observation or prediction authority.");
            }

            if (evidence.Source.TimeRange is { } sourceRange &&
                (string.IsNullOrWhiteSpace(sourceRange.TimeZone) ||
                 sourceRange.FromUtc is null && sourceRange.ToUtc is null ||
                 sourceRange.FromUtc > sourceRange.ToUtc))
            {
                return InvalidEvidence("Evidence source timeRange is invalid.");
            }

            if (string.Equals(evidence.Producer.NodeKind, "CloudReadNode", StringComparison.Ordinal))
            {
                if (!string.Equals(evidence.Source.Provider, "CloudAiRead", StringComparison.Ordinal) ||
                    string.IsNullOrWhiteSpace(evidence.Source.ProviderOperationCode) ||
                    !evidence.Source.ProviderOperationCode.StartsWith("CloudAiRead.", StringComparison.Ordinal) ||
                    string.IsNullOrWhiteSpace(evidence.Source.SemanticIntent) ||
                    !AgentIntentRegistryV1.TryGetDescriptor(evidence.Source.SemanticIntent, out var descriptor) ||
                    descriptor.IntentClass != AgentIntentClass.CloudOnly ||
                    descriptor.Availability != AgentIntentAvailability.Available ||
                    !string.Equals(
                        evidence.Source.ProviderOperationCode,
                        $"CloudAiRead.{evidence.Source.SemanticIntent["Analysis.".Length..]}",
                        StringComparison.Ordinal) ||
                    evidence.Source.QueryScope.Count == 0)
                {
                    return InvalidEvidence(
                        "CloudReadNode Evidence requires CloudAiRead provider, logical operation code, semantic intent, and canonical query scope.");
                }
            }

            var isHealthAssessment =
                string.Equals(evidence.Producer.ToolCode, "assess_cloud_health", StringComparison.Ordinal) ||
                string.Equals(evidence.Source.Provider, "DeterministicHealthAlgorithm", StringComparison.Ordinal) ||
                string.Equals(evidence.Source.ProviderOperationCode, "assess_cloud_health", StringComparison.Ordinal);
            if (isHealthAssessment &&
                (!string.Equals(evidence.EvidenceKind, "DerivedMetric", StringComparison.Ordinal) ||
                 !string.Equals(evidence.TruthClass, "DerivedFact", StringComparison.Ordinal) ||
                 !string.Equals(evidence.Producer.NodeKind, "DeterministicComputeNode", StringComparison.Ordinal) ||
                 !string.Equals(
                     evidence.Producer.ExecutorId,
                     $"deterministic:{AgentCloudHealthAssessmentTool.AlgorithmVersion}",
                     StringComparison.Ordinal) ||
                 !string.Equals(evidence.Producer.ToolCode, "assess_cloud_health", StringComparison.Ordinal) ||
                 evidence.Producer.ModelId is not null ||
                 evidence.Producer.ModelVersion is not null ||
                 evidence.Producer.PromptVersion is not null ||
                 !string.Equals(evidence.Source.Provider, "DeterministicHealthAlgorithm", StringComparison.Ordinal) ||
                 !string.Equals(evidence.Source.ProviderOperationCode, "assess_cloud_health", StringComparison.Ordinal) ||
                 !string.Equals(evidence.Source.SemanticIntent, "Analysis.Device.Status", StringComparison.Ordinal) ||
                 evidence.Source.ObservedAtUtc is not null ||
                 evidence.Source.AsOfUtc is null ||
                 evidence.Lineage.ParentEvidenceIds.Count != 1 ||
                 evidence.Quality.RowCount is null ||
                 evidence.Quality.MissingRate is null ||
                 evidence.Quality.Confidence is null ||
                 !evidence.Quality.QualityFlags.Contains("deterministic-health", StringComparer.Ordinal) ||
                 !evidence.Quality.QualityFlags.Contains(
                     AgentCloudHealthAssessmentTool.AlgorithmVersion,
                     StringComparer.Ordinal) ||
                 evidence.Prediction is not null))
            {
                return InvalidEvidence(
                    "Current health Evidence must be a one-parent replayable DerivedFact from the fixed deterministic algorithm and cannot claim prediction authority.");
            }

            if (string.Equals(evidence.EvidenceKind, "ModelPrediction", StringComparison.Ordinal))
            {
                if (evidence.Prediction is null ||
                    string.IsNullOrWhiteSpace(evidence.Prediction.ModelVersion) ||
                    string.IsNullOrWhiteSpace(evidence.Prediction.PredictionWindow) ||
                    evidence.Prediction.ValidUntilUtc <= evidence.CreatedAtUtc)
                {
                    return InvalidEvidence("ModelPrediction Evidence requires a complete, future-valid prediction contract.");
                }
            }
            else if (evidence.Prediction is not null)
            {
                return InvalidEvidence("Non-prediction Evidence must set prediction=null.");
            }

            if (string.Equals(evidence.Payload.StorageMode, "ArtifactReference", StringComparison.Ordinal) ||
                string.Equals(evidence.EvidenceKind, "ArtifactReference", StringComparison.Ordinal))
            {
                if (!string.Equals(evidence.Payload.StorageMode, "ArtifactReference", StringComparison.Ordinal) ||
                    !string.Equals(evidence.EvidenceKind, "ArtifactReference", StringComparison.Ordinal) ||
                    !string.Equals(
                        evidence.Payload.PolicyVersion,
                        AgentPlanContractVersions.ArtifactReferenceEvidencePolicyV1,
                        StringComparison.Ordinal) ||
                    evidence.Payload.InlineCanonicalJson is not null ||
                    string.IsNullOrWhiteSpace(evidence.Payload.PayloadRef) ||
                    !evidence.Payload.PayloadRef.StartsWith("artifact-fileset:", StringComparison.Ordinal) ||
                    evidence.Payload.PayloadRef.Length != "artifact-fileset:".Length + 32 ||
                    !Guid.TryParseExact(evidence.Payload.PayloadRef["artifact-fileset:".Length..], "N", out _) ||
                    evidence.Payload.ByteLength <= 0 ||
                    !IsSha256(evidence.Payload.Sha256) ||
                    !evidence.Payload.IsComplete ||
                    string.IsNullOrWhiteSpace(evidence.Payload.MediaType) ||
                    !string.Equals(evidence.Lineage.OutputDigest, evidence.Payload.Sha256, StringComparison.Ordinal))
                {
                    return InvalidEvidence(
                        "ArtifactReference Evidence requires a verified opaque file-set reference, complete digest metadata, and artifact-reference-evidence-policy:v1.");
                }
            }
            else if (string.Equals(evidence.Payload.StorageMode, "InlineCanonicalJson", StringComparison.Ordinal))
            {
                if (!string.Equals(
                        evidence.Payload.PolicyVersion,
                        AgentPlanContractVersions.InlineEvidencePolicyV1,
                        StringComparison.Ordinal) ||
                    string.IsNullOrWhiteSpace(evidence.Payload.InlineCanonicalJson))
                {
                    return InvalidEvidence(
                        "InlineCanonicalJson evidence requires inline-evidence-policy:v1 and a complete inline payload.");
                }

                var rawPayloadBytes = Encoding.UTF8.GetByteCount(evidence.Payload.InlineCanonicalJson);
                if (rawPayloadBytes > AgentPlanContractVersions.MaxInlineEvidenceCanonicalBytes)
                {
                    return EvidenceTooLarge(rawPayloadBytes);
                }

                var canonicalPayload = CanonicalJson.Canonicalize(
                    evidence.Payload.InlineCanonicalJson,
                    AgentPlanContractVersions.MaxInlineEvidenceCanonicalBytes);
                var payloadBytes = Encoding.UTF8.GetByteCount(canonicalPayload);
                if (payloadBytes > AgentPlanContractVersions.MaxInlineEvidenceCanonicalBytes)
                {
                    return EvidenceTooLarge(payloadBytes);
                }

                if (!string.Equals(canonicalPayload, evidence.Payload.InlineCanonicalJson, StringComparison.Ordinal) ||
                    evidence.Payload.ByteLength != payloadBytes ||
                    !string.Equals(evidence.Payload.Sha256, CanonicalJson.ComputeSha256(canonicalPayload), StringComparison.Ordinal) ||
                    !evidence.Payload.IsComplete ||
                    evidence.Payload.PayloadRef is not null ||
                    !string.Equals(evidence.Lineage.OutputDigest, evidence.Payload.Sha256, StringComparison.Ordinal) ||
                    string.IsNullOrWhiteSpace(evidence.Payload.MediaType))
                {
                    return InvalidEvidence("Inline evidence payload metadata does not match its canonical bytes.");
                }
            }
            else
            {
                return InvalidEvidence("Evidence storageMode is not supported by the frozen Evidence v1 policy.");
            }

            var unsigned = evidence with { Digest = string.Empty };
            var digestSource = CanonicalJson.Canonicalize(
                JsonSerializer.Serialize(unsigned, CanonicalJson.SerializerOptions),
                DigestExcludedRootProperties);
            var digest = CanonicalJson.ComputeSha256(digestSource);
            var sealedEvidence = evidence with { Digest = digest };
            var canonical = CanonicalJson.Serialize(sealedEvidence);
            return Result.Success(new CanonicalEvidenceEnvelope(
                sealedEvidence,
                canonical,
                digest,
                Encoding.UTF8.GetByteCount(canonical)));
        }
        catch (JsonException)
        {
            return InvalidEvidence("Evidence v1 contains invalid canonical JSON.");
        }
    }

    private static Result<CanonicalEvidenceEnvelope> InvalidEvidence(string detail)
    {
        return Result.Failure(new ApiProblemDescriptor(
            AppProblemCodes.AgentPlanInvalid,
            detail));
    }

    private static Result<CanonicalEvidenceEnvelope> EvidenceTooLarge(int byteCount)
    {
        return Result.Failure(new ApiProblemDescriptor(
            AppProblemCodes.EvidencePayloadTooLarge,
            $"Inline Evidence canonical payload is {byteCount} UTF-8 bytes; maximum is {AgentPlanContractVersions.MaxInlineEvidenceCanonicalBytes}."));
    }

    private static bool IsSha256(string? value)
    {
        return value is { Length: 64 } && value.All(character =>
            character is >= '0' and <= '9' or >= 'a' and <= 'f');
    }

    private static bool IsCanonicalStringSet(IReadOnlyCollection<string> values)
    {
        return values.All(value => !string.IsNullOrWhiteSpace(value)) &&
               values.Distinct(StringComparer.Ordinal).Count() == values.Count &&
               values.SequenceEqual(values.OrderBy(value => value, StringComparer.Ordinal), StringComparer.Ordinal);
    }

    private static bool IsCanonicalGuidSet(IReadOnlyCollection<Guid> values)
    {
        return values.All(value => value != Guid.Empty) &&
               values.Distinct().Count() == values.Count &&
               values.SequenceEqual(values.OrderBy(value => value.ToString("D"), StringComparer.Ordinal));
    }
}
