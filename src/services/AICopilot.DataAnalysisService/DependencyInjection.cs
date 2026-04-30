using AICopilot.AgentPlugin;
using AICopilot.DataAnalysisService.BusinessDatabases;
using AICopilot.DataAnalysisService.Services;
using AICopilot.DataAnalysisService.Semantics;
using AICopilot.Services.Contracts;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Reflection;

namespace AICopilot.DataAnalysisService;

public static class DependencyInjection
{
    public static void AddDataAnalysisService(this IHostApplicationBuilder builder)
    {
        builder.Services.AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssembly(Assembly.GetExecutingAssembly());
        });

        builder.Services.AddScoped<VisualizationContext>();
        builder.Services.AddScoped<IDataAnalysisVisualizationContext>(provider =>
            provider.GetRequiredService<VisualizationContext>());
        builder.Services.AddSingleton<ISqlDialectInstructionProvider, SqlDialectInstructionProvider>();
        builder.Services.AddSingleton<ISemanticDefinitionCatalog, SemanticDefinitionCatalog>();
        builder.Services.AddSingleton<ISemanticIntentCatalog, SemanticIntentCatalog>();
        builder.Services.AddSingleton<ISemanticPhysicalMappingProvider, ConfiguredSemanticPhysicalMappingProvider>();
        builder.Services.AddScoped<ISemanticSourceInspector, SemanticSourceInspector>();
        builder.Services.AddScoped<ISemanticQueryPlanner, SemanticQueryPlanner>();
        builder.Services.AddScoped<ISemanticSqlGenerator, SemanticSqlGenerator>();
        builder.Services.AddScoped<IBusinessDatabaseReadService, BusinessDatabaseReadService>();

        builder.Services.AddAgentPlugin(registrar =>
        {
            registrar.RegisterPluginFromAssembly(Assembly.GetExecutingAssembly());
        });
    }
}
