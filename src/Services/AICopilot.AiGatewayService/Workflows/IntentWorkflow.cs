using Microsoft.Agents.AI.Hosting;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System;
using System.Collections.Generic;
using System.Text;

namespace AICopilot.AiGatewayService.Workflows;

public static class IntentWorkflow
{
    public static void AddIntentWorkflow(this IHostApplicationBuilder builder)
    {
        // 1. 注册执行器（这些可以是 Transient，没问题）
        builder.Services.AddTransient<IntentRoutingExecutor>();
        builder.Services.AddTransient<ToolsPackExecutor>();
        builder.Services.AddTransient<KnowledgeRetrievalExecutor>();
        builder.Services.AddTransient<ContextAggregatorExecutor>();
        builder.Services.AddTransient<FinalProcessExecutor>();

        // 2. [核心修复] 注册工作流本身为 Transient (使用 AddKeyedTransient)
        // 这样每次请求都会 new 一个新的 Workflow 实例，以及新的 Aggregator 实例
        builder.Services.AddKeyedTransient<Workflow>(nameof(IntentWorkflow), (sp, key) =>
        {
            // 每次都会从容器获取新的执行器实例（状态纯净）
            var intentRouting = sp.GetRequiredService<IntentRoutingExecutor>();
            var toolsPack = sp.GetRequiredService<ToolsPackExecutor>();
            var knowledgeRetrieval = sp.GetRequiredService<KnowledgeRetrievalExecutor>();

            // 重点：这个 aggregator 是全新的，_accumulatedResults 列表是空的
            var aggregator = sp.GetRequiredService<ContextAggregatorExecutor>();
            var finalProcess = sp.GetRequiredService<FinalProcessExecutor>();

            var workflowBuilder = new WorkflowBuilder(intentRouting);
            workflowBuilder.WithName(key as string)
                .AddFanOutEdge(intentRouting, [toolsPack, knowledgeRetrieval])
                .AddFanInEdge([toolsPack, knowledgeRetrieval], aggregator)
                .AddEdge(aggregator, finalProcess);

            return workflowBuilder.Build();
        });
    }
}