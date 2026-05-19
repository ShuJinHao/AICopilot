using System.Text.Json;
using AICopilot.AiGatewayService.Tools;
using AICopilot.Core.AiGateway.Aggregates.AgentTasks;
using AICopilot.Core.AiGateway.Aggregates.Artifacts;
using AICopilot.Core.AiGateway.Aggregates.Tools;
using AICopilot.Core.AiGateway.Ids;
using AICopilot.Core.AiGateway.Specifications.AgentTasks;
using AICopilot.Core.AiGateway.Specifications.Artifacts;
using AICopilot.Services.Contracts;
using AICopilot.Services.CrossCutting.Attributes;
using AICopilot.SharedKernel.Messaging;
using AICopilot.SharedKernel.Paging;
using AICopilot.SharedKernel.Repository;
using AICopilot.SharedKernel.Result;

namespace AICopilot.AiGatewayService.AgentTasks;

public sealed record AgentTaskAuditSummaryDto(
    Guid Id,
    Guid TaskId,
    string? WorkspaceCode,
    string ActionCode,
    string TargetType,
    string TargetName,
    string Result,
    string Summary,
    DateTime CreatedAt,
    IReadOnlyDictionary<string, string> Metadata);

[AuthorizeRequirement("AiGateway.GetAgentTask")]
public sealed record GetAgentTaskAuditSummaryQuery(Guid Id)
    : IQuery<Result<IReadOnlyCollection<AgentTaskAuditSummaryDto>>>;

[AuthorizeRequirement("AiGateway.GetAgentTask")]
public sealed record GetAgentTaskToolExecutionsQuery(
    Guid Id,
    int PageIndex = 1,
    int PageSize = 20,
    string? Status = null,
    string? ToolCode = null) : IQuery<Result<ToolExecutionRecordPageDto>>;

[AuthorizeRequirement("AiGateway.GetAgentTask")]
public sealed record GetAgentTaskRunAttemptsQuery(
    Guid Id,
    int PageIndex = 1,
    int PageSize = 20) : IQuery<Result<AgentTaskRunAttemptPageDto>>;

[AuthorizeRequirement("AiGateway.GetAgentTask")]
public sealed record GetAgentTaskRunQueueQuery(
    Guid Id,
    int PageIndex = 1,
    int PageSize = 20) : IQuery<Result<AgentTaskRunQueuePageDto>>;

public sealed class GetAgentTaskAuditSummaryQueryHandler(
    IRepository<AgentTask> taskRepository,
    IReadRepository<ArtifactWorkspace> workspaceRepository,
    IReadRepository<ToolExecutionRecord> toolExecutionRepository,
    IAuditLogQueryService auditLogQueryService,
    ICurrentUser currentUser)
    : IQueryHandler<GetAgentTaskAuditSummaryQuery, Result<IReadOnlyCollection<AgentTaskAuditSummaryDto>>>
{
    private const int MaxSummaryItems = 200;

    public async Task<Result<IReadOnlyCollection<AgentTaskAuditSummaryDto>>> Handle(
        GetAgentTaskAuditSummaryQuery request,
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

        var task = taskResult.Value!;
        var workspace = await LoadWorkspaceAsync(task, cancellationToken);
        var taskId = task.Id.Value.ToString();
        var workspaceCode = workspace?.WorkspaceCode;
        var toolRecords = await toolExecutionRepository.GetListAsync(
            record => record.TaskId == task.Id,
            cancellationToken);
        var auditLogs = await auditLogQueryService.GetListAsync(
            page: 1,
            pageSize: MaxSummaryItems,
            actionGroup: AuditActionGroups.AiGateway,
            actionCode: null,
            targetType: null,
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

public sealed class GetAgentTaskToolExecutionsQueryHandler(
    IRepository<AgentTask> taskRepository,
    IReadRepository<ToolExecutionRecord> toolExecutionRepository,
    ICurrentUser currentUser)
    : IQueryHandler<GetAgentTaskToolExecutionsQuery, Result<ToolExecutionRecordPageDto>>
{
    public async Task<Result<ToolExecutionRecordPageDto>> Handle(
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
        var records = await toolExecutionRepository.GetListAsync(
            record => record.TaskId == task.Id,
            cancellationToken);

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

public sealed class GetAgentTaskRunAttemptsQueryHandler(
    IRepository<AgentTask> taskRepository,
    IReadRepository<AgentTaskRunAttempt> runAttemptRepository,
    ICurrentUser currentUser)
    : IQueryHandler<GetAgentTaskRunAttemptsQuery, Result<AgentTaskRunAttemptPageDto>>
{
    public async Task<Result<AgentTaskRunAttemptPageDto>> Handle(
        GetAgentTaskRunAttemptsQuery request,
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

        var pagination = new Pagination
        {
            PageNumber = request.PageIndex,
            PageSize = request.PageSize
        };
        var attempts = await runAttemptRepository.ListAsync(
            new AgentTaskRunAttemptsByTaskSpec(taskResult.Value!.Id),
            cancellationToken);
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
}

public sealed class GetAgentTaskRunQueueQueryHandler(
    IRepository<AgentTask> taskRepository,
    IReadRepository<AgentTaskRunQueueItem> queueRepository,
    ICurrentUser currentUser)
    : IQueryHandler<GetAgentTaskRunQueueQuery, Result<AgentTaskRunQueuePageDto>>
{
    public async Task<Result<AgentTaskRunQueuePageDto>> Handle(
        GetAgentTaskRunQueueQuery request,
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

        var pagination = new Pagination
        {
            PageNumber = request.PageIndex,
            PageSize = request.PageSize
        };
        var queueItems = await queueRepository.ListAsync(
            new AgentTaskRunQueueItemsByTaskSpec(taskResult.Value!.Id),
            cancellationToken);
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
}
