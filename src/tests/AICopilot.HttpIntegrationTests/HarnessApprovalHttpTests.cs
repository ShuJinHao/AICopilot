using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using AICopilot.Core.AiGateway.Runtime.AgentSessions;
using AICopilot.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace AICopilot.HttpIntegrationTests;

[Collection(CoreBackendTestCollection.Name)]
public sealed class HarnessApprovalHttpTests(CoreAICopilotAppFixture fixture)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    [Theory]
    [InlineData("approved")]
    [InlineData("rejected")]
    public async Task ApprovalDecision_ShouldResumeSingleHarnessToolAndPersistSequenceHistory(
        string decision)
    {
        await AuthenticateAsAdminAsync();

        Guid languageModelId = Guid.Empty;
        Guid templateId = Guid.Empty;
        Guid sessionId = Guid.Empty;

        try
        {
            languageModelId = await CreateLanguageModelAsync(
                $"harness-approval-{decision}-{Guid.NewGuid():N}");
            templateId = await CreateConversationTemplateAsync(
                $"harness-approval-{decision}-general-{Guid.NewGuid():N}",
                languageModelId,
                "single tool approval",
                "Use at most one governed tool call in each response.");
            sessionId = await CreateSessionAsync(templateId);

            var executeMode = await PutJsonAsync<AgentSessionModeDto>(
                $"/api/aigateway/session/{sessionId}/agent-mode",
                new
                {
                    mode = "execute",
                    expectedVersion = 1
                });
            executeMode.Should().Be(new AgentSessionModeDto(sessionId, "execute", 2));

            var approvalEvents = await PostEventStreamAsync(
                "/api/aigateway/chat",
                new
                {
                    sessionId,
                    message = "please prepare a diagnostic checklist for device DEV-001"
                });
            var approvalChunk = approvalEvents.Should()
                .ContainSingle(item => item.Type == "ApprovalRequest")
                .Which;
            using var approvalPayload = JsonDocument.Parse(approvalChunk.Content);
            var callId = approvalPayload.RootElement.GetProperty("callId").GetString();
            callId.Should().NotBeNullOrWhiteSpace();

            var pendingApprovals = await GetJsonAsync<List<PendingApprovalDto>>(
                $"/api/aigateway/approval/pending?sessionId={sessionId}");
            pendingApprovals.Should().ContainSingle(item => item.CallId == callId);

            var toolResultRequestsBeforeDecision = fixture.FakeAiToolResultRequestCount;
            var decisionEvents = await PostEventStreamAsync(
                "/api/aigateway/approval/decision",
                new
                {
                    sessionId,
                    callId,
                    decision
                });

            decisionEvents.Should().NotContain(item => item.Type == "Error");
            string.Concat(decisionEvents
                    .Where(item => item.Type == "Text")
                    .Select(item => item.Content))
                .Should().NotBeNullOrWhiteSpace();
            fixture.FakeAiToolResultRequestCount
                .Should().Be(toolResultRequestsBeforeDecision + 1);

            var remainingApprovals = await GetJsonAsync<List<PendingApprovalDto>>(
                $"/api/aigateway/approval/pending?sessionId={sessionId}");
            remainingApprovals.Should().BeEmpty();

            var projection = await GetJsonAsync<AgentSessionProjectionDto>(
                $"/api/aigateway/session?id={sessionId}");
            projection.AgentSessionStatus.Should().Be(nameof(AgentSessionRuntimeStatus.Ready));
            projection.HasPendingApproval.Should().BeFalse();

            var history = await GetJsonAsync<ChatHistoryPageDto>(
                $"/api/aigateway/chat-message/list?sessionId={sessionId}&count=20");
            history.Items.Should().NotBeEmpty();
            history.Items.Select(item => item.Sequence)
                .Should().BeInAscendingOrder().And.OnlyHaveUniqueItems();
            history.Items.Should().Contain(item => item.Role == "User");
            history.Items.Should().Contain(item => item.Role == "Assistant");
        }
        finally
        {
            await DeleteTestConfigurationAsync(sessionId, templateId, languageModelId);
        }
    }

    [Fact]
    public async Task ChatStream_ShouldInterruptWithoutExecutingWhenProviderReturnsTwoApprovals()
    {
        await AuthenticateAsAdminAsync();

        Guid languageModelId = Guid.Empty;
        Guid templateId = Guid.Empty;
        Guid sessionId = Guid.Empty;

        try
        {
            languageModelId = await CreateLanguageModelAsync(
                $"multiple-approval-lm-{Guid.NewGuid():N}");
            templateId = await CreateConversationTemplateAsync(
                $"multiple-approval-template-{Guid.NewGuid():N}",
                languageModelId,
                "provider contract violation",
                "Use only one governed tool call per response.");
            sessionId = await CreateSessionAsync(templateId);

            _ = await PutJsonAsync<AgentSessionModeDto>(
                $"/api/aigateway/session/{sessionId}/agent-mode",
                new
                {
                    mode = "execute",
                    expectedVersion = 1
                });

            var toolResultRequestsBeforeTurn = fixture.FakeAiToolResultRequestCount;
            var events = await PostEventStreamAsync(
                "/api/aigateway/chat",
                new
                {
                    sessionId,
                    message = "force two diagnostic approvals for device DEV-001"
                });

            ReadSingleError(events).Code.Should().Be("agent_session_interrupted");
            events.Should().NotContain(item =>
                item.Type == "FunctionCall" ||
                item.Type == "FunctionResult" ||
                item.Type == "ApprovalRequest");
            fixture.FakeAiToolResultRequestCount.Should().Be(toolResultRequestsBeforeTurn);

            await using (var dbContext = await CreateAiGatewayDbContextAsync())
            {
                var persistedState = await dbContext.AgentSessionStates.SingleAsync(
                    item => item.SessionId == sessionId);
                persistedState.Status.Should().Be(AgentSessionRuntimeStatus.Interrupted);
                persistedState.ActiveTurnId.Should().BeNull();
                persistedState.ProtectedApprovalBindings.Should().BeNull();
            }

            var projection = await GetJsonAsync<AgentSessionProjectionDto>(
                $"/api/aigateway/session?id={sessionId}");
            projection.AgentSessionStatus.Should().Be(nameof(AgentSessionRuntimeStatus.Interrupted));
            projection.HasPendingApproval.Should().BeFalse();
        }
        finally
        {
            await DeleteTestConfigurationAsync(sessionId, templateId, languageModelId);
        }
    }

    private static ProblemChunkDto ReadSingleError(IReadOnlyCollection<ChatChunkDto> events)
    {
        var errorChunk = events.Single(item => item.Type == "Error");
        return JsonSerializer.Deserialize<ProblemChunkDto>(errorChunk.Content, JsonOptions)!;
    }

    private async Task AuthenticateAsAdminAsync()
    {
        using var response = await fixture.HttpClient.PostAsJsonAsync("/api/identity/login", new
        {
            username = fixture.BootstrapAdminUserName,
            password = fixture.BootstrapAdminPassword
        }, JsonOptions);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var login = await ReadJsonAsync<LoginUserDto>(response);
        fixture.SetAuthToken(login.Token);
    }

    private async Task<Guid> CreateLanguageModelAsync(string name)
    {
        var created = await PostJsonAsync<CreatedLanguageModelDto>("/api/aigateway/language-model", new
        {
            provider = "OpenAI",
            name,
            baseUrl = new Uri(fixture.FakeAiBaseUri, "/v1").ToString().TrimEnd('/'),
            apiKey = "sk-test",
            contextWindowTokens = 4096,
            maxOutputTokens = 1024,
            usages = new[] { "Chat" },
            temperature = 0.2
        });

        return created.Id;
    }

    private async Task<Guid> CreateConversationTemplateAsync(
        string templateName,
        Guid modelId,
        string description,
        string prompt)
    {
        var created = await PostJsonAsync<CreatedConversationTemplateDto>(
            "/api/aigateway/conversation-template",
            new
            {
                name = templateName,
                description,
                systemPrompt = prompt,
                modelId,
                maxTokens = 512,
                temperature = 0.1
            });

        return created.Id;
    }

    private async Task<Guid> CreateSessionAsync(Guid templateId)
    {
        var created = await PostJsonAsync<CreatedSessionDto>("/api/aigateway/session", new
        {
            templateId
        });

        return created.Id;
    }

    private async Task<List<ChatChunkDto>> PostEventStreamAsync(string uri, object payload)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, uri)
        {
            Content = JsonContent.Create(payload)
        };

        using var response = await fixture.HttpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.Should().Be("text/event-stream");

        await using var stream = await response.Content.ReadAsStreamAsync();
        using var reader = new StreamReader(stream);
        var events = new List<ChatChunkDto>();
        var buffer = new StringBuilder();

        while (await reader.ReadLineAsync() is { } line)
        {
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

                events.Add(JsonSerializer.Deserialize<ChatChunkDto>(data, JsonOptions)!);
                continue;
            }

            if (line.StartsWith("data:", StringComparison.Ordinal))
            {
                buffer.Append(line["data:".Length..].TrimStart());
            }
        }

        return events;
    }

    private async Task<T> GetJsonAsync<T>(string uri)
    {
        using var response = await fixture.HttpClient.GetAsync(uri);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return await ReadJsonAsync<T>(response);
    }

    private async Task<T> PostJsonAsync<T>(string uri, object payload)
    {
        using var response = await fixture.HttpClient.PostAsJsonAsync(uri, payload, JsonOptions);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return await ReadJsonAsync<T>(response);
    }

    private async Task<T> PutJsonAsync<T>(string uri, object payload)
    {
        using var response = await SendJsonRawAsync(HttpMethod.Put, uri, payload);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return await ReadJsonAsync<T>(response);
    }

    private async Task DeleteTestConfigurationAsync(
        Guid sessionId,
        Guid templateId,
        Guid languageModelId)
    {
        await AuthenticateAsAdminAsync();
        if (sessionId != Guid.Empty)
        {
            await SendJsonAsync(
                HttpMethod.Delete,
                "/api/aigateway/session",
                new { id = sessionId },
                HttpStatusCode.NoContent);
        }

        if (templateId != Guid.Empty)
        {
            await SendJsonAsync(
                HttpMethod.Delete,
                "/api/aigateway/conversation-template",
                new { id = templateId },
                HttpStatusCode.NoContent);
        }

        if (languageModelId != Guid.Empty)
        {
            await SendJsonAsync(
                HttpMethod.Delete,
                "/api/aigateway/language-model",
                new { id = languageModelId },
                HttpStatusCode.NoContent);
        }
    }

    private async Task SendJsonAsync(
        HttpMethod method,
        string uri,
        object payload,
        HttpStatusCode expectedStatusCode)
    {
        using var response = await SendJsonRawAsync(method, uri, payload);
        response.StatusCode.Should().Be(expectedStatusCode);
    }

    private Task<HttpResponseMessage> SendJsonRawAsync(
        HttpMethod method,
        string uri,
        object payload)
    {
        var request = new HttpRequestMessage(method, uri)
        {
            Content = JsonContent.Create(payload)
        };
        return fixture.HttpClient.SendAsync(request);
    }

    private static async Task<T> ReadJsonAsync<T>(HttpResponseMessage response)
    {
        return (await response.Content.ReadFromJsonAsync<T>(JsonOptions))!;
    }

    private async Task<AiGatewayDbContext> CreateAiGatewayDbContextAsync()
    {
        var connectionString = await fixture.GetConnectionStringAsync();
        var options = new DbContextOptionsBuilder<AiGatewayDbContext>()
            .UseNpgsql(connectionString)
            .Options;
        return new AiGatewayDbContext(options);
    }

    private sealed record LoginUserDto(string UserName, string Token);

    private sealed record CreatedLanguageModelDto(Guid Id, string Provider, string Name);

    private sealed record CreatedConversationTemplateDto(Guid Id, string Name);

    private sealed record CreatedSessionDto(Guid Id, string Title);

    private sealed record AgentSessionModeDto(Guid SessionId, string Mode, long Version);

    private sealed record AgentSessionProjectionDto(
        Guid Id,
        long? AgentSessionVersion,
        string? AgentSessionStatus,
        bool AgentSessionResetRequired,
        bool HasPendingApproval);

    private sealed record PendingApprovalDto(
        string CallId,
        string Name,
        string? RuntimeName,
        string? TargetType,
        string? TargetName,
        string? ToolName,
        IReadOnlyDictionary<string, object?> Args);

    private sealed record ChatHistoryPageDto(IReadOnlyList<ChatHistoryItemDto> Items);

    private sealed record ChatHistoryItemDto(int Sequence, string Role, string Content);

    private sealed record ChatChunkDto(string Source, string Type, string Content);

    private sealed record ProblemChunkDto(string? Code, string? Detail, string? UserFacingMessage);
}
