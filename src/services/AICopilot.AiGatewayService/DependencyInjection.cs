using System.Reflection;
using AICopilot.AgentPlugin;
using AICopilot.AiGatewayService.Agents;
using AICopilot.AiGatewayService.BusinessQueries;
using AICopilot.AiGatewayService.BusinessSemantics;
using AICopilot.AiGatewayService.Runtime;
using AICopilot.AiGatewayService.Safety;
using AICopilot.AiGatewayService.Sessions;
using AICopilot.AiGatewayService.Tools;
using AICopilot.Services.Contracts;
using AICopilot.Services.CrossCutting.Behaviors;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace AICopilot.AiGatewayService;

public static class DependencyInjection
{
    public static void AddAiGatewayService(this IHostApplicationBuilder builder)
    {
        builder.Services.Configure<CloudReadonlyOptions>(
            builder.Configuration.GetSection(CloudReadonlyOptions.SectionName));
        builder.Services.Configure<CloudAiReadOptions>(
            builder.Configuration.GetSection(CloudAiReadOptions.SectionName));
        builder.Services.Configure<CloudReadOnlyTextToSqlOptions>(
            builder.Configuration.GetSection(CloudReadOnlyTextToSqlOptions.SectionName));

        builder.Services.AddMediatR(configuration =>
            configuration.RegisterServicesFromAssembly(Assembly.GetExecutingAssembly()));

        builder.Services.AddScoped<ConfiguredAgentRuntimeFactory>();
        builder.Services.AddScoped<IChatExecutionMetadataAccessor, ChatExecutionMetadataAccessor>();
        builder.Services.AddScoped<IBusinessQueryProvider, CloudAiReadBusinessQueryProvider>();
        builder.Services.AddScoped<IBusinessQueryProviderRegistry, BusinessQueryProviderRegistry>();
        builder.Services.AddScoped<BusinessQueryExecutor>();
        builder.Services.AddScoped<DataAnalysisAuditRecorder>();
        builder.Services.AddScoped<IBusinessTextToSqlGenerator, BusinessLlmTextToSqlGenerator>();
        builder.Services.AddScoped<BusinessTextToSqlFallbackRunner>();
        builder.Services.AddScoped<IBusinessTextToSqlFallbackRunner>(services =>
            services.GetRequiredService<BusinessTextToSqlFallbackRunner>());

        builder.Services.AddScoped<IMcpToolRegistryReadService, McpToolRegistryReadService>();
        builder.Services.AddScoped<ToolRegistryGuard>();
        builder.Services.AddScoped<MainChatToolGate>();
        builder.Services.AddScoped<MainChatToolCatalog>();
        builder.Services.AddScoped<IAgentStreamRuntime, AgentStreamRuntime>();
        builder.Services.AddScoped<SessionMessagePersistenceService>();

        builder.Services.TryAddSingleton<ISessionExecutionLock, InMemorySessionExecutionLock>();
        builder.Services.AddSingleton<IRequestValidator<ChatStreamRequest>, ChatStreamRequestValidator>();
        builder.Services.AddSingleton<IRequestValidator<ApprovalDecisionStreamRequest>, ApprovalDecisionStreamRequestValidator>();
        builder.Services.AddSingleton<IOperationalBoundaryPolicy, ManufacturingOperationalBoundaryPolicy>();
        builder.Services.AddSingleton<ISemanticSummaryProfileCatalog, SemanticSummaryProfileCatalog>();

        builder.Services.AddAgentPlugin(registrar =>
            registrar.RegisterPluginFromAssembly(Assembly.GetExecutingAssembly()));
    }
}
