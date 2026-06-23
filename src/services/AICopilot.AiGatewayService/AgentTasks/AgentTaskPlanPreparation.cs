using AICopilot.Services.Contracts;

namespace AICopilot.AiGatewayService.AgentTasks;

internal sealed record AgentTaskPlanPreparation(
    Guid UserId,
    Guid[] UploadIds,
    Guid[] KnowledgeBaseIds,
    Guid[] DataSourceIds,
    BusinessDatabaseDescriptor[] SelectedDataSources,
    string[] BusinessDomains,
    bool IsSimulationOnlyPlan,
    bool HasBusinessDataSourcesForPlan);
