using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using AICopilot.AgentPlugin;
using AICopilot.AiGatewayService.Plugins;
using AICopilot.Core.AiGateway.Aggregates.ApprovalPolicy;
using AICopilot.EntityFrameworkCore;
using AICopilot.Services.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;

namespace AICopilot.BackendTests;

[Collection(CoreBackendTestCollection.Name)]
[Trait("Suite", "Phase43SafetyQuality")]
[Trait("Runtime", "DockerRequired")]
public sealed class Phase43SafetyQualityTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly AICopilotAppFixture _fixture;

    public Phase43SafetyQualityTests(CoreAICopilotAppFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task ApprovalDecision_ShouldRequireValidOnsiteAttestation_AndExplicitReconfirmation()
    {
        await AuthenticateAsAdminAsync();

        Guid languageModelId = Guid.Empty;
        Guid generalTemplateId = Guid.Empty;
        Guid intentRoutingTemplateId = Guid.Empty;
        Guid approvalPolicyId = Guid.Empty;
        Guid sessionId = Guid.Empty;

        try
        {
            languageModelId = await CreateLanguageModelAsync($"phase43-lm-{Guid.NewGuid():N}");
            await DeleteConversationTemplateIfExistsAsync("IntentRoutingAgent");

            intentRoutingTemplateId = await CreateConversationTemplateAsync(
                "IntentRoutingAgent",
                languageModelId,
                "intent routing",
                "Select the best matching intent from the list and return a JSON array only. {{$IntentList}}");

            generalTemplateId = await CreateConversationTemplateAsync(
                $"phase43-general-{Guid.NewGuid():N}",
                languageModelId,
                "general assistant",
                "You are a concise manufacturing copilot.");

            approvalPolicyId = await CreateApprovalPolicyAsync(
                $"phase43-approval-{Guid.NewGuid():N}",
                ApprovalTargetType.Plugin,
                new DiagnosticAdvisorPlugin().Name,
                [GetDiagnosticChecklistToolName()],
                isEnabled: true,
                requiresOnsiteAttestation: true);

            sessionId = await CreateSessionAsync(generalTemplateId);

            var approvalEvents = await PostChatAsync(new
            {
                sessionId,
                message = "please prepare a diagnostic checklist for device DEV-001"
            });

            var approvalChunk = approvalEvents.Single(item => item.Type == "ApprovalRequest");
            using var approvalPayload = JsonDocument.Parse(approvalChunk.Content);
            var callId = approvalPayload.RootElement.GetProperty("callId").GetString();

            callId.Should().NotBeNullOrWhiteSpace();
            approvalPayload.RootElement.GetProperty("requiresOnsiteAttestation").GetBoolean().Should().BeTrue();
            var pendingApprovals = await GetJsonAsync<List<PendingApprovalDto>>(
                $"/api/aigateway/approval/pending?sessionId={sessionId}");
            var pendingApproval = pendingApprovals.Single(item => item.CallId == callId);

            var missingOnsiteEvents = await PostApprovalDecisionAsync(new
            {
                sessionId,
                callId,
                decision = "approved",
                onsiteConfirmed = true,
                targetType = pendingApproval.TargetType,
                targetName = pendingApproval.TargetName,
                toolName = pendingApproval.ToolName
            });

            ReadSingleError(missingOnsiteEvents).Code.Should().Be("onsite_presence_required");

            var attestedSession = await PutJsonAsync<SessionDto>("/api/aigateway/session/safety-attestation", new
            {
                sessionId,
                isOnsiteConfirmed = true,
                expiresInMinutes = 30
            });

            attestedSession.OnsiteConfirmedAt.Should().NotBeNull();
            attestedSession.OnsiteConfirmedBy.Should().Be(_fixture.BootstrapAdminUserName);
            attestedSession.OnsiteConfirmationExpiresAt.Should().NotBeNull();

            await ExpireSessionAttestationAsync(sessionId);

            var expiredEvents = await PostApprovalDecisionAsync(new
            {
                sessionId,
                callId,
                decision = "approved",
                onsiteConfirmed = true,
                targetType = pendingApproval.TargetType,
                targetName = pendingApproval.TargetName,
                toolName = pendingApproval.ToolName
            });

            ReadSingleError(expiredEvents).Code.Should().Be("onsite_presence_expired");

            await PutJsonAsync<SessionDto>("/api/aigateway/session/safety-attestation", new
            {
                sessionId,
                isOnsiteConfirmed = true,
                expiresInMinutes = 30
            });

            var missingReconfirmationEvents = await PostApprovalDecisionAsync(new
            {
                sessionId,
                callId,
                decision = "approved",
                onsiteConfirmed = false,
                targetType = pendingApproval.TargetType,
                targetName = pendingApproval.TargetName,
                toolName = pendingApproval.ToolName
            });

            ReadSingleError(missingReconfirmationEvents).Code.Should().Be("approval_reconfirmation_required");

            var approvedEvents = await PostApprovalDecisionAsync(new
            {
                sessionId,
                callId,
                decision = "approved",
                onsiteConfirmed = true,
                targetType = pendingApproval.TargetType,
                targetName = pendingApproval.TargetName,
                toolName = pendingApproval.ToolName
            });

            string.Concat(approvedEvents.Where(item => item.Type == "Text").Select(item => item.Content))
                .Should().Contain("已批准并执行工具");

            var approvalAuditLogs = await GetJsonAsync<AuditLogListDto>("/api/identity/audit-log/list?page=1&pageSize=50&actionGroup=Approval");
            approvalAuditLogs.Items.Should().Contain(item =>
                item.ActionCode == "AiGateway.SetOnsiteAttestation"
                && item.TargetType == "Session"
                && item.TargetId == sessionId.ToString());
            approvalAuditLogs.Items.Should().Contain(item =>
                item.ActionCode == "Approval.Approve"
                && item.TargetType == "ToolApproval"
                && item.Result == "Succeeded");
        }
        finally
        {
            await AuthenticateAsAdminAsync();

            if (sessionId != Guid.Empty)
            {
                await SendJsonAsync(HttpMethod.Delete, "/api/aigateway/session", new { id = sessionId }, HttpStatusCode.NoContent);
            }

            if (approvalPolicyId != Guid.Empty)
            {
                await SendJsonAsync(HttpMethod.Delete, "/api/aigateway/approval-policy", new { id = approvalPolicyId }, HttpStatusCode.NoContent);
            }

            if (generalTemplateId != Guid.Empty)
            {
                await SendJsonAsync(HttpMethod.Delete, "/api/aigateway/conversation-template", new { id = generalTemplateId }, HttpStatusCode.NoContent);
            }

            if (intentRoutingTemplateId != Guid.Empty)
            {
                await SendJsonAsync(HttpMethod.Delete, "/api/aigateway/conversation-template", new { id = intentRoutingTemplateId }, HttpStatusCode.NoContent);
            }

            if (languageModelId != Guid.Empty)
            {
                await SendJsonAsync(HttpMethod.Delete, "/api/aigateway/language-model", new { id = languageModelId }, HttpStatusCode.NoContent);
            }
        }
    }

    [Fact]
    public async Task ControlRequest_ShouldBeRejectedBeforeApprovalOrAnalysis()
    {
        await AuthenticateAsAdminAsync();

        Guid languageModelId = Guid.Empty;
        Guid generalTemplateId = Guid.Empty;
        Guid intentRoutingTemplateId = Guid.Empty;
        Guid sessionId = Guid.Empty;

        try
        {
            languageModelId = await CreateLanguageModelAsync($"phase43-control-{Guid.NewGuid():N}");
            await DeleteConversationTemplateIfExistsAsync("IntentRoutingAgent");

            intentRoutingTemplateId = await CreateConversationTemplateAsync(
                "IntentRoutingAgent",
                languageModelId,
                "intent routing",
                "Select the best matching intent from the list and return a JSON array only. {{$IntentList}}");

            generalTemplateId = await CreateConversationTemplateAsync(
                $"phase43-control-general-{Guid.NewGuid():N}",
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

            if (languageModelId != Guid.Empty)
            {
                await SendJsonAsync(HttpMethod.Delete, "/api/aigateway/language-model", new { id = languageModelId }, HttpStatusCode.NoContent);
            }
        }
    }

    private static ProblemChunkDto ReadSingleError(IReadOnlyCollection<ChatChunkDto> events)
    {
        var errorChunk = events.Single(item => item.Type == "Error");
        return JsonSerializer.Deserialize<ProblemChunkDto>(errorChunk.Content, JsonOptions)!;
    }

    private async Task ExpireSessionAttestationAsync(Guid sessionId)
    {
        await using var dbContext = await CreateAiGatewayDbContextAsync();
        var session = await dbContext.Sessions.SingleAsync(item => item.Id == sessionId);
        session.SetOnsiteAttestation(
            _fixture.BootstrapAdminUserName,
            DateTimeOffset.UtcNow.AddMinutes(-20),
            DateTimeOffset.UtcNow.AddMinutes(-1));
        await dbContext.SaveChangesAsync();
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
            maxTokens = 1024,
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

    private async Task<Guid> CreateApprovalPolicyAsync(
        string name,
        ApprovalTargetType targetType,
        string targetName,
        IReadOnlyCollection<string> toolNames,
        bool isEnabled,
        bool requiresOnsiteAttestation)
    {
        var created = await PostJsonAsync<CreatedApprovalPolicyDto>("/api/aigateway/approval-policy", new
        {
            name,
            description = "integration-test",
            targetType,
            targetName,
            toolNames,
            isEnabled,
            requiresOnsiteAttestation
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

    private static string GetDiagnosticChecklistToolName()
    {
        var plugin = new DiagnosticAdvisorPlugin();
        var tool = plugin.GetTools()
            ?.First(function => function.Name.Contains("GenerateDiagnosticChecklist", StringComparison.OrdinalIgnoreCase));

        return tool?.Name ?? "GenerateDiagnosticChecklist";
    }

    private sealed record LoginUserDto(string UserName, string Token);

    private sealed record CreatedLanguageModelDto(Guid Id, string Provider, string Name);

    private sealed record CreatedConversationTemplateDto(Guid Id, string Name);

    private sealed record ConversationTemplateDto(Guid Id, string Name);

    private sealed record CreatedApprovalPolicyDto(Guid Id, string Name);

    private sealed record CreatedSessionDto(Guid Id, string Title);

    private sealed record SessionDto(
        Guid Id,
        string Title,
        DateTimeOffset? OnsiteConfirmedAt,
        string? OnsiteConfirmedBy,
        DateTimeOffset? OnsiteConfirmationExpiresAt);

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

    private sealed record AuditLogListDto(
        IReadOnlyCollection<AuditLogSummaryDto> Items,
        int Page,
        int PageSize,
        int TotalCount);

    private sealed record AuditLogSummaryDto(
        Guid Id,
        string ActionGroup,
        string ActionCode,
        string TargetType,
        string? TargetId,
        string? TargetName,
        string Result,
        string Summary,
        IReadOnlyCollection<string> ChangedFields,
        DateTime CreatedAt);

    private sealed record ChatChunkDto(string Source, string Type, string Content);

    private sealed record ProblemChunkDto(string? Code, string? Detail, string? UserFacingMessage);
}
