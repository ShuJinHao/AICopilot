using AICopilot.Core.AiGateway.Aggregates.Tools;
using AICopilot.Core.AiGateway.Ids;
using AICopilot.Services.Contracts;
using Microsoft.EntityFrameworkCore;

namespace AICopilot.EntityFrameworkCore.Repository;

public sealed class ToolExecutionAuditStore(AiGatewayDbContext dbContext) : IToolExecutionAuditStore
{
    public ToolExecutionRecord Add(ToolExecutionRecord record)
    {
        dbContext.ToolExecutionRecords.Add(record);
        return record;
    }

    public async Task<List<ToolExecutionRecord>> ListByTaskAsync(
        AgentTaskId taskId,
        CancellationToken cancellationToken = default)
    {
        return await dbContext.ToolExecutionRecords
            .Where(record => record.TaskId == taskId)
            .ToListAsync(cancellationToken);
    }
}
