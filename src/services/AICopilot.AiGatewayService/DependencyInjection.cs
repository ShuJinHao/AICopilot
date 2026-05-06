using AICopilot.AgentPlugin;
using AICopilot.AiGatewayService.Approvals;
using AICopilot.AiGatewayService.Agents;
using AICopilot.AiGatewayService.BusinessSemantics;
using AICopilot.AiGatewayService.BusinessPolicies;
using AICopilot.AiGatewayService.Observability;
using AICopilot.AiGatewayService.Safety;
using AICopilot.AiGatewayService.Sessions;
using AICopilot.AiGatewayService.Workflows;
using AICopilot.AiGatewayService.Workflows.Executors;
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
        builder.Services.AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssembly(Assembly.GetExecutingAssembly());
        });

        builder.Services.AddScoped<ChatAgentFactory>();
        builder.Services.TryAddSingleton<ISessionExecutionLock, InMemorySessionExecutionLock>();
        builder.Services.AddSingleton<IOperationalBoundaryPolicy, ManufacturingOperationalBoundaryPolicy>();
        builder.Services.AddSingleton<IManufacturingSceneClassifier, KeywordManufacturingSceneClassifier>();
        builder.Services.AddSingleton<ITokenBudgetPolicy, ChatTokenBudgetPolicy>();
        builder.Services.AddSingleton<IChatTokenTelemetry, ChatTokenTelemetry>();
        builder.Services.AddScoped<ApprovalRequirementResolver>();
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
        builder.Services.AddScoped<DataAnalysisExecutor>();
        builder.Services.AddScoped<BusinessPolicyExecutor>();
        builder.Services.AddScoped<ContextAggregatorExecutor>();
        builder.Services.AddScoped<FinalAgentBuildExecutor>();
        builder.Services.AddScoped<FinalAgentRunExecutor>();
        builder.Services.AddScoped<ChatWorkflowOrchestrator>();
    }
}
