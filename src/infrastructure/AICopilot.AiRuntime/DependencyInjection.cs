using AICopilot.Services.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace AICopilot.AiRuntime;

public static class DependencyInjection
{
    public static void AddAiRuntime(this IHostApplicationBuilder builder)
    {
        builder.Services.AddSingleton<IModelProviderHealth, AlwaysHealthyModelProviderHealth>();
        builder.Services.AddSingleton<IModelFallbackPolicy, DefaultModelFallbackPolicy>();
        builder.Services.AddSingleton<IModelCircuitBreaker, InMemoryModelCircuitBreaker>();
        builder.Services.AddSingleton<IModelCostBudgetPolicy, NoOpModelCostBudgetPolicy>();
        builder.Services.AddSingleton<IAgentRuntimeFactory, AgentRuntimeFactory>();
    }
}
