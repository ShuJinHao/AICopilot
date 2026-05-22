using AICopilot.AgentPlugin;
using AICopilot.AiGatewayService.AgentTasks;
using AICopilot.AiGatewayService.Approvals;
using AICopilot.AiGatewayService.Agents;
using AICopilot.AiGatewayService.BusinessSemantics;
using AICopilot.AiGatewayService.BusinessPolicies;
using AICopilot.AiGatewayService.Observability;
using AICopilot.AiGatewayService.PromptPolicies;
using AICopilot.AiGatewayService.RoutingModels;
using AICopilot.AiGatewayService.Runtime;
using AICopilot.AiGatewayService.Safety;
using AICopilot.AiGatewayService.Sessions;
using AICopilot.AiGatewayService.Tools;
using AICopilot.AiGatewayService.Workspaces;
using AICopilot.AiGatewayService.Workflows;
using AICopilot.AiGatewayService.Workflows.Executors;
using AICopilot.AiGatewayService.CloudReadiness;
using AICopilot.Services.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using System.Reflection;

namespace AICopilot.AiGatewayService;

public static class DependencyInjection
{
    public static void AddAiGatewayService(this IHostApplicationBuilder builder)
    {
        builder.Services.Configure<CloudReadonlyOptions>(
            builder.Configuration.GetSection(CloudReadonlyOptions.SectionName));
        builder.Services.Configure<CloudReadonlySandboxOptions>(
            builder.Configuration.GetSection(CloudReadonlySandboxOptions.SectionName));
        builder.Services.Configure<CloudReadonlySandboxAgentTrialOptions>(
            builder.Configuration.GetSection(CloudReadonlySandboxAgentTrialOptions.SectionName));
        builder.Services.Configure<CloudReadonlySandboxControlledTrialOptions>(
            builder.Configuration.GetSection(CloudReadonlySandboxControlledTrialOptions.SectionName));
        builder.Services.Configure<CloudReadonlyPilotReadinessOptions>(
            builder.Configuration.GetSection(CloudReadonlyPilotReadinessOptions.SectionName));
        builder.Services.Configure<CloudReadonlyProductionPilotOptions>(
            builder.Configuration.GetSection(CloudReadonlyProductionPilotOptions.SectionName));
        builder.Services.Configure<CloudReadonlyProductionControlledPilotOptions>(
            builder.Configuration.GetSection(CloudReadonlyProductionControlledPilotOptions.SectionName));
        builder.Services.Configure<CloudAiReadOptions>(
            builder.Configuration.GetSection(CloudAiReadOptions.SectionName));
        builder.Services.Configure<AgentRunQueueOptions>(
            builder.Configuration.GetSection(AgentRunQueueOptions.SectionName));

        builder.Services.AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssembly(Assembly.GetExecutingAssembly());
        });

        builder.Services.AddScoped<ChatAgentFactory>();
        builder.Services.AddScoped<IChatExecutionMetadataAccessor, ChatExecutionMetadataAccessor>();
        builder.Services.AddScoped<IRoutingModelResolver, RoutingModelResolver>();
        builder.Services.AddScoped<IChatRuntimeSettingsProvider, ChatRuntimeSettingsProvider>();
        builder.Services.AddScoped<IPromptPolicyProvider, PromptPolicyProvider>();
        builder.Services.AddScoped<IAgentArtifactWorkspaceService, AgentArtifactWorkspaceService>();
        builder.Services.AddScoped<IAgentTaskRuntime, AgentTaskRuntime>();
        builder.Services.AddScoped<IAgentTaskRunQueue, AgentTaskRunQueue>();
        builder.Services.AddScoped<IAgentWorkspaceFingerprintProvider, AgentWorkspaceFingerprintProvider>();
        builder.Services.AddScoped<IAgentWorkerHeartbeatService, AgentWorkerHeartbeatService>();
        builder.Services.AddScoped<ICloudReadonlyAgentIntentRouter, CloudReadonlyAgentIntentRouter>();
        builder.Services.AddScoped<ICloudReadonlyAgentPlanService, CloudReadonlyAgentPlanService>();
        builder.Services.AddSingleton<CloudReadonlySimulationDataSet>();
        builder.Services.AddSingleton<ICloudReadonlySimulationIntentPlanner, CloudReadonlySimulationIntentPlanner>();
        builder.Services.AddScoped<ICloudReadonlyDataProvider, DisabledCloudReadonlyDataProvider>();
        builder.Services.AddScoped<ICloudReadonlyDataProvider, SimulationCloudReadonlyDataProvider>();
        builder.Services.AddScoped<ICloudReadonlyDataProvider, RealCloudReadonlyDataProvider>();
        builder.Services.AddScoped<ICloudReadonlyDataProviderResolver, CloudReadonlyDataProviderResolver>();
        builder.Services.AddScoped<ICloudReadonlyAgentToolExecutor, CloudReadonlyAgentToolExecutor>();
        builder.Services.AddSingleton<ICloudReadonlyReadinessHistoryStore, InMemoryCloudReadonlyReadinessHistoryStore>();
        builder.Services.AddSingleton<ICloudReadonlySandboxAgentTrialHistoryStore, InMemoryCloudReadonlySandboxAgentTrialHistoryStore>();
        builder.Services.AddSingleton<ICloudReadonlySandboxControlledTrialIntentStore, InMemoryCloudReadonlySandboxControlledTrialIntentStore>();
        builder.Services.AddSingleton<ICloudReadonlyPilotReadinessStore, InMemoryCloudReadonlyPilotReadinessStore>();
        builder.Services.AddSingleton<ICloudReadonlyProductionPilotStore, InMemoryCloudReadonlyProductionPilotStore>();
        builder.Services.AddSingleton<ICloudReadonlyProductionControlledPilotStore, InMemoryCloudReadonlyProductionControlledPilotStore>();
        builder.Services.AddScoped<IProductionPilotOperationsStore, RepositoryProductionPilotOperationsStore>();
        builder.Services.AddScoped<CloudReadonlyReadinessService>();
        builder.Services.AddScoped<CloudReadonlySandboxAgentTrialService>();
        builder.Services.AddScoped<CloudReadonlySandboxControlledTrialService>();
        builder.Services.AddScoped<CloudReadonlyPilotReadinessService>();
        builder.Services.AddScoped<CloudReadonlyProductionPilotService>();
        builder.Services.AddScoped<CloudReadonlyProductionControlledPilotService>();
        builder.Services.AddScoped<CloudReadonlyProductionOperationsService>();
        builder.Services.AddScoped<IAgentToolExecutor, MockMcpAgentToolExecutor>();
        builder.Services.AddScoped<IAgentToolExecutor, McpAgentToolExecutor>();
        builder.Services.AddScoped<IMcpToolRegistryReadService, McpToolRegistryReadService>();
        builder.Services.AddScoped<ToolRegistryGuard>();
        builder.Services.AddScoped<AgentPlanToolGuard>();
        builder.Services.AddScoped<IAgentDynamicPlanner, DefaultAgentDynamicPlanner>();
        builder.Services.AddScoped<AgentAuditRecorder>();
        builder.Services.TryAddSingleton<ISessionExecutionLock, InMemorySessionExecutionLock>();
        builder.Services.AddSingleton<IOperationalBoundaryPolicy, ManufacturingOperationalBoundaryPolicy>();
        builder.Services.AddSingleton<IManufacturingSceneClassifier, KeywordManufacturingSceneClassifier>();
        builder.Services.AddSingleton<ITokenBudgetPolicy, ChatTokenBudgetPolicy>();
        builder.Services.AddSingleton<IChatTokenTelemetry, ChatTokenTelemetry>();
        builder.Services.AddScoped<ApprovalRequirementResolver>();
        builder.Services.AddScoped<IChatStreamRuntime, ChatStreamRuntime>();
        builder.Services.AddScoped<IApprovalRequirementReadService, ApprovalRequirementReadService>();
        builder.Services.AddScoped<ApprovalToolResolver>();
        builder.Services.AddScoped<IFinalAgentContextSerializer, FinalAgentContextSerializer>();
        builder.Services.AddScoped<SessionMessagePersistenceService>();
        builder.Services.AddSingleton<IBusinessPolicyCatalog, BusinessPolicyCatalog>();
        builder.Services.AddSingleton<ISemanticSummaryProfileCatalog, SemanticSummaryProfileCatalog>();
        builder.Services.AddSingleton<IBusinessSemanticsCatalog, BusinessSemanticsCatalog>();

        builder.Services.AddAgentPlugin(registrar =>
        {
            registrar.RegisterPluginFromAssembly(Assembly.GetExecutingAssembly());
        });

        builder.Services.AddScoped<IntentRoutingPromptComposer>();
        builder.Services.AddScoped<IntentRoutingAgentBuilder>();
        builder.Services.AddScoped<DataAnalysisAgentBuilder>();

        builder.Services.AddScoped<IntentRoutingExecutor>();
        builder.Services.AddScoped<ToolsPackExecutor>();
        builder.Services.AddScoped<KnowledgeRetrievalExecutor>();
        builder.Services.AddScoped<SemanticAnalysisRunner>();
        builder.Services.AddScoped<FreeFormDbaAnalysisRunner>();
        builder.Services.AddScoped<DataAnalysisWidgetEmitter>();
        builder.Services.AddScoped<DataAnalysisAuditRecorder>();
        builder.Services.AddScoped<ToolExecutionAuditRecorder>();
        builder.Services.AddScoped<DataAnalysisExecutor>();
        builder.Services.AddScoped<BusinessPolicyExecutor>();
        builder.Services.AddScoped<ContextAggregatorExecutor>();
        builder.Services.AddScoped<FinalAgentBuildExecutor>();
        builder.Services.AddScoped<FinalAgentRunExecutor>();
        builder.Services.AddScoped<ChatWorkflowOrchestrator>();
    }
}
