using AICopilot.AgentPlugin;
using AICopilot.AiGatewayService.Agents;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Agents.AI.Workflows.Reflection;
using Microsoft.Extensions.AI;
using System;
using System.Collections.Generic;
using System.Text;

namespace AICopilot.AiGatewayService.Workflows;

public class ToolsPackExecutor(AgentPluginLoader pluginLoader) :
    ReflectingExecutor<ToolsPackExecutor>("ToolsPackExecutor"),
    IMessageHandler<List<IntentResult>, AITool[]>
{
    public async ValueTask<AITool[]> HandleAsync(List<IntentResult> intentResults, IWorkflowContext context,
        CancellationToken cancellationToken = new())
    {
        try
        {
            var intent = intentResults
                .Where(i => i.Confidence >= 0.9)
                .Select(i => i.Intent).ToArray();
            var tools = pluginLoader.GetAITools(intent);

            return tools;
        }
        catch (Exception e)
        {
            await context.AddEventAsync(new ExecutorFailedEvent(Id, e), cancellationToken);
            throw;
        }
    }
}