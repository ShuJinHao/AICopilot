using AICopilot.Core.AiGateway.Aggregates.AgentTasks;
using AICopilot.Core.AiGateway.Aggregates.Artifacts;
using AICopilot.Core.AiGateway.Runtime.AgentExecution;
using Microsoft.EntityFrameworkCore;

namespace AICopilot.EntityFrameworkCore.Repository;

internal static class AgentExecutionRowLock
{
    public static Task<TEntity?> ByIdAsync<TEntity>(
        AiGatewayDbContext context,
        Guid id,
        CancellationToken cancellationToken)
        where TEntity : class
    {
        var (table, alias) = typeof(TEntity) switch
        {
            var type when type == typeof(AgentTask) => ("agent_tasks", "task"),
            var type when type == typeof(AgentTaskRunAttempt) => ("agent_task_run_attempts", "attempt"),
            var type when type == typeof(AgentTaskRunQueueItem) => ("agent_task_run_queue_items", "queue_item"),
            var type when type == typeof(AgentNodeRun) => ("agent_node_runs", "node"),
            _ => throw new InvalidOperationException($"Unsupported agent execution row lock type '{typeof(TEntity).FullName}'.")
        };
        var sql = $"SELECT {alias}.*, {alias}.xmin FROM aigateway.{table} AS {alias} WHERE id = {{0}} FOR UPDATE";
        return context.Set<TEntity>()
            .FromSqlRaw(sql, id)
            .SingleOrDefaultAsync(cancellationToken);
    }

    public static Task<List<TEntity>> ByAggregateOwnerAsync<TEntity>(
        AiGatewayDbContext context,
        Guid ownerId,
        CancellationToken cancellationToken)
        where TEntity : class
    {
        var (table, alias, ownerColumn, orderColumn) = typeof(TEntity) switch
        {
            var type when type == typeof(Artifact) => ("artifacts", "artifact", "workspace_id", "id"),
            var type when type == typeof(AgentStep) => ("agent_steps", "step", "task_id", "step_index"),
            _ => throw new InvalidOperationException($"Unsupported aggregate-owned row lock type '{typeof(TEntity).FullName}'.")
        };
        var sql = $"SELECT {alias}.*, {alias}.xmin FROM aigateway.{table} AS {alias} WHERE {ownerColumn} = {{0}} ORDER BY {orderColumn} FOR UPDATE";
        return context.Set<TEntity>().FromSqlRaw(sql, ownerId).ToListAsync(cancellationToken);
    }
}
