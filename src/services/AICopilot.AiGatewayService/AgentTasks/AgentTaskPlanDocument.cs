using System.Text.Json.Serialization;
using AICopilot.Core.AiGateway.Aggregates.AgentTasks;
using AICopilot.Services.Contracts;

namespace AICopilot.AiGatewayService.AgentTasks;

internal sealed record AgentTaskPlanDocument(
    [property: JsonPropertyName("version")] int Version,
    [property: JsonPropertyName("plannerTemplateCode")] string PlannerTemplateCode,
    [property: JsonPropertyName("goal")] string Goal,
    [property: JsonPropertyName("taskType")] string TaskType,
    [property: JsonPropertyName("riskLevel")] string RiskLevel,
    [property: JsonPropertyName("uploadIds")] IReadOnlyCollection<Guid> UploadIds,
    [property: JsonPropertyName("knowledgeBaseIds")] IReadOnlyCollection<Guid> KnowledgeBaseIds,
    [property: JsonPropertyName("cloudReadonlyIntent")] AgentTaskPlanCloudReadonlyIntentDocument? CloudReadonlyIntent,
    [property: JsonPropertyName("steps")] IReadOnlyCollection<AgentTaskPlanStepDocument> Steps,
    [property: JsonPropertyName("runtimeSettings")] AgentTaskPlanRuntimeSettingsDocument RuntimeSettings,
    [property: JsonPropertyName("plannerMode")] string PlannerMode = "Static",
    [property: JsonPropertyName("plannerFallbackReason")] string? PlannerFallbackReason = null,
    [property: JsonPropertyName("plannerModelId")] Guid? PlannerModelId = null,
    [property: JsonPropertyName("plannerValidationVersion")] int PlannerValidationVersion = 1,
    [property: JsonPropertyName("plannerToolCatalogVersion")] int PlannerToolCatalogVersion = PlannerToolCatalog.CurrentVersion,
    [property: JsonPropertyName("plannerAvailableToolCount")] int PlannerAvailableToolCount = 0,
    [property: JsonPropertyName("dataSourceIds")] IReadOnlyCollection<Guid>? DataSourceIds = null,
    [property: JsonPropertyName("businessDomains")] IReadOnlyCollection<string>? BusinessDomains = null,
    [property: JsonPropertyName("queryMode")] string? QueryMode = null,
    [property: JsonPropertyName("requiresDataApproval")] bool RequiresDataApproval = false,
    [property: JsonPropertyName("artifactTypes")] IReadOnlyCollection<string>? ArtifactTypes = null,
    [property: JsonPropertyName("plannerSafetySummary")] AgentTaskPlanSafetySummaryDocument? PlannerSafetySummary = null,
    [property: JsonPropertyName("forcedStepCodes")] IReadOnlyCollection<string>? ForcedStepCodes = null,
    [property: JsonPropertyName("approvalCheckpoints")] IReadOnlyCollection<string>? ApprovalCheckpoints = null,
    [property: JsonPropertyName("dataSourceSummaries")] IReadOnlyCollection<AgentTaskPlanDataSourceSummaryDocument>? DataSourceSummaries = null,
    [property: JsonPropertyName("toolCatalogVersion")] int ToolCatalogVersion = PlannerToolCatalog.CurrentVersion,
    [property: JsonPropertyName("visibleToolCount")] int VisibleToolCount = 0,
    [property: JsonPropertyName("toolRiskSummary")] IReadOnlyDictionary<string, int>? ToolRiskSummary = null,
    [property: JsonPropertyName("mockMcpOnly")] bool MockMcpOnly = false,
    [property: JsonPropertyName("toolApprovalCheckpoints")] IReadOnlyCollection<string>? ToolApprovalCheckpoints = null,
    [property: JsonPropertyName("skillCode"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? SkillCode = null,
    [property: JsonPropertyName("skillName"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? SkillName = null,
    [property: JsonPropertyName("skillRoutingReason"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? SkillRoutingReason = null,
    [property: JsonPropertyName("planKind")] string PlanKind = AgentTaskPlanKinds.ExecutablePlan,
    [property: JsonPropertyName("isExecutable")] bool IsExecutable = true,
    [property: JsonPropertyName("lifecycleSealPadding")] string LifecycleSealPadding = "",
    [property: JsonPropertyName("capabilityGaps")] IReadOnlyCollection<string>? CapabilityGaps = null,
    [property: JsonPropertyName("schemaVersion")] string SchemaVersion = AgentPlanContractVersions.LegacyV1,
    [property: JsonPropertyName("planId")] Guid? PlanId = null,
    [property: JsonPropertyName("planVersion")] int PlanVersion = 1,
    [property: JsonPropertyName("planDigest")] string? PlanDigest = null,
    [property: JsonPropertyName("topologyProfile")] string? TopologyProfile = null,
    [property: JsonPropertyName("intentCandidates")] IReadOnlyCollection<AgentIntentCandidateDocument>? IntentCandidates = null,
    [property: JsonPropertyName("capabilitySelectionMode")] AgentCapabilitySelectionMode? CapabilitySelectionMode = null,
    [property: JsonPropertyName("requestedCapabilityCodes")] IReadOnlyCollection<string>? RequestedCapabilityCodes = null,
    [property: JsonPropertyName("pluginSelectionMode")] AgentPluginSelectionMode? PluginSelectionMode = null,
    [property: JsonPropertyName("selectedPluginIds")] IReadOnlyCollection<Guid>? SelectedPluginIds = null,
    [property: JsonPropertyName("artifactTargets")] IReadOnlyCollection<string>? ArtifactTargets = null,
    [property: JsonPropertyName("nodes")] IReadOnlyCollection<AgentPlanNodeDocument>? Nodes = null,
    [property: JsonPropertyName("joinPolicies")] IReadOnlyCollection<string>? JoinPolicies = null,
    [property: JsonPropertyName("budgets")] AgentPlanBudgetDocument? Budgets = null,
    [property: JsonPropertyName("approvalSummary")] AgentPlanApprovalSummaryDocument? ApprovalSummary = null,
    [property: JsonPropertyName("executionSnapshot")] AgentExecutionSnapshotDocument? ExecutionSnapshot = null,
    [property: JsonPropertyName("securitySummary")] AgentPlanSecuritySummaryDocument? SecuritySummary = null);

internal static class AgentTaskPlanKinds
{
    public const string PlanDraft = "PlanDraft";
    public const string ExecutablePlan = "ExecutablePlan";
}

internal sealed record AgentTaskPlanCloudReadonlyIntentDocument(
    [property: JsonPropertyName("schemaVersion")] string SchemaVersion,
    [property: JsonPropertyName("intent")] string Intent,
    [property: JsonPropertyName("semanticPlanDigest")] string SemanticPlanDigest,
    [property: JsonPropertyName("confidence")] double Confidence,
    [property: JsonPropertyName("target")] SemanticQueryTarget Target,
    [property: JsonPropertyName("kind")] SemanticQueryKind Kind,
    [property: JsonPropertyName("projectionFields")] IReadOnlyCollection<string> ProjectionFields,
    [property: JsonPropertyName("filters")] IReadOnlyCollection<AgentTaskPlanSemanticFilterDocument> Filters,
    [property: JsonPropertyName("timeRange")] AgentTaskPlanSemanticTimeRangeDocument? TimeRange,
    [property: JsonPropertyName("sort")] AgentTaskPlanSemanticSortDocument? Sort,
    [property: JsonPropertyName("limit")] int Limit,
    [property: JsonPropertyName("queryScope")] IReadOnlyCollection<string> QueryScope)
{
    public static AgentTaskPlanCloudReadonlyIntentDocument From(CloudReadonlyAgentPlanIntent intent)
    {
        var plan = intent.SemanticPlan;
        var document = new AgentTaskPlanCloudReadonlyIntentDocument(
            "cloud-readonly-semantic-plan:v1",
            plan.Intent,
            string.Empty,
            intent.Confidence,
            plan.Target,
            plan.Kind,
            plan.Projection.Fields
                .Distinct(StringComparer.Ordinal)
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToArray(),
            plan.Filters
                .Select(filter => new AgentTaskPlanSemanticFilterDocument(
                    filter.Field,
                    filter.Operator,
                    filter.Value))
                .Distinct()
                .OrderBy(filter => filter.Field, StringComparer.Ordinal)
                .ThenBy(filter => filter.Operator)
                .ThenBy(filter => filter.Value, StringComparer.Ordinal)
                .ToArray(),
            plan.TimeRange is null
                ? null
                : new AgentTaskPlanSemanticTimeRangeDocument(
                    plan.TimeRange.Field,
                    plan.TimeRange.Start?.ToUniversalTime(),
                    plan.TimeRange.End?.ToUniversalTime(),
                    plan.TimeRange.TimeZone),
            plan.Sort is null
                ? null
                : new AgentTaskPlanSemanticSortDocument(plan.Sort.Field, plan.Sort.Direction),
            plan.Limit,
            BuildQueryScope(plan));
        var digest = ComputeSemanticPlanDigest(document);
        if (!string.Equals(digest, intent.SemanticPlanDigest, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Cloud readonly typed semantic plan digest changed before PlanDraft sealing.");
        }

        return document with { SemanticPlanDigest = digest };
    }

    public SemanticQueryPlan ToSemanticPlan()
    {
        return new SemanticQueryPlan(
            Intent,
            Target,
            Kind,
            QueryText: null,
            new SemanticProjection(ProjectionFields.ToArray()),
            Filters.Select(filter => new SemanticFilter(
                filter.Field,
                filter.Operator,
                filter.Value)).ToArray(),
            TimeRange is null
                ? null
                : new SemanticTimeRange(TimeRange.Field, TimeRange.Start, TimeRange.End, TimeRange.TimeZone),
            Sort is null
                ? null
                : new SemanticSort(Sort.Field, Sort.Direction),
            Limit);
    }

    public static string ComputeSemanticPlanDigest(AgentTaskPlanCloudReadonlyIntentDocument document)
    {
        var canonical = CanonicalJson.Canonicalize(
            CanonicalJson.Serialize(document with { SemanticPlanDigest = string.Empty }),
            new HashSet<string>(["semanticPlanDigest", "confidence"], StringComparer.Ordinal));
        return CanonicalJson.ComputeSha256(canonical);
    }

    public static string ComputeSemanticPlanDigest(SemanticQueryPlan plan)
    {
        var placeholder = FromUnchecked(plan, confidence: 1);
        return ComputeSemanticPlanDigest(placeholder);
    }

    private static AgentTaskPlanCloudReadonlyIntentDocument FromUnchecked(
        SemanticQueryPlan plan,
        double confidence)
    {
        return new AgentTaskPlanCloudReadonlyIntentDocument(
            "cloud-readonly-semantic-plan:v1",
            plan.Intent,
            string.Empty,
            confidence,
            plan.Target,
            plan.Kind,
            plan.Projection.Fields.Distinct(StringComparer.Ordinal).OrderBy(value => value, StringComparer.Ordinal).ToArray(),
            plan.Filters.Select(filter => new AgentTaskPlanSemanticFilterDocument(filter.Field, filter.Operator, filter.Value))
                .Distinct()
                .OrderBy(filter => filter.Field, StringComparer.Ordinal)
                .ThenBy(filter => filter.Operator)
                .ThenBy(filter => filter.Value, StringComparer.Ordinal)
                .ToArray(),
            plan.TimeRange is null
                ? null
                : new AgentTaskPlanSemanticTimeRangeDocument(
                    plan.TimeRange.Field,
                    plan.TimeRange.Start?.ToUniversalTime(),
                    plan.TimeRange.End?.ToUniversalTime(),
                    plan.TimeRange.TimeZone),
            plan.Sort is null ? null : new AgentTaskPlanSemanticSortDocument(plan.Sort.Field, plan.Sort.Direction),
            plan.Limit,
            BuildQueryScope(plan));
    }

    private static string[] BuildQueryScope(SemanticQueryPlan plan)
    {
        return new[]
            {
                $"target:{plan.Target}",
                $"kind:{plan.Kind}"
            }
            .Concat(plan.Projection.Fields.Select(field => $"projection:{field}"))
            .Concat(plan.Filters.Select(filter => $"filter:{filter.Field}"))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
    }
}

internal sealed record AgentTaskPlanSemanticFilterDocument(
    [property: JsonPropertyName("field")] string Field,
    [property: JsonPropertyName("operator")] SemanticFilterOperator Operator,
    [property: JsonPropertyName("value")] string Value);

internal sealed record AgentTaskPlanSemanticTimeRangeDocument(
    [property: JsonPropertyName("field")] string Field,
    [property: JsonPropertyName("start")] DateTimeOffset? Start,
    [property: JsonPropertyName("end")] DateTimeOffset? End,
    [property: JsonPropertyName("timeZone")] string TimeZone);

internal sealed record AgentTaskPlanSemanticSortDocument(
    [property: JsonPropertyName("field")] string Field,
    [property: JsonPropertyName("direction")] SemanticSortDirection Direction);

internal sealed record AgentTaskPlanStepDocument(
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("description")] string Description,
    [property: JsonPropertyName("stepType")] AgentStepType StepType,
    [property: JsonPropertyName("toolCode")] string? ToolCode,
    [property: JsonPropertyName("requiresApproval")] bool RequiresApproval,
    [property: JsonPropertyName("inputJson")] string? InputJson = null);

internal sealed record AgentTaskPlanRuntimeSettingsDocument(
    [property: JsonPropertyName("agentPlanningHistoryCount")] int AgentPlanningHistoryCount,
    [property: JsonPropertyName("contextTokenLimit")] int ContextTokenLimit);

internal sealed record AgentTaskPlanSafetySummaryDocument(
    [property: JsonPropertyName("planSource")] string PlanSource,
    [property: JsonPropertyName("plannerMode")] string PlannerMode,
    [property: JsonPropertyName("plannerModelSummary")] string? PlannerModelSummary,
    [property: JsonPropertyName("plannerToolCatalogVersion")] int PlannerToolCatalogVersion,
    [property: JsonPropertyName("availableToolCount")] int AvailableToolCount,
    [property: JsonPropertyName("isSimulationOnly")] bool IsSimulationOnly,
    [property: JsonPropertyName("requiresDataApproval")] bool RequiresDataApproval,
    [property: JsonPropertyName("toolRiskSummary")] IReadOnlyDictionary<string, int>? ToolRiskSummary = null,
    [property: JsonPropertyName("mockMcpOnly")] bool MockMcpOnly = false);

internal sealed record AgentTaskPlanDataSourceSummaryDocument(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("sourceMode")] string SourceMode,
    [property: JsonPropertyName("isSimulation")] bool IsSimulation,
    [property: JsonPropertyName("sourceLabel")] string SourceLabel,
    [property: JsonPropertyName("businessDomain")] string? BusinessDomain);
