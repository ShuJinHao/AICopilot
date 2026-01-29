using AICopilot.AgentPlugin;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;

namespace AICopilot.IdentityService;

public static class DependencyInjection
{
    public static void AddIdentityService(this IHostApplicationBuilder builder)
    {
        builder.Services.AddAgentPlugin(registrar =>
        {
            registrar.RegisterPluginFromAssembly(Assembly.GetExecutingAssembly());
        });
    }
}