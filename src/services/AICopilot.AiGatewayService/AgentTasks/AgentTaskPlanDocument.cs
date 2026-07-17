using System.Text.Json.Serialization;
using AICopilot.Core.AiGateway.Aggregates.AgentTasks;

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
    [property: JsonPropertyName("skillCode")] string? SkillCode = null,
    [property: JsonPropertyName("skillName")] string? SkillName = null,
    [property: JsonPropertyName("skillRoutingReason")] string? SkillRoutingReason = null,
    [property: JsonPropertyName("planKind")] string PlanKind = AgentTaskPlanKinds.ExecutablePlan,
    [property: JsonPropertyName("isExecutable")] bool IsExecutable = true,
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
    [property: JsonPropertyName("intent")] string Intent,
    [property: JsonPropertyName("query")] string? Query,
    [property: JsonPropertyName("confidence")] double Confidence,
    [property: JsonPropertyName("target")] string Target,
    [property: JsonPropertyName("kind")] string Kind,
    [property: JsonPropertyName("summary")] string Summary)
{
    public static AgentTaskPlanCloudReadonlyIntentDocument From(CloudReadonlyAgentPlanIntent intent)
    {
        return new AgentTaskPlanCloudReadonlyIntentDocument(
            intent.Intent,
            intent.Query,
            intent.Confidence,
            intent.Target,
            intent.Kind,
            intent.Summary);
    }
}

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
