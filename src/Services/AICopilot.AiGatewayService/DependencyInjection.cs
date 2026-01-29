using AICopilot.AgentPlugin;
using AICopilot.AiGatewayService.Agents;
using AICopilot.AiGatewayService.Plugins;
using AICopilot.AiGatewayService.Workflows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;

namespace AICopilot.AiGatewayService;

public static class DependencyInjection
{
    public static void AddAiGatewayService(this IHostApplicationBuilder builder)
    {
        builder.Services.AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssembly(Assembly.GetExecutingAssembly());
        });

        builder.Services.AddSingleton<ChatAgentFactory>();

        builder.Services.AddHttpClient("OpenAI", client =>
        {
            client.Timeout = TimeSpan.FromSeconds(30);
        });

        //builder.Services.AddScoped<TimeAgentPlugin>();

        builder.Services.AddAgentPlugin(registrar =>
        {
            registrar.RegisterPluginFromAssembly(Assembly.GetExecutingAssembly());
        });

        builder.Services.AddSingleton<IntentRoutingAgentBuilder>();

        builder.AddIntentWorkflow();
    }
}