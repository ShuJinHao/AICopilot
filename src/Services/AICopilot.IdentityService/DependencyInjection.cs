using AICopilot.AgentPlugin;
using AICopilot.IdentityService.Authorization;
using AICopilot.Services.Contracts;
using AICopilot.Services.CrossCutting.Behaviors;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Reflection;

namespace AICopilot.IdentityService;

public static class DependencyInjection
{
    public static void AddIdentityService(this IHostApplicationBuilder builder)
    {
        builder.Services.AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssembly(Assembly.GetExecutingAssembly());
            cfg.AddBehavior(typeof(IPipelineBehavior<,>), typeof(AuthorizationBehavior<,>));
        });

        builder.Services.AddScoped<IPermissionCatalog, PermissionCatalog>();
        builder.Services.AddScoped<IIdentityAccessService, IdentityAccessService>();

        builder.Services.AddAgentPlugin(registrar =>
        {
            registrar.RegisterPluginFromAssembly(Assembly.GetExecutingAssembly());
        });
    }
}
