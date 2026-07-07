using System.Text.Json;
using AICopilot.AiGatewayService.Tools;
using AICopilot.Core.AiGateway.Aggregates.AgentTasks;
using AICopilot.Core.AiGateway.Aggregates.Artifacts;
using AICopilot.Core.AiGateway.Aggregates.Tools;
using AICopilot.Core.AiGateway.Specifications.Artifacts;
using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Paging;
using AICopilot.SharedKernel.Repository;
using AICopilot.SharedKernel.Result;

namespace AICopilot.AiGatewayService.AgentTasks;

public sealed class AgentTaskAuditQueryCoordinator(
    IRepository<AgentTask> taskRepository,
    IReadRepository<ArtifactWorkspace> workspaceRepository,
    IToolExecutionAuditStore toolExecutionAuditStore,
    IAuditLogQueryService auditLogQueryService,
    IAgentTaskRunAttemptStore runAttemptStore,
    IAgentTaskRunQueueStore queueStore,
    ICurrentUser currentUser)
{
    private const int MaxSummaryItems = 200;

    public async Task<Result<IReadOnlyCollection<AgentTaskAuditSummaryDto>>> GetSummaryAsync(
        GetAgentTaskAuditSummaryQuery request,
        CancellationToken cancellationToken)
    {
        var taskResult = await LoadTaskAsync(request.Id, cancellationToken);
        if (!taskResult.IsSuccess)
        {
            return Result.From(taskResult);
        }

        var task = taskResult.Value!;
        var workspace = await LoadWorkspaceAsync(task, cancellationToken);
        var taskId = task.Id.Value.ToString();
        var workspaceCode = workspace?.WorkspaceCode;
        var toolRecords = await toolExecutionAuditStore.ListByTaskAsync(task.Id, cancellationToken);
        var auditLogs = await auditLogQueryService.GetListAsync(
            page: 1,
            pageSize: MaxSummaryItems,
            actionGroup: AuditActionGroups.AiGateway,
            actionCode: null,
            targetType: null,
            targetId: null,
            targetName: null,
            operatorUserName: null,
            result: null,
            from: null,
            to: null,
            cancellationToken);

        var items = auditLogs.Items
            .Where(item => BelongsToTask(item, taskId, workspaceCode))
            .Select(item => new AgentTaskAuditSummaryDto(
                item.Id,
                task.Id,
                ResolveWorkspaceCode(item.Metadata, workspaceCode),
                item.ActionCode,
                item.TargetType,
                item.TargetName ?? string.Empty,
                item.Result,
                item.Summary,
                item.CreatedAt,
                item.Metadata))
            .ToList();

        items.AddRange(toolRecords.Select(record => MapToolExecutionRecord(task, workspaceCode, record)));

        var failureSummary = AgentTaskFailureSummaryResolver.Resolve(task, toolRecords);
        if (failureSummary is not null)
        {
            items.Add(MapFailureSummary(task, workspaceCode, failureSummary));
        }

        return Result.Success<IReadOnlyCollection<AgentTaskAuditSummaryDto>>(
            items
                .OrderBy(item => item.CreatedAt)
                .Take(MaxSummaryItems)
                .ToArray());
    }

    public async Task<Result<AgentTaskRunAttemptPageDto>> GetRunAttemptsAsync(
        GetAgentTaskRunAttemptsQuery request,
        CancellationToken cancellationToken)
    {
        var taskResult = await LoadTaskAsync(request.Id, cancellationToken);
        if (!taskResult.IsSuccess)
        {
            return Result.From(taskResult);
        }

        var pagination = new Pagination
        {
            PageNumber = request.PageIndex,
            PageSize = request.PageSize
        };
        var attempts = await runAttemptStore.ListByTaskAsync(taskResult.Value!.Id, cancellationToken);
        var ordered = attempts
            .OrderByDescending(attempt => attempt.StartedAt)
            .ThenByDescending(attempt => attempt.AttemptNo)
            .ToArray();
        var items = ordered
            .Skip(pagination.Skip)
            .Take(pagination.PageSize)
            .Select(AgentTaskRunAttemptDtoMapper.Map)
            .ToArray();
        var totalPages = (int)Math.Ceiling(ordered.Length / (double)pagination.PageSize);

        return Result.Success(new AgentTaskRunAttemptPageDto(
            items,
            pagination.PageNumber,
            pagination.PageSize,
            ordered.Length,
            totalPages,
            pagination.PageNumber > 1,
            pagination.PageNumber < totalPages));
    }

    public async Task<Result<AgentTaskRunQueuePageDto>> GetRunQueueAsync(
        GetAgentTaskRunQueueQuery request,
        CancellationToken cancellationToken)
    {
        var taskResult = await LoadTaskAsync(request.Id, cancellationToken);
        if (!taskResult.IsSuccess)
        {
            return Result.From(taskResult);
        }

        var pagination = new Pagination
        {
            PageNumber = request.PageIndex,
            PageSize = request.PageSize
        };
        var queueItems = await queueStore.ListByTaskAsync(taskResult.Value!.Id, cancellationToken);
        var ordered = queueItems
            .OrderByDescending(item => item.CreatedAt)
            .ThenByDescending(item => item.Id.Value)
            .ToArray();
        var items = ordered
            .Skip(pagination.Skip)
            .Take(pagination.PageSize)
            .Select(AgentTaskRunQueueItemDtoMapper.Map)
            .ToArray();
        var totalPages = (int)Math.Ceiling(ordered.Length / (double)pagination.PageSize);

        return Result.Success(new AgentTaskRunQueuePageDto(
            items,
            pagination.PageNumber,
            pagination.PageSize,
            ordered.Length,
            totalPages,
            pagination.PageNumber > 1,
            pagination.PageNumber < totalPages));
    }

    private Task<Result<AgentTask>> LoadTaskAsync(Guid id, CancellationToken cancellationToken)
    {
        return AgentTaskCommandLoader.LoadTaskAsync(
            taskRepository,
            currentUser,
            id,
            cancellationToken);
    }

    private async Task<ArtifactWorkspace?> LoadWorkspaceAsync(AgentTask task, CancellationToken cancellationToken)
    {
        if (task.WorkspaceId is null)
        {
            return null;
        }

        return await workspaceRepository.FirstOrDefaultAsync(
            new ArtifactWorkspaceByIdSpec(task.WorkspaceId.Value, includeArtifacts: false),
            cancellationToken);
    }

    private static bool BelongsToTask(
        AuditLogSummaryDto item,
        string taskId,
        string? workspaceCode)
    {
        if (string.Equals(item.TargetId, taskId, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (item.Metadata.TryGetValue("taskId", out var metadataTaskId) &&
            string.Equals(metadataTaskId, taskId, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return !string.IsNullOrWhiteSpace(workspaceCode) &&
               item.Metadata.TryGetValue("workspaceCode", out var metadataWorkspaceCode) &&
               string.Equals(metadataWorkspaceCode, workspaceCode, StringComparison.OrdinalIgnoreCase);
    }

    private static string? ResolveWorkspaceCode(
        IReadOnlyDictionary<string, string> metadata,
        string? fallback)
    {
        return metadata.TryGetValue("workspaceCode", out var workspaceCode) &&
               !string.IsNullOrWhiteSpace(workspaceCode)
            ? workspaceCode
            : fallback;
    }

    private static AgentTaskAuditSummaryDto MapToolExecutionRecord(
        AgentTask task,
        string? workspaceCode,
        ToolExecutionRecord record)
    {
        var metadata = ParseAuditMetadata(record.AuditMetadata);
        metadata["toolExecutionId"] = record.Id.Value.ToString();
        metadata["stepId"] = record.StepId.Value.ToString();
        metadata["toolCode"] = record.ToolCode;
        metadata["status"] = record.Status.ToString();
        if (record.RunAttemptId is not null)
        {
            metadata["runAttemptId"] = record.RunAttemptId.Value.Value.ToString();
        }

        if (record.DurationMs.HasValue)
        {
            metadata["durationMs"] = record.DurationMs.Value.ToString();
        }

        if (!string.IsNullOrWhiteSpace(record.ArtifactId))
        {
            metadata["artifactId"] = record.ArtifactId;
        }

        if (!string.IsNullOrWhiteSpace(record.ErrorCode))
        {
            metadata["errorCode"] = record.ErrorCode;
        }

        return new AgentTaskAuditSummaryDto(
            record.Id.Value,
            task.Id,
            ResolveWorkspaceCode(metadata, workspaceCode),
            "Agent.ToolExecutionRecord",
            "ToolExecutionRecord",
            record.ToolCode,
            record.Status.ToString(),
            BuildToolExecutionSummary(record),
            record.StartedAt.UtcDateTime,
            metadata);
    }

    private static AgentTaskAuditSummaryDto MapFailureSummary(
        AgentTask task,
        string? workspaceCode,
        AgentTaskFailureSummaryDto failureSummary)
    {
        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["errorCode"] = failureSummary.ErrorCode,
            ["canRetry"] = failureSummary.CanRetry.ToString(),
            ["nextAction"] = failureSummary.NextAction
        };
        if (failureSummary.StepIndex.HasValue)
        {
            metadata["stepIndex"] = failureSummary.StepIndex.Value.ToString();
        }

        if (!string.IsNullOrWhiteSpace(failureSummary.ToolCode))
        {
            metadata["toolCode"] = failureSummary.ToolCode!;
        }

        if (!string.IsNullOrWhiteSpace(workspaceCode))
        {
            metadata["workspaceCode"] = workspaceCode!;
        }

        return new AgentTaskAuditSummaryDto(
            Guid.NewGuid(),
            task.Id,
            workspaceCode,
            "Agent.FailureSummary",
            "AgentTask",
            task.TaskCode,
            task.Status.ToString(),
            ToolExecutionRecordSanitizer.Sanitize(failureSummary.SafeMessage, 2000) ?? "Agent task failed.",
            (task.CompletedAt ?? task.UpdatedAt).UtcDateTime,
            metadata);
    }

    private static string BuildToolExecutionSummary(ToolExecutionRecord record)
    {
        var message = record.Status switch
        {
            ToolExecutionStatus.Succeeded => record.OutputSummary,
            ToolExecutionStatus.Failed or ToolExecutionStatus.Rejected => record.ErrorMessage,
            _ => record.InputSummary
        };
        return ToolExecutionRecordSanitizer.Sanitize(message, 1000) ??
               $"Tool {record.ToolCode} {record.Status}.";
    }

    private static Dictionary<string, string> ParseAuditMetadata(string? auditMetadata)
    {
        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(auditMetadata))
        {
            return metadata;
        }

        try
        {
            using var document = JsonDocument.Parse(auditMetadata);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                metadata["metadataParseStatus"] = "not_object";
                return metadata;
            }

            foreach (var property in document.RootElement.EnumerateObject())
            {
                var value = property.Value.ValueKind == JsonValueKind.String
                    ? property.Value.GetString()
                    : property.Value.ToString();
                var sanitized = ToolExecutionRecordSanitizer.Sanitize(value, 500);
                if (!string.IsNullOrWhiteSpace(sanitized))
                {
                    metadata[property.Name] = sanitized;
                }
            }
        }
        catch (JsonException)
        {
            metadata["metadataParseStatus"] = "invalid_json";
        }

        return metadata;
    }
}
