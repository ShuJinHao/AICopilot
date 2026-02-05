using AICopilot.Dapper.Security;
using AICopilot.Services.Common.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System;
using System.Collections.Generic;
using System.Text;

namespace AICopilot.Dapper;

public static class DependencyInjection
{
    public static void AddDapper(this IHostApplicationBuilder builder)
    {
        // 注册 SQL 安全服务
        builder.Services.AddSingleton<ISqlGuardrail, KeywordSqlGuardrail>();

        // 注册 数据库连接器
        builder.Services.AddScoped<IDatabaseConnector, DapperDatabaseConnector>();
    }
}