using System.Text.Json.Serialization;
using AICopilot.Core.AiGateway.Aggregates.AgentTasks;
using AICopilot.AiGatewayService.CloudReadiness;

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
    [property: JsonPropertyName("trialScenarioId")] string? TrialScenarioId = null,
    [property: JsonPropertyName("trialScenarioTitle")] string? TrialScenarioTitle = null,
    [property: JsonPropertyName("isSimulationTrial")] bool IsSimulationTrial = false,
    [property: JsonPropertyName("isCloudSandboxControlledTrial")] bool IsCloudSandboxControlledTrial = false,
    [property: JsonPropertyName("cloudSandboxGoalIntent")] CloudSandboxGoalIntentDto? CloudSandboxGoalIntent = null,
    [property: JsonPropertyName("plannerSafetySummary")] AgentTaskPlanSafetySummaryDocument? PlannerSafetySummary = null,
    [property: JsonPropertyName("forcedStepCodes")] IReadOnlyCollection<string>? ForcedStepCodes = null,
    [property: JsonPropertyName("approvalCheckpoints")] IReadOnlyCollection<string>? ApprovalCheckpoints = null,
    [property: JsonPropertyName("dataSourceSummaries")] IReadOnlyCollection<AgentTaskPlanDataSourceSummaryDocument>? DataSourceSummaries = null,
    [property: JsonPropertyName("toolCatalogVersion")] int ToolCatalogVersion = PlannerToolCatalog.CurrentVersion,
    [property: JsonPropertyName("visibleToolCount")] int VisibleToolCount = 0,
    [property: JsonPropertyName("toolRiskSummary")] IReadOnlyDictionary<string, int>? ToolRiskSummary = null,
    [property: JsonPropertyName("mockMcpOnly")] bool MockMcpOnly = true,
    [property: JsonPropertyName("toolApprovalCheckpoints")] IReadOnlyCollection<string>? ToolApprovalCheckpoints = null);

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
    [property: JsonPropertyName("mockMcpOnly")] bool MockMcpOnly = true);

internal sealed record AgentTaskPlanDataSourceSummaryDocument(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("sourceMode")] string SourceMode,
    [property: JsonPropertyName("isSimulation")] bool IsSimulation,
    [property: JsonPropertyName("sourceLabel")] string SourceLabel,
    [property: JsonPropertyName("businessDomain")] string? BusinessDomain);
