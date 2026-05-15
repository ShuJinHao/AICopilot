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
    [property: JsonPropertyName("steps")] IReadOnlyCollection<AgentTaskPlanStepDocument> Steps,
    [property: JsonPropertyName("runtimeSettings")] AgentTaskPlanRuntimeSettingsDocument RuntimeSettings);

internal sealed record AgentTaskPlanStepDocument(
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("description")] string Description,
    [property: JsonPropertyName("stepType")] AgentStepType StepType,
    [property: JsonPropertyName("toolCode")] string? ToolCode,
    [property: JsonPropertyName("requiresApproval")] bool RequiresApproval);

internal sealed record AgentTaskPlanRuntimeSettingsDocument(
    [property: JsonPropertyName("agentPlanningHistoryCount")] int AgentPlanningHistoryCount,
    [property: JsonPropertyName("contextTokenLimit")] int ContextTokenLimit);
