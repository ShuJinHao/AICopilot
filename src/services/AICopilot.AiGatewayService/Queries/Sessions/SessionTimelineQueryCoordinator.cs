using System.Text.Json;
using AICopilot.Core.AiGateway.Aggregates.AgentTasks;
using AICopilot.Core.AiGateway.Aggregates.Approvals;
using AICopilot.Core.AiGateway.Aggregates.Artifacts;
using AICopilot.Core.AiGateway.Aggregates.Sessions;
using AICopilot.Core.AiGateway.Ids;
using AICopilot.Core.AiGateway.Specifications.AgentTasks;
using AICopilot.Core.AiGateway.Specifications.Approvals;
using AICopilot.Core.AiGateway.Specifications.Artifacts;
using AICopilot.Core.AiGateway.Specifications.Sessions;
using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Repository;
using AICopilot.SharedKernel.Result;

namespace AICopilot.AiGatewayService.Queries.Sessions;

public sealed class SessionTimelineQueryCoordinator(
    IReadRepository<Session> sessionRepository,
    IMessageTimelineProjectionStore messageTimelineProjectionStore,
    IReadRepository<AgentTask> agentTaskRepository,
    IReadRepository<ApprovalRequest> approvalRepository,
    IReadRepository<ArtifactWorkspace> workspaceRepository,
    ICurrentUser currentUser)
{
    private const int MaxRagSourcePreviewLength = 220;

    public async Task<Result<SessionTimelinePageDto>> GetAsync(
        GetSessionTimelineQuery request,
        CancellationToken cancellationToken)
    {
        if (currentUser.Id is not { } userId)
        {
            return Result.Unauthorized(new ApiProblemDescriptor(
                AuthProblemCodes.Unauthorized,
                "Current user id is missing or invalid."));
        }

        var sessionId = new SessionId(request.SessionId);
        var exists = await sessionRepository.AnyAsync(
            new SessionByIdForUserSpec(sessionId, userId),
            cancellationToken);
        if (!exists)
        {
            return Result.NotFound();
        }

        var count = Math.Clamp(request.Count <= 0 ? 200 : request.Count, 1, 500);
        var allEvents = (await messageTimelineProjectionStore.ListBySessionAsync(sessionId, cancellationToken: cancellationToken))
            .OrderBy(item => item.Sequence)
            .ToArray();
        if (allEvents.Length == 0)
        {
            return Result.Success(new SessionTimelinePageDto([], null, null, false, false, false));
        }

        var taskIds = allEvents
            .Select(item => item.AgentTaskId)
            .Where(id => id.HasValue)
            .Select(id => id!.Value)
            .Distinct()
            .ToArray();
        var tasks = taskIds.Length == 0
            ? []
            : (await agentTaskRepository.ListAsync(
                    new AgentTasksBySessionForUserSpec(sessionId, userId, includeSteps: true),
                    cancellationToken))
                .Where(task => taskIds.Contains(task.Id))
                .ToArray();
        var taskMap = tasks.ToDictionary(task => task.Id);

        var approvals = taskIds.Length == 0
            ? []
            : await approvalRepository.ListAsync(
                new ApprovalRequestsByTasksSpec(taskIds),
                cancellationToken);
        var approvalMap = approvals.ToDictionary(approval => approval.Id);

        var workspaceIds = allEvents
            .Select(item => item.ArtifactWorkspaceId)
            .Where(id => id.HasValue)
            .Select(id => id!.Value)
            .Distinct()
            .ToArray();
        var workspaceMap = new Dictionary<ArtifactWorkspaceId, ArtifactWorkspace>();
        foreach (var workspaceId in workspaceIds)
        {
            var workspace = await workspaceRepository.FirstOrDefaultAsync(
                new ArtifactWorkspaceByIdSpec(workspaceId, includeArtifacts: true),
                cancellationToken);
            if (workspace is not null)
            {
                workspaceMap[workspace.Id] = workspace;
            }
        }

        var cursorEvents = ApplyCursor(allEvents, request);
        var pageEvents = PageEvents(cursorEvents, request, count);
        var items = pageEvents
            .Select(item => Map(item, taskMap, approvalMap, workspaceMap))
            .ToList();

        var minSequence = items.Count > 0 ? items.Min(item => item.Sequence) : (int?)null;
        var maxSequence = items.Count > 0 ? items.Max(item => item.Sequence) : (int?)null;
        var hasMoreBefore = minSequence.HasValue && allEvents.Any(item => item.Sequence < minSequence.Value);
        var hasMoreAfter = maxSequence.HasValue && allEvents.Any(item => item.Sequence > maxSequence.Value);

        return Result.Success(new SessionTimelinePageDto(
            items,
            minSequence,
            maxSequence,
            request.AfterSequence is > 0 ? hasMoreAfter : hasMoreBefore,
            hasMoreBefore,
            hasMoreAfter));
    }

    private static IEnumerable<MessageEvent> ApplyCursor(
        IEnumerable<MessageEvent> allEvents,
        GetSessionTimelineQuery request)
    {
        if (request.BeforeSequence is > 0)
        {
            return allEvents.Where(item => item.Sequence < request.BeforeSequence.Value);
        }

        if (request.AfterSequence is > 0)
        {
            return allEvents.Where(item => item.Sequence > request.AfterSequence.Value);
        }

        return allEvents;
    }

    private static MessageEvent[] PageEvents(
        IEnumerable<MessageEvent> cursorEvents,
        GetSessionTimelineQuery request,
        int count)
    {
        if (request.IsDesc)
        {
            return cursorEvents
                .OrderByDescending(item => item.Sequence)
                .Take(count)
                .ToArray();
        }

        if (request.AfterSequence is > 0)
        {
            return cursorEvents
                .OrderBy(item => item.Sequence)
                .Take(count)
                .ToArray();
        }

        return cursorEvents
            .OrderByDescending(item => item.Sequence)
            .Take(count)
            .OrderBy(item => item.Sequence)
            .ToArray();
    }

    private static SessionTimelineEventDto Map(
        MessageEvent messageEvent,
        IReadOnlyDictionary<AgentTaskId, AgentTask> tasks,
        IReadOnlyDictionary<ApprovalRequestId, ApprovalRequest> approvals,
        IReadOnlyDictionary<ArtifactWorkspaceId, ArtifactWorkspace> workspaces)
    {
        var task = messageEvent.AgentTaskId is { } taskId && tasks.TryGetValue(taskId, out var foundTask)
            ? foundTask
            : null;
        var step = messageEvent.AgentStepId is { } stepId
            ? task?.Steps.FirstOrDefault(item => item.Id == stepId)
            : null;
        var approval = messageEvent.ApprovalRequestId is { } approvalId &&
                       approvals.TryGetValue(approvalId, out var foundApproval)
            ? foundApproval
            : null;
        var workspace = messageEvent.ArtifactWorkspaceId is { } workspaceId &&
                        workspaces.TryGetValue(workspaceId, out var foundWorkspace)
            ? foundWorkspace
            : null;
        var artifact = messageEvent.ArtifactId is { } artifactId
            ? workspace?.Artifacts.FirstOrDefault(item => item.Id == artifactId)
            : null;
        var stepOutput = ResolveStepOutput(step);

        return new SessionTimelineEventDto(
            messageEvent.Sequence,
            messageEvent.EventType.ToString(),
            messageEvent.CreatedAt,
            messageEvent.MessageId,
            task?.Id.Value ?? messageEvent.AgentTaskId?.Value,
            task?.Title,
            task?.Goal,
            task?.Status.ToString(),
            step?.Id.Value ?? messageEvent.AgentStepId?.Value,
            step?.StepIndex,
            step?.Title,
            step?.Status.ToString(),
            step?.ToolCode,
            approval?.Id.Value ?? messageEvent.ApprovalRequestId?.Value,
            approval?.ApprovalType.ToString(),
            approval?.Status.ToString(),
            ResolveApprovalTargetName(task, step, approval, workspace),
            approval?.ApprovedAt,
            workspace?.Id.Value ?? messageEvent.ArtifactWorkspaceId?.Value,
            workspace?.WorkspaceCode,
            workspace?.Status.ToString(),
            artifact?.Id.Value ?? messageEvent.ArtifactId?.Value,
            artifact?.Name,
            artifact?.ArtifactType.ToString(),
            artifact?.Status.ToString(),
            artifact?.RelativePath,
            artifact is null ? null : $"/api/aigateway/artifact/{artifact.Id.Value}/download",
            stepOutput?.Kind,
            stepOutput?.ResultCount,
            stepOutput?.LowConfidence,
            stepOutput?.Sources ?? []);
    }

    private static string? ResolveApprovalTargetName(
        AgentTask? task,
        AgentStep? step,
        ApprovalRequest? approval,
        ArtifactWorkspace? workspace)
    {
        return approval?.ApprovalType switch
        {
            AgentApprovalType.Plan => task?.Title,
            AgentApprovalType.ToolCall => step?.Title ?? approval.TargetId,
            AgentApprovalType.FinalOutput => workspace?.WorkspaceCode ?? approval.TargetId,
            AgentApprovalType.Artifact => approval.TargetId,
            _ => null
        };
    }

    private static AgentStepOutputSummary? ResolveStepOutput(AgentStep? step)
    {
        if (step is null ||
            !string.Equals(step.ToolCode, "rag_search", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(step.OutputJson))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(step.OutputJson);
            var root = document.RootElement;
            var sources = new List<SessionTimelineStepSourceDto>();
            if (TryGetProperty(root, "sources", out var sourcesElement) &&
                sourcesElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var sourceElement in sourcesElement.EnumerateArray())
                {
                    if (sourceElement.ValueKind != JsonValueKind.Object)
                    {
                        continue;
                    }

                    sources.Add(new SessionTimelineStepSourceDto(
                        GetGuid(sourceElement, "knowledgeBaseId"),
                        GetInt(sourceElement, "documentId"),
                        GetString(sourceElement, "documentName"),
                        GetInt(sourceElement, "chunkIndex"),
                        GetDouble(sourceElement, "score"),
                        GetBool(sourceElement, "isLowConfidence"),
                        GetString(sourceElement, "lowConfidenceReason"),
                        NormalizePreview(GetString(sourceElement, "text"))));
                }
            }

            return new AgentStepOutputSummary(
                "RagSearch",
                sources.Count,
                GetBool(root, "lowConfidence"),
                sources);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool TryGetProperty(JsonElement element, string propertyName, out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
            }
        }

        value = default;
        return false;
    }

    private static string? GetString(JsonElement element, string propertyName)
    {
        return TryGetProperty(element, propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private static Guid? GetGuid(JsonElement element, string propertyName)
    {
        var value = GetString(element, propertyName);
        return Guid.TryParse(value, out var guid) ? guid : null;
    }

    private static int? GetInt(JsonElement element, string propertyName)
    {
        return TryGetProperty(element, propertyName, out var value) &&
               value.ValueKind == JsonValueKind.Number &&
               value.TryGetInt32(out var number)
            ? number
            : null;
    }

    private static double? GetDouble(JsonElement element, string propertyName)
    {
        return TryGetProperty(element, propertyName, out var value) &&
               value.ValueKind == JsonValueKind.Number &&
               value.TryGetDouble(out var number)
            ? number
            : null;
    }

    private static bool? GetBool(JsonElement element, string propertyName)
    {
        return TryGetProperty(element, propertyName, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : null;
    }

    private static string? NormalizePreview(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = string.Join(
            ' ',
            value.Trim().Split([' ', '\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries));
        return normalized.Length <= MaxRagSourcePreviewLength
            ? normalized
            : normalized[..(MaxRagSourcePreviewLength - 3)] + "...";
    }

    private sealed record AgentStepOutputSummary(
        string Kind,
        int ResultCount,
        bool? LowConfidence,
        IReadOnlyList<SessionTimelineStepSourceDto> Sources);
}
