using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Npgsql;

namespace AICopilot.EndToEndTests;

public abstract class AgentTaskHttpScenarioTestBase : EndToEndScenarioTestBase
{
    protected AgentTaskHttpScenarioTestBase(CoreAICopilotAppFixture fixture)
        : base(fixture)
    {
    }

    protected async Task<Guid> CreateAgentReportTemplateAsync()
    {
        var modelId = await CreateLanguageModelAsync(
            $"agent-report-lm-{Guid.NewGuid():N}",
            BuildFakeAiBaseUrl(),
            "sk-agent-report",
            usages: ["Chat"]);

        return await CreateConversationTemplateAsync(
            $"AgentReport-{Guid.NewGuid():N}",
            modelId,
            "controlled agent report scenario",
            "You are a controlled report-generation assistant.");
    }

    protected async Task<UploadRecordDto> UploadAiGatewayFileAsync(
        Guid sessionId,
        string fileName,
        string content)
    {
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent("SessionTemp"), "scope");
        form.Add(new StringContent(sessionId.ToString()), "sessionId");
        form.Add(new ByteArrayContent(Encoding.UTF8.GetBytes(content)), "file", fileName);

        using var response = await _fixture.HttpClient.PostAsync("/api/aigateway/upload", form);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<UploadRecordDto>(JsonOptions))!;
    }

    protected async Task<List<AgentApprovalRequestDto>> GetPendingApprovalsAsync(Guid taskId)
    {
        var approvals = await GetJsonAsync<List<AgentApprovalRequestDto>>(
            $"/api/aigateway/agent/task/{taskId}/approvals");
        return approvals.Where(item => item.Status == "Pending").ToList();
    }

    protected async Task<AgentTaskDto> WaitForTaskStatusAsync(Guid taskId, string expectedStatus)
    {
        return await WaitForTaskAsync(
            taskId,
            task => string.Equals(task.Status, expectedStatus, StringComparison.OrdinalIgnoreCase),
            $"status {expectedStatus}");
    }

    protected async Task<AgentTaskDto> WaitForTaskToPauseAsync(Guid taskId)
    {
        return await WaitForTaskAsync(
            taskId,
            task => !task.IsRunQueued &&
                    !task.IsRunInProgress &&
                    task.Status is "WaitingToolApproval" or "WaitingFinalApproval" or "WorkspaceReady" or "Failed",
            "worker pause");
    }

    protected async Task<AgentTaskDto> WaitForTaskAsync(
        Guid taskId,
        Func<AgentTaskDto, bool> predicate,
        string reason)
    {
        AgentTaskDto? latest = null;
        for (var attempt = 0; attempt < 60; attempt++)
        {
            latest = await GetJsonAsync<AgentTaskDto>($"/api/aigateway/agent/task?id={taskId}");
            if (predicate(latest))
            {
                return latest;
            }

            await Task.Delay(TimeSpan.FromSeconds(1));
        }

        throw new Xunit.Sdk.XunitException(
            $"Timed out waiting for agent task {taskId} to reach {reason}. " +
            $"Last status={latest?.Status}, queue={latest?.RunQueueStatus}, queued={latest?.IsRunQueued}, running={latest?.IsRunInProgress}.");
    }

    protected async Task ApproveAgentApprovalAsync(Guid approvalId, string comment)
    {
        _ = await PostJsonAsync<AgentApprovalRequestDto>(
            $"/api/aigateway/agent/approval/{approvalId}/approve",
            new { comment });
    }

    protected async Task RejectAgentApprovalAsync(Guid approvalId, string comment)
    {
        _ = await PostJsonAsync<AgentApprovalRequestDto>(
            $"/api/aigateway/agent/approval/{approvalId}/reject",
            new { comment });
    }

    protected async Task<byte[]> DownloadArtifactAsync(Guid artifactId)
    {
        using var response = await _fixture.HttpClient.GetAsync(
            $"/api/aigateway/artifact/{artifactId}/download");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return await response.Content.ReadAsByteArrayAsync();
    }

    protected async Task<AgentTaskDto> PostPlanStreamAsync(object payload)
    {
        var chunks = await PostPlanStreamEventsAsync(payload);
        var taskChunk = chunks.SingleOrDefault(chunk => chunk.Type == "AgentTask");
        if (taskChunk is not null)
        {
            return JsonSerializer.Deserialize<AgentTaskDto>(taskChunk.Content, JsonOptions)!;
        }

        throw new Xunit.Sdk.XunitException(
            $"Plan stream completed without an AgentTask chunk. " +
            $"Chunks={string.Join(" | ", chunks.Select(chunk => chunk.Type))}.");
    }

    protected async Task<List<ChatChunkDto>> PostPlanStreamEventsAsync(object payload)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/aigateway/agent/task/plan-stream")
        {
            Content = JsonContent.Create(payload, options: JsonOptions)
        };

        using var response = await _fixture.HttpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.Should().Be("text/event-stream");

        await using var stream = await response.Content.ReadAsStreamAsync();
        using var reader = new StreamReader(stream);
        var chunks = new List<ChatChunkDto>();
        var buffer = new StringBuilder();

        while (true)
        {
            var line = await reader.ReadLineAsync();
            if (line is null)
            {
                break;
            }

            if (string.IsNullOrWhiteSpace(line))
            {
                if (buffer.Length == 0)
                {
                    continue;
                }

                var data = buffer.ToString();
                buffer.Clear();
                if (data == "[DONE]")
                {
                    break;
                }

                var chunk = JsonSerializer.Deserialize<ChatChunkDto>(data, JsonOptions)!;
                chunks.Add(chunk);
                continue;
            }

            if (line.StartsWith("data:", StringComparison.Ordinal))
            {
                buffer.Append(line["data:".Length..].TrimStart());
            }
        }

        return chunks;
    }

    protected async Task PostJsonExpectingStatusAsync(
        string uri,
        object payload,
        HttpStatusCode statusCode)
    {
        using var response = await _fixture.HttpClient.PostAsJsonAsync(
            uri,
            payload,
            JsonOptions);
        response.StatusCode.Should().Be(statusCode);
    }

    protected async Task<List<MessageTimelineEventRow>> QueryMessageTimelineEventsAsync(Guid sessionId)
    {
        var connectionString = await _fixture.GetConnectionStringAsync();
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT sequence,
                   event_type,
                   message_id,
                   agent_task_id,
                   agent_step_id,
                   approval_request_id,
                   artifact_workspace_id,
                   artifact_id,
                   payload_json
            FROM aigateway.message_events
            WHERE session_id = @sessionId
            ORDER BY sequence;
            """;
        command.Parameters.AddWithValue("sessionId", sessionId);

        var result = new List<MessageTimelineEventRow>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            result.Add(new MessageTimelineEventRow(
                reader.GetInt32(0),
                reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetInt32(2),
                reader.IsDBNull(3) ? null : reader.GetGuid(3),
                reader.IsDBNull(4) ? null : reader.GetGuid(4),
                reader.IsDBNull(5) ? null : reader.GetGuid(5),
                reader.IsDBNull(6) ? null : reader.GetGuid(6),
                reader.IsDBNull(7) ? null : reader.GetGuid(7),
                reader.IsDBNull(8) ? null : reader.GetString(8)));
        }

        return result;
    }

    protected sealed record UploadRecordDto(
        Guid Id,
        string Scope,
        Guid? SessionId,
        Guid? AgentTaskId,
        string FileName,
        string ContentType,
        long FileSize,
        string Sha256,
        DateTimeOffset CreatedAt);

    protected sealed record AgentTaskDto(
        Guid Id,
        string TaskCode,
        Guid SessionId,
        string Title,
        string Goal,
        string TaskType,
        string Status,
        string RiskLevel,
        Guid? ModelId,
        Guid? WorkspaceId,
        string? WorkspaceCode,
        string PlanJson,
        string? FinalSummary,
        DateTimeOffset CreatedAt,
        DateTimeOffset UpdatedAt,
        DateTimeOffset? CompletedAt,
        IReadOnlyCollection<AgentStepDto> Steps,
        int PendingApprovalCount,
        string? LastFailureReason,
        bool CanRetry,
        bool IsRunInProgress = false,
        Guid? QueuedRunId = null,
        string? RunQueueStatus = null,
        bool IsRunQueued = false,
        string? PlanSchemaVersion = null,
        string? PlanDigest = null,
        string? TopologyProfile = null,
        bool IsPlanExecutable = false,
        string PlanIntegrityStatus = "Invalid");

    protected sealed record AgentStepDto(
        Guid Id,
        int StepIndex,
        string Title,
        string Description,
        string StepType,
        string Status,
        string? ToolCode,
        bool RequiresApproval,
        string? ErrorMessage);

    protected sealed record AgentApprovalRequestDto(
        Guid Id,
        Guid TaskId,
        string? WorkspaceCode,
        string Type,
        string TargetId,
        string TargetName,
        string RiskLevel,
        string Status,
        string? Reason,
        DateTimeOffset RequestedAt,
        DateTimeOffset? DecidedAt,
        Guid? DecidedBy);

    protected sealed record ArtifactWorkspaceDto(
        Guid Id,
        string WorkspaceCode,
        Guid TaskId,
        string Status,
        IReadOnlyCollection<ArtifactWorkspaceFileDto> Files,
        IReadOnlyCollection<ArtifactDto> Artifacts,
        IReadOnlyCollection<ArtifactManifestItemDto> Manifest);

    protected sealed record ArtifactWorkspaceFileDto(
        string Name,
        string RelativePath,
        bool IsDirectory,
        long FileSize,
        DateTimeOffset UpdatedAt);

    protected sealed record ArtifactDto(
        Guid Id,
        string Name,
        string Type,
        string Status,
        string RelativePath,
        long FileSize,
        string MimeType,
        int Version,
        DateTimeOffset UpdatedAt,
        string PreviewKind,
        string DownloadUrl,
        int? GeneratedByStepOrder,
        bool RequiresApproval,
        string ApprovalStatus,
        DateTimeOffset? FinalizedAt);

    protected sealed record ArtifactManifestItemDto(
        Guid ArtifactId,
        string Type,
        string Name,
        string RelativePath,
        string Status,
        int Version,
        int? GeneratedByStep,
        string DownloadUrl,
        DateTimeOffset CreatedAt);

    protected sealed record AgentTaskAuditSummaryDto(
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

    protected sealed record SessionTimelinePageDto(
        IReadOnlyList<SessionTimelineEventDto> Items,
        int? BeforeSequence,
        int? AfterSequence,
        bool HasMore,
        bool HasMoreBefore,
        bool HasMoreAfter);

    protected sealed record SessionTimelineEventDto(
        int Sequence,
        string EventType,
        DateTimeOffset CreatedAt,
        int? MessageId,
        Guid? AgentTaskId,
        string? AgentTaskTitle,
        string? AgentTaskGoal,
        string? AgentTaskStatus,
        Guid? AgentStepId,
        int? AgentStepIndex,
        string? AgentStepTitle,
        string? AgentStepStatus,
        string? AgentStepToolCode,
        Guid? ApprovalRequestId,
        string? ApprovalType,
        string? ApprovalStatus,
        string? ApprovalTargetName,
        DateTimeOffset? ApprovalDecidedAt,
        Guid? ArtifactWorkspaceId,
        string? WorkspaceCode,
        string? WorkspaceStatus,
        Guid? ArtifactId,
        string? ArtifactName,
        string? ArtifactType,
        string? ArtifactStatus,
        string? ArtifactRelativePath,
        string? ArtifactDownloadUrl);

    protected sealed record MessageTimelineEventRow(
        int Sequence,
        string EventType,
        int? MessageId,
        Guid? AgentTaskId,
        Guid? AgentStepId,
        Guid? ApprovalRequestId,
        Guid? ArtifactWorkspaceId,
        Guid? ArtifactId,
        string? PayloadJson);
}
