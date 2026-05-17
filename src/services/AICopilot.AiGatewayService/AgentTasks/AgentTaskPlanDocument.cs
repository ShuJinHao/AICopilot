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
    [property: JsonPropertyName("plannerModelId")] Guid? PlannerModelId = null,
    [property: JsonPropertyName("plannerValidationVersion")] int PlannerValidationVersion = 1,
    [property: JsonPropertyName("plannerToolCatalogVersion")] int PlannerToolCatalogVersion = PlannerToolCatalog.CurrentVersion,
    [property: JsonPropertyName("plannerAvailableToolCount")] int PlannerAvailableToolCount = 0);

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
