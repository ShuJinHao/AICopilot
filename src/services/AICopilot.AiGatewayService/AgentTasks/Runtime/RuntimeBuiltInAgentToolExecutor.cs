using AICopilot.Core.AiGateway.Aggregates.AgentTasks;
using AICopilot.Core.AiGateway.Aggregates.Tools;

namespace AICopilot.AiGatewayService.AgentTasks;

internal sealed class RuntimeBuiltInAgentToolExecutor(
    Func<AgentToolExecutionContext, Task<object>> execute)
    : IAgentToolExecutor
{
    public bool CanExecute(ToolRegistration tool, AgentStep step)
    {
        return tool.ProviderType is ToolProviderType.BuiltIn or ToolProviderType.Artifact or ToolProviderType.CloudReadonly &&
               tool.TargetType == ToolRegistrationTargetType.AgentRuntime &&
               string.Equals(tool.TargetName, "AgentTaskRuntime", StringComparison.OrdinalIgnoreCase);
    }

    public async Task<AgentToolExecutionResult> ExecuteAsync(AgentToolExecutionContext context)
    {
        return AgentToolExecutionResult.From(await execute(context));
    }
}
