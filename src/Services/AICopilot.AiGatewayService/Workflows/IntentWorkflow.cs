using Microsoft.Agents.AI.Hosting;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using AICopilot.AiGatewayService.Workflows.Executors;

namespace AICopilot.AiGatewayService.Workflows;

public static class IntentWorkflow
{
    public static void AddIntentWorkflow(this IHostApplicationBuilder builder)
    {
        builder.Services.AddScoped<IntentRoutingExecutor>();
        builder.Services.AddScoped<ToolsPackExecutor>();
        builder.Services.AddScoped<KnowledgeRetrievalExecutor>();
        builder.Services.AddScoped<DataAnalysisExecutor>();
        builder.Services.AddScoped<ContextAggregatorExecutor>();
        builder.Services.AddScoped<FinalAgentBuildExecutor>();
        builder.Services.AddScoped<FinalAgentRunExecutor>();
        builder.Services.AddScoped<WorkflowFactory>();
        // 👇 新增这段代码：告诉 DI 容器如何创建名为 "IntentWorkflow" 的 Workflow
        builder.Services.AddKeyedScoped<Workflow>(nameof(IntentWorkflow), (sp, key) =>
        {
            // 从容器中解析出工厂
            var factory = sp.GetRequiredService<WorkflowFactory>();
            // 调用工厂方法创建 Workflow 并返回
            return factory.CreateIntentWorkflow();
        });
    }
}