using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AICopilot.AspireIntegrationTestKit;

namespace AICopilot.SimulationDockerTests;

[Collection(AgentSimulationTestCollection.Name)]
public sealed class AgentSimulationAcceptanceTests
{
    private const string SimulationLabel = "\u6a21\u62df Cloud \u53ea\u8bfb\u6570\u636e";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly AgentSimulationAICopilotAppFixture _fixture;

    public AgentSimulationAcceptanceTests(AgentSimulationAICopilotAppFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task OfflineSimulation_ShouldExposeOnlyTheUnifiedBusinessQueryTool()
    {
        await AuthenticateAsAdminAsync();

        using var obsoleteResponse = await _fixture.HttpClient.GetAsync(
            "/api/aigateway/tools/query_cloud_data_readonly");
        obsoleteResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var tool = await GetJsonAsync<ToolRegistrationDto>(
            "/api/aigateway/tools/query_business_database_readonly");
        tool.ToolCode.Should().Be("query_business_database_readonly");
        tool.IsEnabled.Should().BeTrue();
        tool.RequiresApproval.Should().BeTrue();
    }

    private async Task AuthenticateAsAdminAsync()
    {
        _fixture.ClearAuthToken();
        var result = await PostJsonAsync<LoginUserDto>("/api/identity/login", new
        {
            username = _fixture.BootstrapAdminUserName,
            password = _fixture.BootstrapAdminPassword
        });
        _fixture.SetAuthToken(result.Token);
    }

    private async Task<Guid> CreateConversationTemplateAsync()
    {
        var model = await PostJsonAsync<CreatedLanguageModelDto>("/api/aigateway/language-model", new
        {
            provider = "OpenAI",
            name = $"simulation-lm-{Guid.NewGuid():N}",
            baseUrl = new Uri(_fixture.FakeAiBaseUri, "/v1").ToString().TrimEnd('/'),
            apiKey = "sk-simulation",
            maxTokens = 4096,
            contextWindowTokens = 4096,
            maxOutputTokens = 1024,
            usages = new[] { "Chat" },
            temperature = 0.1
        });

        var template = await PostJsonAsync<CreatedConversationTemplateDto>("/api/aigateway/conversation-template", new
        {
            name = $"AgentSimulation-{Guid.NewGuid():N}",
            description = "offline simulation acceptance template",
            systemPrompt = "You are a controlled offline Simulation assistant.",
            modelId = model.Id,
            maxTokens = 512,
            temperature = 0.1
        });

        return template.Id;
    }

    private async Task<List<AgentApprovalRequestDto>> GetPendingApprovalsAsync(Guid taskId)
    {
        var approvals = await GetJsonAsync<List<AgentApprovalRequestDto>>(
            $"/api/aigateway/agent/task/{taskId}/approvals");
        return approvals.Where(item => item.Status == "Pending").ToList();
    }

    private async Task<AgentTaskDto> WaitForTaskToPauseAsync(Guid taskId)
    {
        AgentTaskDto? latest = null;
        for (var attempt = 0; attempt < 90; attempt++)
        {
            latest = await GetJsonAsync<AgentTaskDto>($"/api/aigateway/agent/task?id={taskId}");
            if (!latest.IsRunQueued &&
                !latest.IsRunInProgress &&
                latest.Status is "WaitingToolApproval" or "WaitingFinalApproval" or "WorkspaceReady" or "Failed")
            {
                return latest;
            }

            await Task.Delay(TimeSpan.FromSeconds(1));
        }

        throw new Xunit.Sdk.XunitException(
            $"Timed out waiting for Simulation task {taskId}. Last status={latest?.Status}, queue={latest?.RunQueueStatus}.");
    }

    private async Task ApproveAgentApprovalAsync(Guid approvalId, string comment)
    {
        _ = await PostJsonAsync<AgentApprovalRequestDto>(
            $"/api/aigateway/agent/approval/{approvalId}/approve",
            new { comment });
    }

    private async Task<string> DownloadArtifactTextAsync(Guid artifactId)
    {
        var bytes = await DownloadArtifactBytesAsync(artifactId);
        return System.Text.Encoding.UTF8.GetString(bytes);
    }

    private async Task<byte[]> DownloadArtifactBytesAsync(Guid artifactId)
    {
        using var response = await _fixture.HttpClient.GetAsync($"/api/aigateway/artifact/{artifactId}/download");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return await response.Content.ReadAsByteArrayAsync();
    }

    private async Task<JsonDocument> GetJsonDocumentAsync(string uri)
    {
        using var response = await _fixture.HttpClient.GetAsync(uri);
        var body = await response.Content.ReadAsStringAsync();
        response.IsSuccessStatusCode.Should().BeTrue($"GET '{uri}' failed: {body}");
        return JsonDocument.Parse(body);
    }

    private async Task<T> GetJsonAsync<T>(string uri)
    {
        using var response = await _fixture.HttpClient.GetAsync(uri);
        var body = await response.Content.ReadAsStringAsync();
        response.IsSuccessStatusCode.Should().BeTrue($"GET '{uri}' failed: {body}");
        return JsonSerializer.Deserialize<T>(body, JsonOptions)!;
    }

    private async Task<T> PostJsonAsync<T>(string uri, object payload)
    {
        using var response = await _fixture.HttpClient.PostAsJsonAsync(uri, payload, JsonOptions);
        var body = await response.Content.ReadAsStringAsync();
        response.IsSuccessStatusCode.Should().BeTrue($"POST '{uri}' failed: {body}");
        return JsonSerializer.Deserialize<T>(body, JsonOptions)!;
    }

    private async Task<T> PatchJsonAsync<T>(string uri, object payload)
    {
        using var response = await _fixture.HttpClient.PatchAsJsonAsync(uri, payload, JsonOptions);
        var body = await response.Content.ReadAsStringAsync();
        response.IsSuccessStatusCode.Should().BeTrue($"PATCH '{uri}' failed: {body}");
        return JsonSerializer.Deserialize<T>(body, JsonOptions)!;
    }

    private async Task PatchJsonExpectingStatusAsync(string uri, object payload, HttpStatusCode expectedStatus)
    {
        using var response = await _fixture.HttpClient.PatchAsJsonAsync(uri, payload, JsonOptions);
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(expectedStatus, $"PATCH '{uri}' returned: {body}");
    }

    private sealed record LoginUserDto(string UserName, string Token);

    private sealed record CreatedSessionDto(Guid Id);

    private sealed record CreatedLanguageModelDto(Guid Id);

    private sealed record CreatedConversationTemplateDto(Guid Id);

    private sealed record ToolRegistrationDto(Guid Id, string ToolCode, bool IsEnabled, bool RequiresApproval);

    private sealed record AgentTaskDto(
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
        bool IsRunQueued = false);

    private sealed record AgentStepDto(
        Guid Id,
        int StepIndex,
        string Title,
        string Description,
        string StepType,
        string Status,
        string? ToolCode,
        bool RequiresApproval,
        string? ErrorMessage);

    private sealed record AgentApprovalRequestDto(
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

    private sealed record ArtifactWorkspaceDto(
        Guid Id,
        string WorkspaceCode,
        Guid TaskId,
        string Status,
        IReadOnlyCollection<ArtifactWorkspaceFileDto> Files,
        IReadOnlyCollection<ArtifactDto> Artifacts,
        IReadOnlyCollection<ArtifactManifestItemDto> Manifest);

    private sealed record ArtifactWorkspaceFileDto(
        string Name,
        string RelativePath,
        bool IsDirectory,
        long FileSize,
        DateTimeOffset UpdatedAt);

    private sealed record ArtifactDto(
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

    private sealed record ArtifactManifestItemDto(
        Guid ArtifactId,
        string Type,
        string Name,
        string RelativePath,
        string Status,
        int Version,
        int? GeneratedByStep,
        string DownloadUrl,
        DateTimeOffset CreatedAt);

    private sealed record ToolExecutionRecordPageDto(
        IReadOnlyCollection<ToolExecutionRecordDto> Items,
        int PageIndex,
        int PageSize,
        int TotalCount,
        int TotalPages,
        bool HasPrevious,
        bool HasNext);

    private sealed record ToolExecutionRecordDto(
        Guid Id,
        Guid TaskId,
        Guid StepId,
        Guid? RunAttemptId,
        string ToolCode,
        string? InputSummary,
        string? OutputSummary,
        string Status,
        DateTimeOffset StartedAt,
        DateTimeOffset? CompletedAt,
        long? DurationMs,
        string? ErrorCode,
        string? ErrorMessage,
        string? ArtifactId,
        string? AuditMetadata);

    private sealed record AgentTaskAuditSummaryDto(
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
}
