using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using AICopilot.AgentPlugin;
using AICopilot.AiGatewayService.Plugins;
using AICopilot.Core.AiGateway.Aggregates.ApprovalPolicy;
using AICopilot.Core.AiGateway.Aggregates.Sessions;
using AICopilot.Core.Rag.Aggregates.KnowledgeBase;
using AICopilot.DataAnalysisService.Semantics;
using AICopilot.EntityFrameworkCore;
using AICopilot.EntityFrameworkCore.Security;
using AICopilot.EntityFrameworkCore.Repository;
using AICopilot.Infrastructure.Mcp;
using AICopilot.McpService;
using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Ai;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Npgsql;


namespace AICopilot.EndToEndTests;

[Collection(CloudSemanticSimulationBackendTestCollection.Name)]
public sealed class ChatHttpWorkflowEndToEndTests : EndToEndScenarioTestBase
{
    public ChatHttpWorkflowEndToEndTests(CloudSemanticSimulationAICopilotAppFixture fixture)
        : base(fixture)
    {
    }

    [Fact]
    public async Task FrontendReadOnlySupportEndpoints_ShouldReturnChatHistoryAndSemanticSourceStatus()
    {
        await AuthenticateAsAdminAsync();

        var semanticDatabase = await ProvisionSemanticBusinessDatabaseAsync();
        Guid businessDatabaseId = Guid.Empty;
        Guid languageModelId = Guid.Empty;
        Guid templateId = Guid.Empty;
        Guid intentRoutingTemplateId = Guid.Empty;
        Guid sessionId = Guid.Empty;

        try
        {
            await DeleteBusinessDatabaseIfExistsAsync(ConfiguredSemanticPhysicalMappingProvider.DefaultDatabaseName);

            businessDatabaseId = await CreateBusinessDatabaseAsync(
                ConfiguredSemanticPhysicalMappingProvider.DefaultDatabaseName,
                connectionString: semanticDatabase.ConnectionString,
                description: "frontend semantic readonly database",
                isReadOnly: true);

            languageModelId = await CreateLanguageModelAsync(
                $"frontend-lm-{Guid.NewGuid():N}",
                BuildFakeAiBaseUrl(),
                "sk-frontend");

            templateId = await CreateConversationTemplateAsync(
                $"FrontendAgent-{Guid.NewGuid():N}",
                languageModelId,
                "frontend assistant",
                "You are a frontend smoke-test assistant.");

            await DeleteConversationTemplateIfExistsAsync("IntentRoutingAgent");
            intentRoutingTemplateId = await CreateConversationTemplateAsync(
                "IntentRoutingAgent",
                languageModelId,
                "intent routing",
                "Select the best matching intent from the list and return a JSON array only. {{$IntentList}}");

            sessionId = await CreateSessionAsync(templateId);

            var events = await PostChatAsync(new
            {
                sessionId,
                message = "请简单介绍一下当前能力范围"
            });

            events.Should().Contain(item => item.Type == "Text");

            var historyPage = await EventuallyAsync(
                async () => await GetJsonAsync<ChatHistoryMessagePageDto>(
                    $"/api/aigateway/chat-message/list?sessionId={sessionId}&count=20&isDesc=false"),
                page => page.Items.Count >= 2 &&
                        page.Items.Any(item => item.Role == "User" && item.Content.Contains("当前能力范围")) &&
                        page.Items.Any(item => item.Role == "Assistant"));
            var history = historyPage.Items;

            history.Should().OnlyContain(item => item.Role == "User" || item.Role == "Assistant");
            history.Should().BeInAscendingOrder(item => item.Sequence);
            history.Should().OnlyContain(item => item.MessageId > 0 && item.Sequence > 0);
            historyPage.BeforeSequence.Should().Be(history.Min(item => item.Sequence));
            historyPage.AfterSequence.Should().Be(history.Max(item => item.Sequence));
            history.Should().Contain(item =>
                item.Role == "Assistant" &&
                item.RenderChunks.Any(chunk =>
                    chunk.Type == "Text" ||
                    chunk.Type == "Widget" ||
                    chunk.Type == "Error"));
            history.SelectMany(item => item.RenderChunks)
                .Should()
                .OnlyContain(chunk =>
                    chunk.Type == "Text" ||
                    chunk.Type == "Widget" ||
                    chunk.Type == "Error",
                    "message history may restore stable result cards, but runtime details belong to the session timeline");

            await using (var dbContext = await CreateAiGatewayDbContextAsync())
            {
                var messageEvents = await dbContext.MessageEvents
                    .Include(item => item.Message)
                    .Where(item => item.SessionId == sessionId)
                    .OrderBy(item => item.Sequence)
                    .ToListAsync();

                messageEvents.Should().HaveCountGreaterThanOrEqualTo(2);
                messageEvents.Should().OnlyContain(item => item.EventType == MessageEventType.Message);
                messageEvents.Should().OnlyContain(item => item.MessageId.HasValue && item.Message != null);
                messageEvents.Select(item => item.Sequence).Should().Equal(history.Select(item => item.Sequence));
                messageEvents.Select(item => item.MessageId!.Value).Should().Equal(history.Select(item => item.MessageId));
            }

            var semanticStatuses = await GetJsonAsync<List<SemanticSourceStatusDto>>("/api/data-analysis/semantic-source/status");

            semanticStatuses.Should().HaveCount(Enum.GetValues<SemanticQueryTarget>().Length);
            semanticStatuses.Should().OnlyContain(item => !string.IsNullOrWhiteSpace(item.Target));
            semanticStatuses.Should().Contain(item =>
                item.Target == nameof(SemanticQueryTarget.Device) &&
                item.DatabaseName == ConfiguredSemanticPhysicalMappingProvider.DefaultDatabaseName &&
                item.SourceName == ConfiguredSemanticPhysicalMappingProvider.DefaultDeviceSourceName &&
                item.EffectiveSourceName == ConfiguredSemanticPhysicalMappingProvider.DefaultDeviceSourceName &&
                item.IsEnabled &&
                item.IsReadOnly &&
                item.SourceExists &&
                item.ProviderMatched &&
                item.MissingRequiredFields.Count == 0 &&
                item.Status == SemanticSourceStatusValues.Ready);
            semanticStatuses.Should().Contain(item =>
                item.Target == nameof(SemanticQueryTarget.ProductionData) &&
                item.SourceName == ConfiguredSemanticPhysicalMappingProvider.DefaultProductionDataSourceName &&
                item.EffectiveSourceName == ConfiguredSemanticPhysicalMappingProvider.DefaultProductionDataSourceName &&
                item.SourceExists &&
                item.ProviderMatched &&
                item.MissingRequiredFields.Count == 0 &&
                item.Status == SemanticSourceStatusValues.Ready);
        }
        finally
        {
            if (sessionId != Guid.Empty)
            {
                await SendJsonAsync(HttpMethod.Delete, "/api/aigateway/session", new { id = sessionId }, HttpStatusCode.NoContent);
            }

            if (templateId != Guid.Empty)
            {
                await SendJsonAsync(HttpMethod.Delete, "/api/aigateway/conversation-template", new { id = templateId }, HttpStatusCode.NoContent);
            }

            if (intentRoutingTemplateId != Guid.Empty)
            {
                await SendJsonAsync(HttpMethod.Delete, "/api/aigateway/conversation-template", new { id = intentRoutingTemplateId }, HttpStatusCode.NoContent);
            }

            if (languageModelId != Guid.Empty)
            {
                await SendJsonAsync(HttpMethod.Delete, "/api/aigateway/language-model", new { id = languageModelId }, HttpStatusCode.NoContent);
            }

            if (businessDatabaseId != Guid.Empty)
            {
                await SendJsonAsync(HttpMethod.Delete, "/api/data-analysis/business-database", new { id = businessDatabaseId }, HttpStatusCode.NoContent);
            }
        }
    }

    [Fact]
    public async Task ChatFlow_ShouldStreamActualModelMetadataAndPersistMessageSnapshot()
    {
        await AuthenticateAsAdminAsync();

        Guid routingModelId = Guid.Empty;
        Guid finalModelId = Guid.Empty;
        Guid templateId = Guid.Empty;
        Guid intentRoutingTemplateId = Guid.Empty;
        Guid routingConfigurationId = Guid.Empty;
        Guid sessionId = Guid.Empty;
        var routingModelName = $"routing-model-{Guid.NewGuid():N}";
        var finalModelName = $"final-model-{Guid.NewGuid():N}";

        try
        {
            routingModelId = await CreateLanguageModelAsync(
                routingModelName,
                BuildFakeAiBaseUrl(),
                "sk-routing-model",
                usages: ["Routing"]);

            finalModelId = await CreateLanguageModelAsync(
                finalModelName,
                BuildFakeAiBaseUrl(),
                "sk-final-model",
                contextWindowTokens: 128_000,
                maxOutputTokens: 2048,
                usages: ["Chat"]);

            await DeleteConversationTemplateIfExistsAsync("IntentRoutingAgent");
            intentRoutingTemplateId = await CreateConversationTemplateAsync(
                "IntentRoutingAgent",
                finalModelId,
                "metadata routing",
                "Select the best matching intent from the list and return a JSON array only. {{$IntentList}}");

            var routingConfiguration = await PostJsonAsync<RoutingModelConfigurationDto>("/api/aigateway/routing-model", new
            {
                name = $"routing-config-{Guid.NewGuid():N}",
                modelId = routingModelId,
                isActive = true
            });
            routingConfigurationId = routingConfiguration.Id;

            templateId = await CreateConversationTemplateAsync(
                $"metadata-template-{Guid.NewGuid():N}",
                finalModelId,
                "metadata chat",
                "You are a metadata smoke-test assistant.");

            sessionId = await CreateSessionAsync(templateId);

            var events = await PostChatAsync(new
            {
                sessionId,
                message = "请告诉我当前支持哪些只读分析能力。",
                finalModelId
            });

            var metadataEvents = events
                .Where(item => item.Type == "Metadata")
                .Select(item => JsonSerializer.Deserialize<ChatModelMetadataDto>(item.Content, JsonOptions)!)
                .ToList();

            metadataEvents.Should().Contain(item => item.RoutingModelName == routingModelName);
            metadataEvents.Should().Contain(item =>
                item.FinalModelId == finalModelId &&
                item.FinalModelName == finalModelName &&
                item.RoutingModelName == routingModelName &&
                item.ContextWindowTokens == 128_000 &&
                item.MaxOutputTokens == 512);

            var firstFinalMetadataIndex = events.FindIndex(item =>
                item.Type == "Metadata" &&
                item.Content.Contains(finalModelName, StringComparison.Ordinal));
            firstFinalMetadataIndex.Should().BeGreaterThanOrEqualTo(0);

            var firstTextIndex = events.FindIndex(item => item.Type == "Text");
            firstTextIndex.Should().BeGreaterThan(firstFinalMetadataIndex);

            var historyPage = await EventuallyAsync(
                async () => await GetJsonAsync<ChatHistoryMessagePageDto>(
                    $"/api/aigateway/chat-message/list?sessionId={sessionId}&count=20&isDesc=false"),
                page => page.Items.Count >= 2 && page.Items.Any(item =>
                    item.Role == "Assistant"
                    && item.FinalModelId == finalModelId
                    && item.FinalModelName == finalModelName
                    && item.RoutingModelId == routingModelId
                    && item.RoutingModelName == routingModelName));

            var assistantMessage = historyPage.Items.Last(item => item.Role == "Assistant");
            assistantMessage.FinalModelId.Should().Be(finalModelId);
            assistantMessage.FinalModelName.Should().Be(finalModelName);
            assistantMessage.RoutingModelId.Should().Be(routingModelId);
            assistantMessage.RoutingModelName.Should().Be(routingModelName);
            assistantMessage.ContextWindowTokens.Should().Be(128_000);
            assistantMessage.MaxOutputTokens.Should().Be(512);
        }
        finally
        {
            if (sessionId != Guid.Empty)
            {
                await SendJsonAsync(HttpMethod.Delete, "/api/aigateway/session", new { id = sessionId }, HttpStatusCode.NoContent);
            }

            if (routingConfigurationId != Guid.Empty)
            {
                await SendJsonAsync(HttpMethod.Delete, "/api/aigateway/routing-model", new { id = routingConfigurationId }, HttpStatusCode.NoContent);
            }

            if (templateId != Guid.Empty)
            {
                await SendJsonAsync(HttpMethod.Delete, "/api/aigateway/conversation-template", new { id = templateId }, HttpStatusCode.NoContent);
            }

            if (intentRoutingTemplateId != Guid.Empty)
            {
                await SendJsonAsync(HttpMethod.Delete, "/api/aigateway/conversation-template", new { id = intentRoutingTemplateId }, HttpStatusCode.NoContent);
            }

            if (finalModelId != Guid.Empty)
            {
                await SendJsonAsync(HttpMethod.Delete, "/api/aigateway/language-model", new { id = finalModelId }, HttpStatusCode.NoContent);
            }

            if (routingModelId != Guid.Empty)
            {
                await SendJsonAsync(HttpMethod.Delete, "/api/aigateway/language-model", new { id = routingModelId }, HttpStatusCode.NoContent);
            }
        }
    }

    [Fact]
    public async Task ChatFlow_ShouldPersistMessages_AndRecoverApproval()
    {
        await AuthenticateAsAdminAsync();

        var languageModelName = $"chat-lm-{Guid.NewGuid():N}";
        var languageModelId = await CreateLanguageModelAsync(
            languageModelName,
            BuildFakeAiBaseUrl(),
            "sk-chat",
            usages: ["Chat", "Routing"]);

        var generalTemplateName = $"GeneralAgent-{Guid.NewGuid():N}";
        var generalTemplateId = await CreateConversationTemplateAsync(
            generalTemplateName,
            languageModelId,
            "通用助手",
            "你是一个通用助手。");

        await DeleteConversationTemplateIfExistsAsync("IntentRoutingAgent");
        var intentRoutingTemplateId = await CreateConversationTemplateAsync(
            "IntentRoutingAgent",
            languageModelId,
            "意图识别",
            "你负责从下面的意图列表中选择最匹配的一项，并返回 JSON 数组。{{$IntentList}}");
        var routingConfigurationId = await CreateActiveRoutingModelAsync(languageModelId);

        var approvalId = await CreateApprovalPolicyAsync(
            $"chat-approval-{Guid.NewGuid():N}",
            ApprovalTargetType.Plugin,
            new DiagnosticAdvisorPlugin().Name,
            [GetDiagnosticChecklistToolName()],
            true,
            requiresOnsiteAttestation: true);

        var sessionId = await CreateSessionAsync(generalTemplateId);

        var session = await GetJsonAsync<SessionDto>($"/api/aigateway/session?id={sessionId}");
        session.Id.Should().Be(sessionId);

        var sessionList = await GetJsonAsync<List<SessionDto>>("/api/aigateway/session/list");
        sessionList.Should().Contain(item => item.Id == sessionId);

        var normalEvents = await PostChatAsync(new
        {
            sessionId,
            message = "hello"
        });

        normalEvents.Should().Contain(item => item.Type == "Text");
        string.Concat(normalEvents.Where(item => item.Type == "Text").Select(item => item.Content))
            .Should().Contain("Hello");

        await EventuallyAsync(async () =>
        {
            await using var dbContext = await CreateAiGatewayDbContextAsync();
            var messages = await dbContext.Messages
                .Where(message => message.SessionId == sessionId)
                .OrderBy(message => message.Id)
                .ToListAsync();

            return messages;
        }, messages =>
            messages.Count >= 2 &&
            messages.Any(message => message.Type == MessageType.User && message.Content == "hello") &&
            messages.Any(message => message.Type == MessageType.Assistant && message.Content.Contains("Hello")));

        var approvalEvents = await PostChatAsync(new
        {
            sessionId,
            message = "please prepare a diagnostic checklist for device DEV-001"
        });

        var approvalEvent = approvalEvents.Single(item => item.Type == "ApprovalRequest");
        using var approvalPayload = JsonDocument.Parse(approvalEvent.Content);
        var callId = approvalPayload.RootElement.GetProperty("callId").GetString();

        callId.Should().NotBeNullOrWhiteSpace();
        approvalPayload.RootElement.GetProperty("requiresOnsiteAttestation").GetBoolean().Should().BeTrue();

        var pendingApprovals = await GetJsonAsync<List<PendingApprovalDto>>(
            $"/api/aigateway/approval/pending?sessionId={sessionId}");
        pendingApprovals.Should().ContainSingle(item =>
            item.CallId == callId &&
            item.Name == GetDiagnosticChecklistToolName() &&
            item.RequiresOnsiteAttestation);
        var pendingApproval = pendingApprovals.Single(item => item.CallId == callId);

        var blockedByPendingApprovalEvents = await PostChatAsync(new
        {
            sessionId,
            message = "this should wait until the pending approval is handled"
        });

        ReadSingleError(blockedByPendingApprovalEvents).Code.Should().Be("approval_pending");

        await SendJsonAsync(HttpMethod.Put, "/api/aigateway/session/safety-attestation", new
        {
            sessionId,
            isOnsiteConfirmed = true,
            expiresInMinutes = 30
        }, HttpStatusCode.OK);

        var resumedEvents = await PostApprovalDecisionAsync(new
        {
            sessionId,
            message = "批准",
            callIds = new[] { callId },
            callId,
            decision = "approved",
            onsiteConfirmed = true,
            targetType = pendingApproval.TargetType,
            targetName = pendingApproval.TargetName,
            toolName = pendingApproval.ToolName
        });

        string.Concat(resumedEvents.Where(item => item.Type == "Text").Select(item => item.Content))
            .Should().Contain("已批准并执行工具");

        await EventuallyAsync(async () =>
        {
            await using var dbContext = await CreateAiGatewayDbContextAsync();
            return await dbContext.Messages
                .Where(message => message.SessionId == sessionId)
                .OrderBy(message => message.Id)
                .ToListAsync();
        }, messages =>
            messages.Count >= 4 &&
            messages.Any(message => message.Type == MessageType.User && message.Content == "please prepare a diagnostic checklist for device DEV-001") &&
            messages.All(message => message.Content != "this should wait until the pending approval is handled") &&
            messages.Any(message => message.Type == MessageType.User && message.Content.Contains("[审批通过]")) &&
            messages.Any(message => message.Type == MessageType.Assistant && message.Content.Contains("已批准并执行工具")));

        var emptyPendingApprovals = await GetJsonAsync<List<PendingApprovalDto>>(
            $"/api/aigateway/approval/pending?sessionId={sessionId}");
        emptyPendingApprovals.Should().BeEmpty();

        var rejectApprovalEvents = await PostChatAsync(new
        {
            sessionId,
            message = "please prepare another diagnostic checklist for device DEV-001"
        });

        var rejectApprovalEvent = rejectApprovalEvents.Single(item => item.Type == "ApprovalRequest");
        using var rejectApprovalPayload = JsonDocument.Parse(rejectApprovalEvent.Content);
        var rejectCallId = rejectApprovalPayload.RootElement.GetProperty("callId").GetString();

        rejectCallId.Should().NotBeNullOrWhiteSpace();
        var rejectPendingApprovals = await GetJsonAsync<List<PendingApprovalDto>>(
            $"/api/aigateway/approval/pending?sessionId={sessionId}");
        var rejectPendingApproval = rejectPendingApprovals.Single(item => item.CallId == rejectCallId);

        var rejectedEvents = await PostApprovalDecisionAsync(new
        {
            sessionId,
            message = "拒绝",
            callIds = new[] { rejectCallId },
            callId = rejectCallId,
            decision = "rejected",
            onsiteConfirmed = false,
            targetType = rejectPendingApproval.TargetType,
            targetName = rejectPendingApproval.TargetName,
            toolName = rejectPendingApproval.ToolName
        });

        rejectedEvents.Should().Contain(item => item.Type == "Text");

        var anotherSessionId = await CreateSessionAsync(generalTemplateId);
        var lostContextEvents = await PostApprovalDecisionAsync(new
        {
            sessionId = anotherSessionId,
            message = "批准",
            callIds = new[] { "call-missing" },
            callId = "call-missing",
            decision = "approved",
            onsiteConfirmed = true
        });

        lostContextEvents.Should().Contain(item => item.Type == "Error");

        var approvalAuditLogs = await GetJsonAsync<AuditLogListDto>("/api/identity/audit-log/list?page=1&pageSize=50&actionGroup=Approval");
        approvalAuditLogs.Items.Should().Contain(item =>
            item.ActionCode == "Approval.Approve" &&
            item.TargetType == "ToolApproval" &&
            item.Result == "Succeeded");
        approvalAuditLogs.Items.Should().Contain(item =>
            item.ActionCode == "Approval.Reject" &&
            item.TargetType == "ToolApproval" &&
            item.Result == "Rejected");

        await SendJsonAsync(HttpMethod.Delete, "/api/aigateway/session", new { id = anotherSessionId }, HttpStatusCode.NoContent);
        await SendJsonAsync(HttpMethod.Delete, "/api/aigateway/session", new { id = sessionId }, HttpStatusCode.NoContent);
        await SendJsonAsync(HttpMethod.Delete, "/api/aigateway/approval-policy", new { id = approvalId }, HttpStatusCode.NoContent);
        await SendJsonAsync(HttpMethod.Delete, "/api/aigateway/conversation-template", new { id = intentRoutingTemplateId }, HttpStatusCode.NoContent);
        await SendJsonAsync(HttpMethod.Delete, "/api/aigateway/conversation-template", new { id = generalTemplateId }, HttpStatusCode.NoContent);
        await SendJsonAsync(HttpMethod.Delete, "/api/aigateway/routing-model", new { id = routingConfigurationId }, HttpStatusCode.NoContent);
        await SendJsonAsync(HttpMethod.Delete, "/api/aigateway/language-model", new { id = languageModelId }, HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task SessionEndpoints_ShouldNotExposeOtherUsersSessions()
    {
        await AuthenticateAsAdminAsync();

        Guid languageModelId = Guid.Empty;
        Guid generalTemplateId = Guid.Empty;
        Guid sessionId = Guid.Empty;
        var userName = $"session-scope-{Guid.NewGuid():N}";
        const string password = "Password123!";

        try
        {
            languageModelId = await CreateLanguageModelAsync(
                $"session-scope-lm-{Guid.NewGuid():N}",
                BuildFakeAiBaseUrl(),
                "sk-session-scope");

            generalTemplateId = await CreateConversationTemplateAsync(
                $"session-scope-general-{Guid.NewGuid():N}",
                languageModelId,
                "session scope",
                "You are a concise test assistant.");

            sessionId = await CreateSessionAsync(generalTemplateId);
            await CreateUserAsync(userName, password, "User");
            await AuthenticateAsync(userName, password);

            var sessionList = await GetJsonAsync<List<SessionDto>>("/api/aigateway/session/list");
            sessionList.Should().NotContain(item => item.Id == sessionId);

            using var getResponse = await _fixture.HttpClient.GetAsync($"/api/aigateway/session?id={sessionId}");
            getResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);

            using var historyResponse = await _fixture.HttpClient.GetAsync($"/api/aigateway/chat-message/list?sessionId={sessionId}");
            historyResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);

            using var pendingApprovalResponse = await _fixture.HttpClient.GetAsync($"/api/aigateway/approval/pending?sessionId={sessionId}");
            pendingApprovalResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);

            var chatEvents = await PostChatAsync(new
            {
                sessionId,
                message = "hello from a different user"
            });

            ReadSingleError(chatEvents).Code.Should().Be("missing_permission");
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

            if (languageModelId != Guid.Empty)
            {
                await SendJsonAsync(HttpMethod.Delete, "/api/aigateway/language-model", new { id = languageModelId }, HttpStatusCode.NoContent);
            }
        }
    }

    [Fact]
    public async Task ChatFlow_ShouldReturnConfigurationErrorChunk_WhenSessionTemplateIsMissing()
    {
        await AuthenticateAsAdminAsync();

        Guid languageModelId = Guid.Empty;
        Guid generalTemplateId = Guid.Empty;
        Guid sessionId = Guid.Empty;

        try
        {
            languageModelId = await CreateLanguageModelAsync(
                $"missing-template-lm-{Guid.NewGuid():N}",
                BuildFakeAiBaseUrl(),
                "sk-missing-template");

            generalTemplateId = await CreateConversationTemplateAsync(
                $"missing-template-{Guid.NewGuid():N}",
                languageModelId,
                "缺失模板测试",
                "你是一个稳定返回错误的测试助手。");

            sessionId = await CreateSessionAsync(generalTemplateId);

            await SendJsonAsync(
                HttpMethod.Delete,
                "/api/aigateway/conversation-template",
                new { id = generalTemplateId },
                HttpStatusCode.NoContent);
            generalTemplateId = Guid.Empty;

            var events = await PostChatAsync(new
            {
                sessionId,
                message = "hello after template deletion"
            });

            var error = ReadSingleError(events);
            error.Code.Should().Be("chat_configuration_missing");
            error.UserFacingMessage.Should().NotBeNullOrWhiteSpace();

            await EventuallyAsync(async () =>
            {
                await using var dbContext = await CreateAiGatewayDbContextAsync();
                return await dbContext.Messages
                    .Where(message => message.SessionId == sessionId)
                    .OrderBy(message => message.Id)
                    .ToListAsync();
            }, messages =>
                messages.Count == 2 &&
                messages.Any(message => message.Type == MessageType.User && message.Content == "hello after template deletion") &&
                messages.Any(message => message.Type == MessageType.Assistant && !string.IsNullOrWhiteSpace(message.Content)));
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

            if (languageModelId != Guid.Empty)
            {
                await SendJsonAsync(HttpMethod.Delete, "/api/aigateway/language-model", new { id = languageModelId }, HttpStatusCode.NoContent);
            }
        }
    }

    [Fact]
    public async Task ApprovalDecision_ShouldOnlyExecuteOnce_WhenSameCallIsSubmittedConcurrently()
    {
        await AuthenticateAsAdminAsync();

        Guid languageModelId = Guid.Empty;
        Guid routingConfigurationId = Guid.Empty;
        Guid generalTemplateId = Guid.Empty;
        Guid intentRoutingTemplateId = Guid.Empty;
        Guid approvalId = Guid.Empty;
        Guid sessionId = Guid.Empty;

        try
        {
            languageModelId = await CreateLanguageModelAsync(
                $"approval-lock-lm-{Guid.NewGuid():N}",
                BuildFakeAiBaseUrl(),
                "sk-approval-lock",
                usages: ["Chat", "Routing"]);

            generalTemplateId = await CreateConversationTemplateAsync(
                $"approval-lock-general-{Guid.NewGuid():N}",
                languageModelId,
                "审批串行化测试",
                "你是一个稳定的审批串行化测试助手。");

            await DeleteConversationTemplateIfExistsAsync("IntentRoutingAgent");
            intentRoutingTemplateId = await CreateConversationTemplateAsync(
                "IntentRoutingAgent",
                languageModelId,
                "审批串行化意图识别",
                "从意图列表中选择最匹配的一项，并返回 JSON 数组。{{$IntentList}}");
            routingConfigurationId = await CreateActiveRoutingModelAsync(languageModelId);

            approvalId = await CreateApprovalPolicyAsync(
                $"approval-lock-{Guid.NewGuid():N}",
                ApprovalTargetType.Plugin,
                new DiagnosticAdvisorPlugin().Name,
                [GetDiagnosticChecklistToolName()],
                true,
                requiresOnsiteAttestation: true);

            sessionId = await CreateSessionAsync(generalTemplateId);

            var approvalEvents = await PostChatAsync(new
            {
                sessionId,
                message = "please prepare a diagnostic checklist for device DEV-001"
            });

            var approvalEvent = approvalEvents.Single(item => item.Type == "ApprovalRequest");
            using var approvalPayload = JsonDocument.Parse(approvalEvent.Content);
            var callId = approvalPayload.RootElement.GetProperty("callId").GetString();

            callId.Should().NotBeNullOrWhiteSpace();
            var pendingApprovals = await GetJsonAsync<List<PendingApprovalDto>>(
                $"/api/aigateway/approval/pending?sessionId={sessionId}");
            var pendingApproval = pendingApprovals.Single(item => item.CallId == callId);

            await SendJsonAsync(HttpMethod.Put, "/api/aigateway/session/safety-attestation", new
            {
                sessionId,
                isOnsiteConfirmed = true,
                expiresInMinutes = 30
            }, HttpStatusCode.OK);

            var payload = new
            {
                sessionId,
                message = "批准",
                callIds = new[] { callId },
                callId,
                decision = "approved",
                onsiteConfirmed = true,
                targetType = pendingApproval.TargetType,
                targetName = pendingApproval.TargetName,
                toolName = pendingApproval.ToolName
            };

            var decisionTasks = new[]
            {
                PostApprovalDecisionAsync(payload),
                PostApprovalDecisionAsync(payload)
            };

            var results = await Task.WhenAll(decisionTasks);

            results.Count(events => events.Any(item => item.Type == "Error")).Should().Be(1);
            results.Count(events => events.Count != 0 && events.All(item => item.Type != "Error")).Should().Be(1);
            results
                .Single(events => events.Count != 0 && events.All(item => item.Type != "Error"))
                .Should()
                .Contain(item => item.Type == "Text" || item.Type == "FunctionCall" || item.Type == "FunctionResult");

            var duplicateError = results
                .Where(events => events.Any(item => item.Type == "Error"))
                .Select(ReadSingleError)
                .Single();

            duplicateError.Code.Should().Be("approval_already_processed");

            await EventuallyAsync(async () =>
            {
                var approvalAuditLogs = await GetJsonAsync<AuditLogListDto>("/api/identity/audit-log/list?page=1&pageSize=200&actionGroup=Approval");
                return approvalAuditLogs.Items
                    .Where(item => item.ActionCode == "Approval.Approve"
                                   && item.TargetType == "ToolApproval"
                                   && item.TargetId == callId
                                   && item.Result == "Succeeded")
                    .ToList();
            }, logs => logs.Count == 1);
        }
        finally
        {
            await AuthenticateAsAdminAsync();

            if (sessionId != Guid.Empty)
            {
                await SendJsonAsync(HttpMethod.Delete, "/api/aigateway/session", new { id = sessionId }, HttpStatusCode.NoContent);
            }

            if (approvalId != Guid.Empty)
            {
                await SendJsonAsync(HttpMethod.Delete, "/api/aigateway/approval-policy", new { id = approvalId }, HttpStatusCode.NoContent);
            }

            if (intentRoutingTemplateId != Guid.Empty)
            {
                await SendJsonAsync(HttpMethod.Delete, "/api/aigateway/conversation-template", new { id = intentRoutingTemplateId }, HttpStatusCode.NoContent);
            }

            if (generalTemplateId != Guid.Empty)
            {
                await SendJsonAsync(HttpMethod.Delete, "/api/aigateway/conversation-template", new { id = generalTemplateId }, HttpStatusCode.NoContent);
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
}
