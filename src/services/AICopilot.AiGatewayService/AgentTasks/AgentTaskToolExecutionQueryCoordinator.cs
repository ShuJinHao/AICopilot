using AICopilot.AiGatewayService.Tools;
using AICopilot.Core.AiGateway.Aggregates.AgentTasks;
using AICopilot.Core.AiGateway.Aggregates.Tools;
using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Paging;
using AICopilot.SharedKernel.Repository;
using AICopilot.SharedKernel.Result;

namespace AICopilot.AiGatewayService.AgentTasks;

public sealed class AgentTaskToolExecutionQueryCoordinator(
    IRepository<AgentTask> taskRepository,
    IToolExecutionAuditStore toolExecutionAuditStore,
    ICurrentUser currentUser)
{
    public async Task<Result<ToolExecutionRecordPageDto>> GetAsync(
        GetAgentTaskToolExecutionsQuery request,
        CancellationToken cancellationToken)
    {
        var taskResult = await AgentTaskCommandLoader.LoadTaskAsync(
            taskRepository,
            currentUser,
            request.Id,
            cancellationToken);
        if (!taskResult.IsSuccess)
        {
            return Result.From(taskResult);
        }

        ToolExecutionStatus? status = null;
        if (!string.IsNullOrWhiteSpace(request.Status))
        {
            if (!Enum.TryParse<ToolExecutionStatus>(request.Status, ignoreCase: true, out var parsedStatus))
            {
                return Result.Invalid("Tool execution status is invalid.");
            }

            status = parsedStatus;
        }

        var task = taskResult.Value!;
        var pagination = new Pagination
        {
            PageNumber = request.PageIndex,
            PageSize = request.PageSize
        };
        var records = await toolExecutionAuditStore.ListByTaskAsync(task.Id, cancellationToken);

        var query = records.AsEnumerable();
        if (status.HasValue)
        {
            query = query.Where(record => record.Status == status.Value);
        }

        if (!string.IsNullOrWhiteSpace(request.ToolCode))
        {
            var toolCode = request.ToolCode.Trim();
            query = query.Where(record => string.Equals(record.ToolCode, toolCode, StringComparison.OrdinalIgnoreCase));
        }

        var ordered = query
            .OrderByDescending(record => record.StartedAt)
            .ThenByDescending(record => record.Id.Value)
            .ToArray();
        var items = ordered
            .Skip(pagination.Skip)
            .Take(pagination.PageSize)
            .Select(ToolRegistrationMapper.Map)
            .ToArray();
        var totalPages = (int)Math.Ceiling(ordered.Length / (double)pagination.PageSize);

        return Result.Success(new ToolExecutionRecordPageDto(
            items,
            pagination.PageNumber,
            pagination.PageSize,
            ordered.Length,
            totalPages,
            pagination.PageNumber > 1,
            pagination.PageNumber < totalPages));
    }
}
