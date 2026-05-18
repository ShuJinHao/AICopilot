using System.Security.Cryptography;
using System.Text;
using AICopilot.AiGatewayService.Tools;
using AICopilot.Core.AiGateway.Aggregates.AgentTasks;
using AICopilot.Core.AiGateway.Ids;
using AICopilot.Core.AiGateway.Specifications.AgentTasks;
using AICopilot.Services.Contracts;
using AICopilot.Services.CrossCutting.Attributes;
using AICopilot.SharedKernel.Messaging;
using AICopilot.SharedKernel.Paging;
using AICopilot.SharedKernel.Repository;
using AICopilot.SharedKernel.Result;
using Microsoft.Extensions.Options;

namespace AICopilot.AiGatewayService.AgentTasks;

public interface IAgentWorkspaceFingerprintProvider
{
    string GetWorkspaceRootHash();
}

internal sealed class AgentWorkspaceFingerprintProvider(IArtifactWorkspaceFileStore fileStore)
    : IAgentWorkspaceFingerprintProvider
{
    public string GetWorkspaceRootHash()
    {
        var settings = fileStore.GetSettings();
        var fullPath = Path.GetFullPath(settings.RootPath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var normalized = fullPath.ToUpperInvariant();
        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }
}

internal interface IAgentWorkerHeartbeatService
{
    Task MarkAsync(
        string workerId,
        string workerName,
        string version,
        AgentTaskRunQueueItem? activeQueueItem,
        CancellationToken cancellationToken);
}

internal sealed class AgentWorkerHeartbeatService(
    IRepository<AgentWorkerHeartbeat> heartbeatRepository,
    IAgentWorkspaceFingerprintProvider workspaceFingerprintProvider)
    : IAgentWorkerHeartbeatService
{
    public async Task MarkAsync(
        string workerId,
        string workerName,
        string version,
        AgentTaskRunQueueItem? activeQueueItem,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var workspaceRootHash = workspaceFingerprintProvider.GetWorkspaceRootHash();
        var heartbeat = await heartbeatRepository.FirstOrDefaultAsync(
            new AgentWorkerHeartbeatByWorkerIdSpec(workerId),
            cancellationToken);

        if (heartbeat is null)
        {
            heartbeat = new AgentWorkerHeartbeat(workerId, workerName, now, workspaceRootHash, version);
            heartbeat.MarkSeen(
                now,
                workerName,
                workspaceRootHash,
                version,
                activeQueueItem?.Id,
                activeQueueItem?.TaskId);
            heartbeatRepository.Add(heartbeat);
        }
        else
        {
            heartbeat.MarkSeen(
                now,
                workerName,
                workspaceRootHash,
                version,
                activeQueueItem?.Id,
                activeQueueItem?.TaskId);
            heartbeatRepository.Update(heartbeat);
        }

        await heartbeatRepository.SaveChangesAsync(cancellationToken);
    }
}

[AuthorizeRequirement("AiGateway.RunQueue.Read")]
public sealed record GetAgentRunQueueQuery(
    int PageIndex = 1,
    int PageSize = 20,
    string? Status = null,
    string? TriggerType = null,
    Guid? TaskId = null,
    Guid? RequestedBy = null,
    DateTimeOffset? From = null,
    DateTimeOffset? To = null) : IQuery<Result<AgentRunQueuePageDto>>;

[AuthorizeRequirement("AiGateway.RunQueue.Read")]
public sealed record GetAgentRunQueueSummaryQuery : IQuery<Result<AgentRunQueueSummaryDto>>;

[AuthorizeRequirement("AiGateway.WorkerStatus.Read")]
public sealed record GetAgentWorkerStatusQuery : IQuery<Result<AgentWorkerStatusDto>>;

[AuthorizeRequirement("AiGateway.RunQueue.Manage")]
public sealed record DeadLetterAgentRunQueueItemCommand(Guid Id, string? Reason = null) : ICommand<Result<AgentRunQueueItemDto>>;

public sealed class GetAgentRunQueueQueryHandler(
    IReadRepository<AgentTaskRunQueueItem> queueRepository)
    : IQueryHandler<GetAgentRunQueueQuery, Result<AgentRunQueuePageDto>>
{
    public async Task<Result<AgentRunQueuePageDto>> Handle(
        GetAgentRunQueueQuery request,
        CancellationToken cancellationToken)
    {
        if (!TryParseFilter<AgentTaskRunQueueStatus>(request.Status, out var status, out var statusError))
        {
            return Result.Invalid(statusError);
        }

        if (!TryParseFilter<AgentTaskRunTriggerType>(request.TriggerType, out var triggerType, out var triggerError))
        {
            return Result.Invalid(triggerError);
        }

        var pagination = new Pagination
        {
            PageNumber = request.PageIndex,
            PageSize = request.PageSize
        };
        var queueItems = await queueRepository.ListAsync(
            new AgentTaskRunQueueAllItemsSpec(),
            cancellationToken);

        var query = queueItems.AsEnumerable();
        if (status.HasValue)
        {
            query = query.Where(item => item.Status == status.Value);
        }

        if (triggerType.HasValue)
        {
            query = query.Where(item => item.TriggerType == triggerType.Value);
        }

        if (request.TaskId.HasValue)
        {
            var taskId = new AgentTaskId(request.TaskId.Value);
            query = query.Where(item => item.TaskId == taskId);
        }

        if (request.RequestedBy.HasValue)
        {
            query = query.Where(item => item.RequestedBy == request.RequestedBy.Value);
        }

        if (request.From.HasValue)
        {
            query = query.Where(item => item.CreatedAt >= request.From.Value);
        }

        if (request.To.HasValue)
        {
            query = query.Where(item => item.CreatedAt <= request.To.Value);
        }

        var ordered = query
            .OrderByDescending(item => item.CreatedAt)
            .ThenByDescending(item => item.Id.Value)
            .ToArray();
        var items = ordered
            .Skip(pagination.Skip)
            .Take(pagination.PageSize)
            .Select(AgentTaskRunQueueItemDtoMapper.MapGlobal)
            .ToArray();
        var totalPages = (int)Math.Ceiling(ordered.Length / (double)pagination.PageSize);

        return Result.Success(new AgentRunQueuePageDto(
            items,
            pagination.PageNumber,
            pagination.PageSize,
            ordered.Length,
            totalPages,
            pagination.PageNumber > 1,
            pagination.PageNumber < totalPages));
    }

    private static bool TryParseFilter<TEnum>(
        string? value,
        out TEnum? parsed,
        out string error)
        where TEnum : struct, Enum
    {
        parsed = null;
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        if (Enum.TryParse<TEnum>(value, ignoreCase: true, out var result))
        {
            parsed = result;
            return true;
        }

        error = $"{typeof(TEnum).Name} filter is invalid.";
        return false;
    }
}

public sealed class GetAgentRunQueueSummaryQueryHandler(
    IReadRepository<AgentTaskRunQueueItem> queueRepository,
    IReadRepository<AgentWorkerHeartbeat> heartbeatRepository,
    IAgentWorkspaceFingerprintProvider? workspaceFingerprintProvider = null,
    IOptions<AgentRunQueueOptions>? options = null)
    : IQueryHandler<GetAgentRunQueueSummaryQuery, Result<AgentRunQueueSummaryDto>>
{
    public async Task<Result<AgentRunQueueSummaryDto>> Handle(
        GetAgentRunQueueSummaryQuery request,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var queueItems = await queueRepository.ListAsync(
            new AgentTaskRunQueueAllItemsSpec(),
            cancellationToken);
        var heartbeats = await heartbeatRepository.ListAsync(
            new AgentWorkerHeartbeatAllSpec(),
            cancellationToken);

        var httpApiWorkspaceHash = workspaceFingerprintProvider?.GetWorkspaceRootHash();
        return Result.Success(BuildSummary(
            queueItems,
            heartbeats,
            now,
            httpApiWorkspaceHash,
            options?.Value.HeartbeatActiveWindow));
    }

    internal static AgentRunQueueSummaryDto BuildSummary(
        IReadOnlyCollection<AgentTaskRunQueueItem> queueItems,
        IReadOnlyCollection<AgentWorkerHeartbeat> heartbeats,
        DateTimeOffset nowUtc,
        string? httpApiWorkspaceRootHash = null,
        TimeSpan? activeHeartbeatWindow = null)
    {
        var activeSince = nowUtc.Subtract(activeHeartbeatWindow ?? AgentWorkerStatusCalculator.DefaultActiveHeartbeatWindow);
        var activeHeartbeats = heartbeats
            .Where(heartbeat => heartbeat.LastSeenAt >= activeSince)
            .ToArray();
        var oldestQueuedAt = queueItems
            .Where(item => item.Status == AgentTaskRunQueueStatus.Queued)
            .Select(item => (DateTimeOffset?)item.AvailableAt)
            .Order()
            .FirstOrDefault();
        return new AgentRunQueueSummaryDto(
            queueItems.Count(item => item.Status == AgentTaskRunQueueStatus.Queued),
            queueItems.Count(item => item.Status == AgentTaskRunQueueStatus.Leased),
            queueItems.Count(item => item.Status == AgentTaskRunQueueStatus.Succeeded),
            queueItems.Count(item => item.Status == AgentTaskRunQueueStatus.Failed),
            queueItems.Count(item => item.Status == AgentTaskRunQueueStatus.Cancelled),
            queueItems.Count(item => item.Status == AgentTaskRunQueueStatus.DeadLetter),
            queueItems.Count(item => item.Status == AgentTaskRunQueueStatus.Leased &&
                                     item.LeaseExpiresAt.HasValue &&
                                     item.LeaseExpiresAt.Value <= nowUtc),
            oldestQueuedAt,
            AverageMilliseconds(queueItems
                .Where(item => item.StartedAt.HasValue)
                .Select(item => item.StartedAt!.Value - item.AvailableAt)),
            AverageMilliseconds(queueItems
                .Where(item => item.StartedAt.HasValue && item.CompletedAt.HasValue)
                .Select(item => item.CompletedAt!.Value - item.StartedAt!.Value)),
            oldestQueuedAt.HasValue
                ? NonNegativeMilliseconds(nowUtc - oldestQueuedAt.Value)
                : null,
            activeHeartbeats.Length,
            string.IsNullOrWhiteSpace(httpApiWorkspaceRootHash)
                ? 0
                : activeHeartbeats.Count(heartbeat => heartbeat.WorkspaceRootHash != httpApiWorkspaceRootHash),
            nowUtc);
    }

    private static long? AverageMilliseconds(IEnumerable<TimeSpan> durations)
    {
        var values = durations
            .Select(NonNegativeMilliseconds)
            .ToArray();
        return values.Length == 0
            ? null
            : (long)Math.Round(values.Average(), MidpointRounding.AwayFromZero);
    }

    private static long NonNegativeMilliseconds(TimeSpan duration)
    {
        return Math.Max(0, (long)duration.TotalMilliseconds);
    }
}

public sealed class GetAgentWorkerStatusQueryHandler(
    IReadRepository<AgentTaskRunQueueItem> queueRepository,
    IReadRepository<AgentWorkerHeartbeat> heartbeatRepository,
    IAgentWorkspaceFingerprintProvider workspaceFingerprintProvider,
    IOptions<AgentRunQueueOptions>? options = null)
    : IQueryHandler<GetAgentWorkerStatusQuery, Result<AgentWorkerStatusDto>>
{
    public async Task<Result<AgentWorkerStatusDto>> Handle(
        GetAgentWorkerStatusQuery request,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var queueItems = await queueRepository.ListAsync(
            new AgentTaskRunQueueAllItemsSpec(),
            cancellationToken);
        var heartbeats = await heartbeatRepository.ListAsync(
            new AgentWorkerHeartbeatAllSpec(),
            cancellationToken);
        var httpApiWorkspaceHash = workspaceFingerprintProvider.GetWorkspaceRootHash();
        return Result.Success(AgentWorkerStatusCalculator.Build(
            queueItems,
            heartbeats,
            httpApiWorkspaceHash,
            now,
            options?.Value.HeartbeatActiveWindow));
    }
}

public sealed class DeadLetterAgentRunQueueItemCommandHandler(
    IRepository<AgentTaskRunQueueItem> queueRepository,
    IAuditLogWriter auditLogWriter)
    : ICommandHandler<DeadLetterAgentRunQueueItemCommand, Result<AgentRunQueueItemDto>>
{
    public async Task<Result<AgentRunQueueItemDto>> Handle(
        DeadLetterAgentRunQueueItemCommand request,
        CancellationToken cancellationToken)
    {
        var item = await queueRepository.FirstOrDefaultAsync(
            new AgentTaskRunQueueItemByIdSpec(new AgentTaskRunQueueItemId(request.Id)),
            cancellationToken);
        if (item is null)
        {
            return Result.NotFound(AppProblemCodes.AgentTaskRunQueueNotFound);
        }

        var now = DateTimeOffset.UtcNow;
        var oldStatus = item.Status.ToString();
        if (!item.CanMoveToDeadLetter(now))
        {
            return Result.Failure(new ApiProblemDescriptor(
                AppProblemCodes.AgentRunQueueDeadLetterNotAllowed,
                "Only queued, failed, or expired leased queue items can be moved to dead letter."));
        }

        var safeMessage = ToolExecutionRecordSanitizer.Sanitize(request.Reason, 1000)
                          ?? item.SafeMessage
                          ?? "Agent run queue item moved to dead letter by operator.";
        var failureCode = item.FailureCode ?? AppProblemCodes.AgentRunQueueOperationDenied;
        item.MarkDeadLetter(failureCode, safeMessage, now);
        queueRepository.Update(item);
        await queueRepository.SaveChangesAsync(cancellationToken);

        await auditLogWriter.WriteAsync(
            new AuditLogWriteRequest(
                AuditActionGroups.AiGateway,
                "Agent.RunQueueDeadLetter",
                "AgentTaskRunQueueItem",
                item.Id.Value.ToString(),
                item.TaskId.Value.ToString(),
                AuditResults.Succeeded,
                safeMessage,
                Metadata: new Dictionary<string, string>
                {
                    ["queueItemId"] = item.Id.Value.ToString(),
                    ["taskId"] = item.TaskId.Value.ToString(),
                    ["attemptId"] = item.RunAttemptId?.Value.ToString() ?? string.Empty,
                    ["triggerType"] = item.TriggerType.ToString(),
                    ["oldStatus"] = oldStatus,
                    ["newStatus"] = item.Status.ToString(),
                    ["failureCode"] = failureCode,
                    ["retryAttemptNo"] = string.Empty,
                    ["availableAt"] = item.AvailableAt.ToString("O")
                }),
            cancellationToken);

        return Result.Success(AgentTaskRunQueueItemDtoMapper.MapGlobal(item));
    }
}

internal static class AgentWorkerStatusCalculator
{
    public static readonly TimeSpan DefaultActiveHeartbeatWindow = TimeSpan.FromSeconds(30);

    public static AgentWorkerStatusDto Build(
        IReadOnlyCollection<AgentTaskRunQueueItem> queueItems,
        IReadOnlyCollection<AgentWorkerHeartbeat> heartbeats,
        string httpApiWorkspaceRootHash,
        DateTimeOffset nowUtc,
        TimeSpan? activeHeartbeatWindow = null)
    {
        var activeSince = nowUtc.Subtract(activeHeartbeatWindow ?? DefaultActiveHeartbeatWindow);
        var workers = heartbeats
            .OrderByDescending(heartbeat => heartbeat.LastSeenAt)
            .Select(heartbeat => MapHeartbeat(
                heartbeat,
                heartbeat.LastSeenAt >= activeSince,
                httpApiWorkspaceRootHash))
            .ToArray();
        var activeWorkers = workers.Where(worker => worker.IsActive).ToArray();
        var hasActiveWorkers = activeWorkers.Length > 0;
        var workspaceConsistent = hasActiveWorkers &&
                                  activeWorkers.All(worker => worker.WorkspaceMatchesHttpApi);
        var statusCode = !hasActiveWorkers
            ? AppProblemCodes.AgentWorkerUnavailable
            : workspaceConsistent
                ? "healthy"
                : AppProblemCodes.AgentWorkerWorkspaceMismatch;

        return new AgentWorkerStatusDto(
            statusCode,
            hasActiveWorkers,
            workspaceConsistent,
            httpApiWorkspaceRootHash,
            activeWorkers.Length,
            queueItems.Count(item => item.Status == AgentTaskRunQueueStatus.Queued),
            queueItems.Count(item => item.Status == AgentTaskRunQueueStatus.Leased),
            queueItems.Count(item => item.Status == AgentTaskRunQueueStatus.Leased &&
                                     item.LeaseExpiresAt.HasValue &&
                                     item.LeaseExpiresAt.Value <= nowUtc),
            queueItems
                .Where(item => item.Status == AgentTaskRunQueueStatus.Queued)
                .Select(item => (DateTimeOffset?)item.AvailableAt)
                .Order()
                .FirstOrDefault(),
            nowUtc,
            workers);
    }

    private static AgentWorkerHeartbeatDto MapHeartbeat(
        AgentWorkerHeartbeat heartbeat,
        bool isActive,
        string httpApiWorkspaceRootHash)
    {
        return new AgentWorkerHeartbeatDto(
            heartbeat.Id.Value,
            heartbeat.WorkerId,
            heartbeat.WorkerName,
            heartbeat.StartedAt,
            heartbeat.LastSeenAt,
            isActive,
            heartbeat.ActiveQueueItemId?.Value,
            heartbeat.ActiveTaskId?.Value,
            heartbeat.WorkspaceRootHash,
            heartbeat.Version,
            heartbeat.WorkspaceRootHash == httpApiWorkspaceRootHash);
    }
}
