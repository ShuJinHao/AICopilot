using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AICopilot.AgentPlugin;

public static class AgentPluginExtensions
{
    public static IServiceCollection AddAgentPlugin(
        this IServiceCollection services,
        Action<IAgentPluginRegistrar> configure)
    {
        var registrar = new AgentPluginRegistrar();
        configure(registrar);

        services.AddSingleton<IAgentPluginRegistrar>(registrar);
        services.TryAddSingleton<AgentPluginLoader>();
        services.TryAddSingleton<IAgentPluginCatalog>(sp => sp.GetRequiredService<AgentPluginLoader>());
        services.TryAddSingleton<IAgentPluginRegistry>(sp => sp.GetRequiredService<AgentPluginLoader>());

        return services;
    }
}
