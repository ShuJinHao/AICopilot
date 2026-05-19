using AICopilot.Core.AiGateway.Aggregates.AgentTasks;
using AICopilot.Core.AiGateway.Aggregates.Artifacts;
using AICopilot.Core.AiGateway.Aggregates.Tools;
using AICopilot.SharedKernel.Result;

namespace AICopilot.AiGatewayService.AgentTasks;

internal sealed record AgentToolExecutionContext(
    AgentTask Task,
    ArtifactWorkspace Workspace,
    AgentTaskPlanDocument Plan,
    AgentStep Step,
    AgentTaskRunState State,
    ToolRegistration ToolRegistration,
    CancellationToken CancellationToken);

internal sealed record AgentToolExecutionResult(object Output)
{
    public static AgentToolExecutionResult From(object output) => new(output);
}

internal interface IAgentToolExecutor
{
    bool CanExecute(ToolRegistration tool, AgentStep step);

    Task<AgentToolExecutionResult> ExecuteAsync(AgentToolExecutionContext context);
}

internal sealed class AgentToolExecutorResolver(IEnumerable<IAgentToolExecutor> executors)
{
    private readonly IAgentToolExecutor[] executors = executors.ToArray();

    public IAgentToolExecutor Resolve(ToolRegistration tool, AgentStep step)
    {
        return executors.FirstOrDefault(executor => executor.CanExecute(tool, step))
               ?? throw new AgentToolExecutionException(
                   AppProblemCodes.ToolExecutionNotFound,
                   $"No runtime executor is available for tool '{tool.ToolCode}'.");
    }
}

internal sealed class AgentToolExecutionException(string code, string message) : InvalidOperationException(message)
{
    public string Code { get; } = code;
}
