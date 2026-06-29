using AICopilot.AgentPlugin;
using AICopilot.AiGatewayService.AgentTasks;
using AICopilot.AiGatewayService.Approvals;
using AICopilot.AiGatewayService.Agents;
using AICopilot.AiGatewayService.BusinessSemantics;
using AICopilot.AiGatewayService.BusinessPolicies;
using AICopilot.AiGatewayService.Observability;
using AICopilot.AiGatewayService.RoutingModels;
using AICopilot.AiGatewayService.Runtime;
using AICopilot.AiGatewayService.Safety;
using AICopilot.AiGatewayService.Sessions;
using AICopilot.AiGatewayService.Skills;
using AICopilot.AiGatewayService.Tools;
using AICopilot.AiGatewayService.Workspaces;
using AICopilot.AiGatewayService.Workflows;
using AICopilot.AiGatewayService.Workflows.Executors;
using AICopilot.Services.Contracts;
using AICopilot.Services.CrossCutting.Behaviors;
using Microsoft.Extensions.Configuration;
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
        builder.Services.Configure<CloudAiReadOptions>(
            builder.Configuration.GetSection(CloudAiReadOptions.SectionName));
        builder.Services.Configure<AgentRunQueueOptions>(
            builder.Configuration.GetSection(AgentRunQueueOptions.SectionName));
        builder.Services.Configure<MockMcpOptions>(
            builder.Configuration.GetSection(MockMcpOptions.SectionName));
        builder.Services.Configure<AgentModelCallTimeoutOptions>(
            builder.Configuration.GetSection(AgentModelCallTimeoutOptions.SectionName));

        builder.Services.AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssembly(Assembly.GetExecutingAssembly());
        });

        builder.Services.AddScoped<ConfiguredAgentRuntimeFactory>();
        builder.Services.AddScoped<IAgentExecutionMetadataAccessor, AgentExecutionMetadataAccessor>();
        builder.Services.AddScoped<IRoutingModelResolver, RoutingModelResolver>();
        builder.Services.AddScoped<IAgentRuntimeSettingsProvider, AgentRuntimeSettingsProvider>();
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
        var mockMcpEnabled = builder.Environment.IsDevelopment() &&
                             builder.Configuration
                                 .GetSection(MockMcpOptions.SectionName)
                                 .Get<MockMcpOptions>() is { Enabled: true };
        if (mockMcpEnabled)
        {
            builder.Services.AddScoped<IAgentToolExecutor, MockMcpAgentToolExecutor>();
        }

        builder.Services.AddScoped<IAgentToolExecutor, McpAgentToolExecutor>();
        builder.Services.AddScoped<IMcpToolRegistryReadService, McpToolRegistryReadService>();
        builder.Services.AddScoped<ToolRegistryGuard>();
        builder.Services.AddScoped<SkillDefinitionGuard>();
        builder.Services.AddScoped<AgentPlanToolGuard>();
        builder.Services.AddScoped<AgentPlanDraftConfirmationService>();
        builder.Services.AddScoped<IAgentDynamicPlanner, DefaultAgentDynamicPlanner>();
        builder.Services.AddScoped<AgentAuditRecorder>();
        builder.Services.TryAddSingleton<ISessionExecutionLock, InMemorySessionExecutionLock>();
        builder.Services.AddSingleton<IRequestValidator<ChatStreamRequest>, ChatStreamRequestValidator>();
        builder.Services.AddSingleton<IRequestValidator<ApprovalDecisionStreamRequest>, ApprovalDecisionStreamRequestValidator>();
        builder.Services.AddSingleton<IRequestValidator<PlanAgentTaskStreamRequest>, PlanAgentTaskStreamRequestValidator>();
        builder.Services.AddSingleton<IOperationalBoundaryPolicy, ManufacturingOperationalBoundaryPolicy>();
        builder.Services.AddSingleton<IManufacturingSceneClassifier, KeywordManufacturingSceneClassifier>();
        builder.Services.AddSingleton<ITokenBudgetPolicy, ChatTokenBudgetPolicy>();
        builder.Services.AddSingleton<IChatTokenTelemetry, ChatTokenTelemetry>();
        builder.Services.AddScoped<ApprovalRequirementResolver>();
        builder.Services.AddScoped<IAgentStreamRuntime, AgentStreamRuntime>();
        builder.Services.AddScoped<IApprovalRequirementReadService, ApprovalRequirementReadService>();
        builder.Services.AddScoped<ApprovalToolResolver>();
        builder.Services.AddScoped<IFinalAgentContextSerializer, FinalAgentContextSerializer>();
        builder.Services.AddScoped<SessionMessagePersistenceService>();
        builder.Services.AddScoped<MessageTimelineProjectionWriter>();
        builder.Services.AddScoped<IAgentSkillAutoSelector, AgentSkillRouterAutoSelector>();
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
        builder.Services.AddScoped<AgentWorkflowPipeline>();
    }
}
