using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using AICopilot.Core.AiGateway.Runtime.AgentSessions;
using AICopilot.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace AICopilot.HttpIntegrationTests;

[Collection(CoreBackendTestCollection.Name)]
public sealed class AgentSafetyApprovalHttpTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly AICopilotAppFixture _fixture;

    public AgentSafetyApprovalHttpTests(CoreAICopilotAppFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task ApprovalDecision_ShouldUseRegisteredHarnessToolWithoutLegacyPolicyOrOnsite()
    {
        await AuthenticateAsAdminAsync();

        Guid languageModelId = Guid.Empty;
        Guid generalTemplateId = Guid.Empty;
        Guid sessionId = Guid.Empty;

        try
        {
            languageModelId = await CreateLanguageModelAsync(
                $"harness-approval-{Guid.NewGuid():N}");
            generalTemplateId = await CreateConversationTemplateAsync(
                $"harness-approval-general-{Guid.NewGuid():N}",
                languageModelId,
                "general assistant",
                "You are a concise manufacturing copilot.");
            sessionId = await CreateSessionAsync(generalTemplateId);

            var executeMode = await PutJsonAsync<AgentSessionModeDto>(
                $"/api/aigateway/session/{sessionId}/agent-mode",
                new
                {
                    mode = "execute",
                    expectedVersion = 1
                });
            executeMode.Should().Be(new AgentSessionModeDto(sessionId, "execute", 2));

            var approvalEvents = await PostChatAsync(new
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
            approvalPayload.RootElement
                .GetProperty("requiresOnsiteAttestation")
                .GetBoolean()
                .Should().BeFalse();

            var pendingApprovals = await GetJsonAsync<List<PendingApprovalDto>>(
                $"/api/aigateway/approval/pending?sessionId={sessionId}");
            var pendingApproval = pendingApprovals.Should()
                .ContainSingle(item => item.CallId == callId)
                .Which;

            var approvedEvents = await PostApprovalDecisionAsync(new
            {
                sessionId,
                callId,
                decision = "approved",
                onsiteConfirmed = false,
                targetType = pendingApproval.TargetType,
                targetName = pendingApproval.TargetName,
                toolName = pendingApproval.ToolName
            });

            approvedEvents.Should().NotContain(item => item.Type == "Error");
            string.Concat(approvedEvents
                    .Where(item => item.Type == "Text")
                    .Select(item => item.Content))
                .Should().Contain("已批准并执行工具");
        }
        finally
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

            if (generalTemplateId != Guid.Empty)
            {
                await SendJsonAsync(
                    HttpMethod.Delete,
                    "/api/aigateway/conversation-template",
                    new { id = generalTemplateId },
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
    }

    [Fact]
    public async Task ControlRequest_ShouldBeRejectedBeforeApprovalOrAnalysis()
    {
        await AuthenticateAsAdminAsync();

        Guid languageModelId = Guid.Empty;
        Guid routingConfigurationId = Guid.Empty;
        Guid generalTemplateId = Guid.Empty;
        Guid intentRoutingTemplateId = Guid.Empty;
        Guid sessionId = Guid.Empty;

        try
        {
            languageModelId = await CreateLanguageModelAsync($"safety-control-{Guid.NewGuid():N}");
            await DeleteConversationTemplateIfExistsAsync("IntentRoutingAgent");

            intentRoutingTemplateId = await CreateConversationTemplateAsync(
                "IntentRoutingAgent",
                languageModelId,
                "intent routing",
                "Select the best matching intent from the list and return a JSON array only. {{$IntentList}}");
            routingConfigurationId = await CreateActiveRoutingModelAsync(languageModelId);

            generalTemplateId = await CreateConversationTemplateAsync(
                $"safety-control-general-{Guid.NewGuid():N}",
                languageModelId,
                "general assistant",
                "You are a concise manufacturing copilot.");

            sessionId = await CreateSessionAsync(generalTemplateId);

            var events = await PostChatAsync(new
            {
                sessionId,
                message = "please restart the server"
            });

            var error = ReadSingleError(events);
            error.Code.Should().Be("control_action_blocked");
            error.UserFacingMessage.Should().NotBeNullOrWhiteSpace();
            events.Should().NotContain(item => item.Type == "ApprovalRequest");
            events.Should().NotContain(item => item.Type == "Widget");
        }
        finally
        {
            await AuthenticateAsAdminAsync();

            if (sessionId != Guid.Empty)
            {
                await SendJsonAsync(HttpMethod.Delete, "/api/aigateway/session", new { id = sessionId }, HttpStatusCode.NoContent);
            }

            if (generalTemplateId != Guid.Empty)
            {
                await SendJsonAsync(HttpMethod.Delete, "/api/aigateway/conversation-template", new { id = generalTemplateId }, HttpStatusCode.NoContent);
            }

            if (intentRoutingTemplateId != Guid.Empty)
            {
                await SendJsonAsync(HttpMethod.Delete, "/api/aigateway/conversation-template", new { id = intentRoutingTemplateId }, HttpStatusCode.NoContent);
            }

            if (routingConfigurationId != Guid.Empty)
            {
                await SendJsonAsync(HttpMethod.Delete, "/api/aigateway/routing-model", new { id = routingConfigurationId }, HttpStatusCode.NoContent);
            }

            if (languageModelId != Guid.Empty)
            {
                await SendJsonAsync(HttpMethod.Delete, "/api/aigateway/language-model", new { id = languageModelId }, HttpStatusCode.NoContent);
            }
        }
    }

    [Fact]
    public async Task BusinessQuery_ShouldStreamTrustedWidgetSeparatelyFromModelText()
    {
        await AuthenticateAsAdminAsync();

        Guid languageModelId = Guid.Empty;
        Guid generalTemplateId = Guid.Empty;
        Guid sessionId = Guid.Empty;

        try
        {
            languageModelId = await CreateLanguageModelAsync(
                $"harness-widget-{Guid.NewGuid():N}");
            generalTemplateId = await CreateConversationTemplateAsync(
                $"harness-widget-general-{Guid.NewGuid():N}",
                languageModelId,
                "trusted widget stream",
                "Use the governed BusinessQuery tool for the requested business data.");
            sessionId = await CreateSessionAsync(generalTemplateId);

            _ = await PutJsonAsync<AgentSessionModeDto>(
                $"/api/aigateway/session/{sessionId}/agent-mode",
                new
                {
                    mode = "execute",
                    expectedVersion = 1
                });

            var challengeEvents = await PostChatAsync(new
            {
                sessionId,
                message = "查看设备 DEV-001 最新日志并显示 inline business widget"
            });
            challengeEvents.Should().NotContain(item => item.Type == "Widget");
            var challengeText = string.Concat(challengeEvents
                .Where(item => item.Type == "Text")
                .Select(item => item.Content));
            var confirmation = Regex.Match(
                challengeText,
                @"确认查询 [0-9a-f]{32}",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            confirmation.Success.Should().BeTrue();

            var resultEvents = await PostChatAsync(new
            {
                sessionId,
                message = confirmation.Value
            });

            resultEvents.Should().NotContain(item => item.Type == "Error");
            var widgets = resultEvents
                .Where(item => item.Type == "Widget")
                .ToArray();
            widgets.Should().NotBeEmpty(
                "the governed result must cross SSE independently; received events: {0}",
                JsonSerializer.Serialize(resultEvents, JsonOptions));
            foreach (var widget in widgets)
            {
                using var payload = JsonDocument.Parse(widget.Content);
                payload.RootElement.GetProperty("type").GetString()
                    .Should().BeOneOf("Chart", "DataTable", "StatsCard");
            }

            var modelText = string.Concat(resultEvents
                .Where(item => item.Type == "Text")
                .Select(item => item.Content));
            modelText.Should().NotContain("\"type\":\"Chart\"");
            modelText.Should().NotContain("\"type\":\"DataTable\"");
            modelText.Should().NotContain("\"type\":\"StatsCard\"");
        }
        finally
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

            if (generalTemplateId != Guid.Empty)
            {
                await SendJsonAsync(
                    HttpMethod.Delete,
                    "/api/aigateway/conversation-template",
                    new { id = generalTemplateId },
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
    }

    [Fact]
    public async Task ChatStream_ShouldInterruptLeftoverRunningSessionWithoutReplay()
    {
        await AuthenticateAsAdminAsync();

        Guid languageModelId = Guid.Empty;
        Guid templateId = Guid.Empty;
        Guid sessionId = Guid.Empty;

        try
        {
            languageModelId = await CreateLanguageModelAsync(
                $"interrupted-session-lm-{Guid.NewGuid():N}");
            templateId = await CreateConversationTemplateAsync(
                $"interrupted-session-template-{Guid.NewGuid():N}",
                languageModelId,
                "interrupted session",
                "You are a concise manufacturing copilot.");
            sessionId = await CreateSessionAsync(templateId);

            await using (var dbContext = await CreateAiGatewayDbContextAsync())
            {
                var persistedState = await dbContext.AgentSessionStates.SingleAsync(
                    item => item.SessionId == sessionId);
                var nowUtc = DateTimeOffset.UtcNow;
                persistedState.BeginTurn(
                    Guid.NewGuid(),
                    nowUtc,
                    nowUtc.AddDays(30));
                await dbContext.SaveChangesAsync();
            }

            var events = await PostChatAsync(new
            {
                sessionId,
                message = "do not replay the abandoned turn"
            });

            ReadSingleError(events).Code.Should().Be("agent_session_interrupted");
            events.Should().NotContain(item =>
                item.Type == "FunctionCall" ||
                item.Type == "FunctionResult" ||
                item.Type == "ApprovalRequest");

            var projection = await GetJsonAsync<AgentSessionProjectionDto>(
                $"/api/aigateway/session?id={sessionId}");
            projection.AgentSessionStatus.Should().Be(nameof(AgentSessionRuntimeStatus.Interrupted));
            projection.AgentSessionResetRequired.Should().BeFalse();
            projection.AgentSessionVersion.Should().Be(3);
        }
        finally
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
    }

    private static ProblemChunkDto ReadSingleError(IReadOnlyCollection<ChatChunkDto> events)
    {
        var errorChunk = events.Single(item => item.Type == "Error");
        return JsonSerializer.Deserialize<ProblemChunkDto>(errorChunk.Content, JsonOptions)!;
    }

    private async Task AuthenticateAsAdminAsync()
    {
        await AuthenticateAsync(_fixture.BootstrapAdminUserName, _fixture.BootstrapAdminPassword);
    }

    private async Task AuthenticateAsync(string userName, string password)
    {
        var result = await LoginAsync(userName, password);
        _fixture.SetAuthToken(result.Token);
    }

    private async Task<LoginUserDto> LoginAsync(string userName, string password)
    {
        using var response = await _fixture.HttpClient.PostAsJsonAsync("/api/identity/login", new
        {
            username = userName,
            password
        }, JsonOptions);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return await ReadJsonAsync<LoginUserDto>(response);
    }

    private async Task<Guid> CreateLanguageModelAsync(string name)
    {
        var created = await PostJsonAsync<CreatedLanguageModelDto>("/api/aigateway/language-model", new
        {
            provider = "OpenAI",
            name,
            baseUrl = new Uri(_fixture.FakeAiBaseUri, "/v1").ToString().TrimEnd('/'),
            apiKey = "sk-test",
            contextWindowTokens = 4096,
            maxOutputTokens = 1024,
            usages = new[] { "Chat", "Routing" },
            temperature = 0.2
        });

        return created.Id;
    }

    private async Task<Guid> CreateActiveRoutingModelAsync(Guid modelId)
    {
        var created = await PostJsonAsync<RoutingModelConfigurationDto>("/api/aigateway/routing-model", new
        {
            name = $"safety-routing-{Guid.NewGuid():N}",
            modelId,
            isActive = true
        });

        return created.Id;
    }

    private async Task<Guid> CreateConversationTemplateAsync(
        string templateName,
        Guid modelId,
        string description,
        string prompt)
    {
        var created = await PostJsonAsync<CreatedConversationTemplateDto>("/api/aigateway/conversation-template", new
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

    private async Task DeleteConversationTemplateIfExistsAsync(string name)
    {
        var templates = await GetJsonAsync<List<ConversationTemplateDto>>("/api/aigateway/conversation-template/list");
        foreach (var template in templates.Where(item => item.Name == name))
        {
            await SendJsonAsync(
                HttpMethod.Delete,
                "/api/aigateway/conversation-template",
                new { id = template.Id },
                HttpStatusCode.NoContent);
        }
    }

    private async Task<Guid> CreateSessionAsync(Guid templateId)
    {
        var created = await PostJsonAsync<CreatedSessionDto>("/api/aigateway/session", new
        {
            templateId
        });

        return created.Id;
    }

    private async Task<List<ChatChunkDto>> PostChatAsync(object payload)
    {
        return await PostEventStreamAsync("/api/aigateway/chat", payload);
    }

    private async Task<List<ChatChunkDto>> PostApprovalDecisionAsync(object payload)
    {
        return await PostEventStreamAsync("/api/aigateway/approval/decision", payload);
    }

    private async Task<List<ChatChunkDto>> PostEventStreamAsync(string uri, object payload)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, uri)
        {
            Content = JsonContent.Create(payload)
        };

        using var response = await _fixture.HttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.Should().Be("text/event-stream");

        await using var stream = await response.Content.ReadAsStreamAsync();
        using var reader = new StreamReader(stream);
        var events = new List<ChatChunkDto>();
        var buffer = new StringBuilder();

        while (true)
        {
            var line = await reader.ReadLineAsync();
            if (line == null)
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
        using var response = await _fixture.HttpClient.GetAsync(uri);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return await ReadJsonAsync<T>(response);
    }

    private async Task<T> PostJsonAsync<T>(string uri, object payload)
    {
        using var response = await _fixture.HttpClient.PostAsJsonAsync(uri, payload, JsonOptions);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return await ReadJsonAsync<T>(response);
    }

    private async Task<T> PutJsonAsync<T>(string uri, object payload)
    {
        using var response = await SendJsonRawAsync(HttpMethod.Put, uri, payload);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return await ReadJsonAsync<T>(response);
    }

    private async Task SendJsonAsync(HttpMethod method, string uri, object payload, HttpStatusCode expectedStatusCode)
    {
        using var response = await SendJsonRawAsync(method, uri, payload);
        response.StatusCode.Should().Be(expectedStatusCode);
    }

    private async Task<HttpResponseMessage> SendJsonRawAsync(HttpMethod method, string uri, object payload)
    {
        var request = new HttpRequestMessage(method, uri)
        {
            Content = JsonContent.Create(payload)
        };

        return await _fixture.HttpClient.SendAsync(request);
    }

    private static async Task<T> ReadJsonAsync<T>(HttpResponseMessage response)
    {
        return (await response.Content.ReadFromJsonAsync<T>(JsonOptions))!;
    }

    private async Task<AiGatewayDbContext> CreateAiGatewayDbContextAsync()
    {
        var connectionString = await _fixture.GetConnectionStringAsync();
        var options = new DbContextOptionsBuilder<AiGatewayDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new AiGatewayDbContext(options);
    }

    private sealed record LoginUserDto(string UserName, string Token);

    private sealed record CreatedLanguageModelDto(Guid Id, string Provider, string Name);

    private sealed record RoutingModelConfigurationDto(Guid Id);

    private sealed record CreatedConversationTemplateDto(Guid Id, string Name);

    private sealed record ConversationTemplateDto(Guid Id, string Name);

    private sealed record CreatedSessionDto(Guid Id, string Title);

    private sealed record AgentSessionModeDto(Guid SessionId, string Mode, long Version);

    private sealed record AgentSessionProjectionDto(
        Guid Id,
        long? AgentSessionVersion,
        string? AgentSessionStatus,
        bool AgentSessionResetRequired);

    private sealed record PendingApprovalDto(
        string CallId,
        string Name,
        string? RuntimeName,
        string? TargetType,
        string? TargetName,
        string? ToolName,
        IReadOnlyDictionary<string, object?> Args,
        bool RequiresOnsiteAttestation,
        DateTimeOffset? AttestationExpiresAt);

    private sealed record ChatChunkDto(string Source, string Type, string Content);

    private sealed record ProblemChunkDto(string? Code, string? Detail, string? UserFacingMessage);
}
