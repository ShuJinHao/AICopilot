using AICopilot.Core.AiGateway.Aggregates.Tools;
using AICopilot.Core.AiGateway.Ids;

namespace AICopilot.Services.Contracts;

public interface IToolExecutionAuditStore
{
    ToolExecutionRecord Add(ToolExecutionRecord record);

    Task<List<ToolExecutionRecord>> ListByTaskAsync(
        AgentTaskId taskId,
        CancellationToken cancellationToken = default);
}
