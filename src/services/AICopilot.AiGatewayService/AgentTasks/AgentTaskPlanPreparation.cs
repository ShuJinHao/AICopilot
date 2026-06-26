using AICopilot.Services.Contracts;

namespace AICopilot.AiGatewayService.AgentTasks;

internal sealed record AgentTaskPlanPreparation(
    Guid UserId,
    Guid[] UploadIds,
    Guid[] KnowledgeBaseIds,
    Guid[] DataSourceIds,
    BusinessDatabaseDescriptor[] SelectedDataSources,
    string[] BusinessDomains,
    bool IsCloudSandboxFixedTrialPlan,
    bool IsCloudSandboxControlledTrialPlan,
    bool IsCloudSandboxTrialPlan,
    bool IsCloudProductionPilotTrialPlan,
    bool IsCloudProductionControlledPilotPlan,
    bool IsSimulationOnlyPlan,
    bool HasBusinessDataSourcesForPlan);
