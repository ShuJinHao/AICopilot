using Microsoft.Extensions.Hosting;
using System;
using System.Collections.Generic;
using System.Text;
using AICopilot.Dapper;
using AICopilot.AgentPlugin;
using System.Reflection;

namespace AICopilot.DataAnalysisService;

public static class DependencyInjection
{
    public static void AddDataAnalysisService(this IHostApplicationBuilder builder)
    {
        // 注册 Dapper 基础服务
        builder.AddDapper();
        // 注册插件加载器
        builder.Services.AddAgentPlugin(registrar =>
        {
            registrar.RegisterPluginFromAssembly(Assembly.GetExecutingAssembly());
        });
    }
}