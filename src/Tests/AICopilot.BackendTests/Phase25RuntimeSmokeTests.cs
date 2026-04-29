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
using AICopilot.EntityFrameworkCore.Repository;
using AICopilot.Infrastructure.Mcp;
using AICopilot.McpService;
using AICopilot.SharedKernel.Ai;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Npgsql;

namespace AICopilot.BackendTests;

[Collection(BackendTestCollection.Name)]
[Trait("Suite", "Phase38Acceptance")]
[Trait("Runtime", "DockerRequired")]
public sealed class Phase25RuntimeSmokeTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private static readonly TimeSpan EventuallyTimeout = TimeSpan.FromSeconds(90);
    private static readonly TimeSpan EventuallyInterval = TimeSpan.FromMilliseconds(750);

    private readonly AICopilotAppFixture _fixture;

    public Phase25RuntimeSmokeTests(AICopilotAppFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task ConfigurationCrud_ShouldManageAllFormalConfigurationsAndMaskSensitiveValues()
    {
        await AuthenticateAsAdminAsync();

        var languageModelName = $"cfg-lm-{Guid.NewGuid():N}";
        var languageModelId = await CreateLanguageModelAsync(
            languageModelName,
            BuildFakeAiBaseUrl(),
            "sk-language-model");

        var languageModel = await GetJsonAsync<LanguageModelDto>($"/api/aigateway/language-model?id={languageModelId}");
        languageModel.Name.Should().Be(languageModelName);
        languageModel.HasApiKey.Should().BeTrue();
        languageModel.ApiKeyMasked.Should().Be("******");
        JsonSerializer.Serialize(languageModel, JsonOptions).Should().NotContain("sk-language-model");

        await SendJsonAsync(HttpMethod.Put, "/api/aigateway/language-model", new
        {
            id = languageModelId,
            provider = "OpenAI",
            name = "fake-chat-model-updated",
            baseUrl = BuildFakeAiBaseUrl(),
            apiKey = "sk-language-model-updated",
            maxTokens = 4096,
            temperature = 0.25
        });

        var languageModelList = await GetJsonAsync<List<LanguageModelDto>>("/api/aigateway/language-model/list");
        languageModelList.Should().Contain(item => item.Id == languageModelId && item.Name == "fake-chat-model-updated");

        var templateName = $"cfg-template-{Guid.NewGuid():N}";
        var templateId = await CreateConversationTemplateAsync(
            templateName,
            languageModelId,
            "测试模板",
            "你是一个配置测试助手。");

        var template = await GetJsonAsync<ConversationTemplateDto>($"/api/aigateway/conversation-template?id={templateId}");
        template.Name.Should().Be(templateName);

        await SendJsonAsync(HttpMethod.Put, "/api/aigateway/conversation-template", new
        {
            id = templateId,
            name = $"{templateName}-updated",
            description = "更新后的模板",
            systemPrompt = "你是一个更新后的配置测试助手。",
            modelId = languageModelId,
            maxTokens = 512,
            temperature = 0.1,
            isEnabled = true
        });

        var templateList = await GetJsonAsync<List<ConversationTemplateDto>>("/api/aigateway/conversation-template/list");
        templateList.Should().Contain(item => item.Id == templateId && item.Name == $"{templateName}-updated");

        var approvalId = await CreateApprovalPolicyAsync(
            $"cfg-approval-{Guid.NewGuid():N}",
            ApprovalTargetType.Plugin,
            new DiagnosticAdvisorPlugin().Name,
            [GetDiagnosticChecklistToolName()],
            true,
            requiresOnsiteAttestation: true);

        var approvalPolicy = await GetJsonAsync<ApprovalPolicyDto>($"/api/aigateway/approval-policy?id={approvalId}");
        approvalPolicy.TargetName.Should().Be(new DiagnosticAdvisorPlugin().Name);

        await SendJsonAsync(HttpMethod.Put, "/api/aigateway/approval-policy", new
        {
            id = approvalId,
            name = "cfg-approval-updated",
            description = "updated",
            targetType = ApprovalTargetType.Plugin,
            targetName = new DiagnosticAdvisorPlugin().Name,
            toolNames = new[] { GetDiagnosticChecklistToolName() },
            isEnabled = false,
            requiresOnsiteAttestation = false
        });

        var approvalList = await GetJsonAsync<List<ApprovalPolicyDto>>("/api/aigateway/approval-policy/list");
        approvalList.Should().Contain(item => item.Id == approvalId && item.IsEnabled == false && item.RequiresOnsiteAttestation == false);

        var embeddingModelId = await CreateEmbeddingModelAsync(
            $"cfg-embedding-{Guid.NewGuid():N}",
            "fake-embedding-model",
            BuildFakeAiBaseUrl(),
            "sk-embedding");

        var embeddingModel = await GetJsonAsync<EmbeddingModelDto>($"/api/rag/embedding-model?id={embeddingModelId}");
        embeddingModel.HasApiKey.Should().BeTrue();
        embeddingModel.ApiKeyMasked.Should().Be("******");
        JsonSerializer.Serialize(embeddingModel, JsonOptions).Should().NotContain("sk-embedding");

        await SendJsonAsync(HttpMethod.Put, "/api/rag/embedding-model", new
        {
            id = embeddingModelId,
            name = "cfg-embedding-updated",
            provider = "OpenAI",
            baseUrl = BuildFakeAiBaseUrl(),
            apiKey = "sk-embedding-updated",
            modelName = "fake-embedding-model-updated",
            dimensions = 4,
            maxTokens = 256,
            isEnabled = true
        });

        var embeddingList = await GetJsonAsync<List<EmbeddingModelDto>>("/api/rag/embedding-model/list");
        embeddingList.Should().Contain(item => item.Id == embeddingModelId && item.ModelName == "fake-embedding-model-updated");

        var knowledgeBaseId = await CreateKnowledgeBaseAsync(
            $"cfg-kb-{Guid.NewGuid():N}",
            "配置知识库",
            embeddingModelId);

        var knowledgeBase = await GetJsonAsync<KnowledgeBaseDto>($"/api/rag/knowledge-base?id={knowledgeBaseId}");
        knowledgeBase.EmbeddingModelId.Should().Be(embeddingModelId);

        await SendJsonAsync(HttpMethod.Put, "/api/rag/knowledge-base", new
        {
            id = knowledgeBaseId,
            name = "cfg-kb-updated",
            description = "updated kb",
            embeddingModelId
        });

        var knowledgeBaseList = await GetJsonAsync<List<KnowledgeBaseDto>>("/api/rag/knowledge-base/list");
        knowledgeBaseList.Should().Contain(item => item.Id == knowledgeBaseId && item.Name == "cfg-kb-updated");

        var businessDatabaseId = await CreateBusinessDatabaseAsync($"cfg-db-{Guid.NewGuid():N}");
        var businessDatabase = await GetJsonAsync<BusinessDatabaseDto>($"/api/data-analysis/business-database?id={businessDatabaseId}");
        businessDatabase.IsReadOnly.Should().BeTrue();
        businessDatabase.HasConnectionString.Should().BeTrue();
        businessDatabase.ConnectionStringMasked.Should().Be("******");
        JsonSerializer.Serialize(businessDatabase, JsonOptions).Should().NotContain("Host=localhost");

        await SendJsonAsync(HttpMethod.Put, "/api/data-analysis/business-database", new
        {
            id = businessDatabaseId,
            name = "cfg-db-updated",
            description = "updated db",
            connectionString = "Host=localhost;Database=updated;Username=test;Password=test;",
            provider = 1,
            isEnabled = true,
            isReadOnly = true
        });

        var businessDatabaseList = await GetJsonAsync<List<BusinessDatabaseDto>>("/api/data-analysis/business-database/list");
        businessDatabaseList.Should().Contain(item => item.Id == businessDatabaseId && item.Name == "cfg-db-updated");

        var mcpServerId = await CreateMcpServerAsync(
            $"cfg-mcp-{Guid.NewGuid():N}",
            true,
            "dotnet",
            typeof(TestingMcpServerMarker).Assembly.Location,
            ChatExposureMode.Disabled);

        var mcpServer = await GetJsonAsync<McpServerDto>($"/api/mcp/server?id={mcpServerId}");
        mcpServer.HasArguments.Should().BeTrue();
        mcpServer.ArgumentsMasked.Should().Be("******");
        JsonSerializer.Serialize(mcpServer, JsonOptions).Should().NotContain(typeof(TestingMcpServerMarker).Assembly.Location);

        await SendJsonAsync(HttpMethod.Put, "/api/mcp/server", new
        {
            id = mcpServerId,
            name = "cfg-mcp-updated",
            description = "updated mcp",
            transportType = 1,
            command = "dotnet",
            arguments = typeof(TestingMcpServerMarker).Assembly.Location,
            chatExposureMode = ChatExposureMode.Disabled,
            allowedToolNames = Array.Empty<string>(),
            isEnabled = false
        });

        var mcpServerList = await GetJsonAsync<List<McpServerDto>>("/api/mcp/server/list");
        mcpServerList.Should().Contain(item => item.Id == mcpServerId && item.IsEnabled == false);

        await SendJsonAsync(HttpMethod.Delete, "/api/mcp/server", new { id = mcpServerId }, HttpStatusCode.NoContent);
        await SendJsonAsync(HttpMethod.Delete, "/api/data-analysis/business-database", new { id = businessDatabaseId }, HttpStatusCode.NoContent);
        await SendJsonAsync(HttpMethod.Delete, "/api/rag/knowledge-base", new { id = knowledgeBaseId }, HttpStatusCode.NoContent);
        await SendJsonAsync(HttpMethod.Delete, "/api/rag/embedding-model", new { id = embeddingModelId }, HttpStatusCode.NoContent);
        await SendJsonAsync(HttpMethod.Delete, "/api/aigateway/approval-policy", new { id = approvalId }, HttpStatusCode.NoContent);
        await SendJsonAsync(HttpMethod.Delete, "/api/aigateway/conversation-template", new { id = templateId }, HttpStatusCode.NoContent);
        await SendJsonAsync(HttpMethod.Delete, "/api/aigateway/language-model", new { id = languageModelId }, HttpStatusCode.NoContent);

        var auditLogs = await GetJsonAsync<AuditLogListDto>("/api/identity/audit-log/list?page=1&pageSize=200&actionGroup=Config");
        auditLogs.Items.Should().Contain(item => item.ActionCode == "AiGateway.CreateLanguageModel" && item.TargetId == languageModelId.ToString());
        auditLogs.Items.Should().Contain(item => item.ActionCode == "AiGateway.UpdateLanguageModel" && item.TargetId == languageModelId.ToString());
        auditLogs.Items.Should().Contain(item => item.ActionCode == "AiGateway.DeleteLanguageModel" && item.TargetId == languageModelId.ToString());
        auditLogs.Items.Should().Contain(item => item.ActionCode == "AiGateway.CreateConversationTemplate" && item.TargetId == templateId.ToString());
        auditLogs.Items.Should().Contain(item => item.ActionCode == "AiGateway.UpdateConversationTemplate" && item.TargetId == templateId.ToString());
        auditLogs.Items.Should().Contain(item => item.ActionCode == "AiGateway.DeleteConversationTemplate" && item.TargetId == templateId.ToString());
        auditLogs.Items.Should().Contain(item => item.ActionCode == "AiGateway.CreateApprovalPolicy" && item.TargetId == approvalId.ToString());
        auditLogs.Items.Should().Contain(item => item.ActionCode == "AiGateway.UpdateApprovalPolicy" && item.TargetId == approvalId.ToString());
        auditLogs.Items.Should().Contain(item => item.ActionCode == "AiGateway.DeleteApprovalPolicy" && item.TargetId == approvalId.ToString());
        auditLogs.Items.Should().Contain(item => item.ActionCode == "DataAnalysis.CreateBusinessDatabase" && item.TargetId == businessDatabaseId.ToString());
        auditLogs.Items.Should().Contain(item => item.ActionCode == "DataAnalysis.UpdateBusinessDatabase" && item.TargetId == businessDatabaseId.ToString());
        auditLogs.Items.Should().Contain(item => item.ActionCode == "DataAnalysis.DeleteBusinessDatabase" && item.TargetId == businessDatabaseId.ToString());
        auditLogs.Items.Should().NotContain(item => item.Summary.Contains("sk-language-model", StringComparison.Ordinal));
        auditLogs.Items.Should().NotContain(item => item.Summary.Contains("Host=localhost;Database=updated;", StringComparison.Ordinal));
    }

    [Fact]
    public async Task UpdateLanguageModel_WithBlankApiKey_ShouldPreserveExistingSecret()
    {
        await AuthenticateAsAdminAsync();

        var languageModelId = await CreateLanguageModelAsync(
            $"cfg-lm-keep-secret-{Guid.NewGuid():N}",
            BuildFakeAiBaseUrl(),
            "sk-keep-original");

        try
        {
            await SendJsonAsync(HttpMethod.Put, "/api/aigateway/language-model", new
            {
                id = languageModelId,
                provider = "OpenAI",
                name = "cfg-lm-keep-secret-updated",
                baseUrl = BuildFakeAiBaseUrl(),
                apiKey = "",
                maxTokens = 4096,
                temperature = 0.35
            });

            await using var dbContext = await CreateAiGatewayDbContextAsync();
            var entity = await dbContext.LanguageModels.SingleAsync(item => item.Id == languageModelId);

            entity.ApiKey.Should().Be("sk-keep-original");
            entity.Name.Should().Be("cfg-lm-keep-secret-updated");
            entity.Parameters.MaxTokens.Should().Be(4096);
            entity.Parameters.Temperature.Should().BeApproximately(0.35f, 0.001f);
        }
        finally
        {
            await SendJsonAsync(HttpMethod.Delete, "/api/aigateway/language-model", new { id = languageModelId }, HttpStatusCode.NoContent);
        }
    }

    [Fact]
    public async Task UpdateBusinessDatabase_WithBlankConnectionString_ShouldPreserveExistingConnection()
    {
        await AuthenticateAsAdminAsync();

        var businessDatabaseId = await CreateBusinessDatabaseAsync($"cfg-db-keep-connection-{Guid.NewGuid():N}");

        try
        {
            await SendJsonAsync(HttpMethod.Put, "/api/data-analysis/business-database", new
            {
                id = businessDatabaseId,
                name = "cfg-db-keep-connection-updated",
                description = "updated db without replacing connection",
                connectionString = "",
                provider = 2,
                isEnabled = true,
                isReadOnly = true
            });

            await using var dbContext = await CreateDataAnalysisDbContextAsync();
            var entity = await dbContext.BusinessDatabases.SingleAsync(item => item.Id == businessDatabaseId);

            entity.ConnectionString.Should().Be("Host=localhost;Database=test;Username=test;Password=test;");
            entity.Provider.Should().Be(AICopilot.Core.DataAnalysis.Aggregates.BusinessDatabase.DbProviderType.PostgreSql);
            entity.Name.Should().Be("cfg-db-keep-connection-updated");
            entity.Description.Should().Be("updated db without replacing connection");
            entity.IsReadOnly.Should().BeTrue();
        }
        finally
        {
            await SendJsonAsync(HttpMethod.Delete, "/api/data-analysis/business-database", new { id = businessDatabaseId }, HttpStatusCode.NoContent);
        }
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

            var history = await EventuallyAsync(
                async () => await GetJsonAsync<List<ChatHistoryMessageDto>>(
                    $"/api/aigateway/chat-message/list?sessionId={sessionId}&count=20&isDesc=false"),
                items => items.Count >= 2 &&
                         items.Any(item => item.Role == "User" && item.Content.Contains("当前能力范围")) &&
                         items.Any(item => item.Role == "Assistant"));

            history.Should().OnlyContain(item => item.Role == "User" || item.Role == "Assistant");
            history.Should().BeInAscendingOrder(item => item.CreatedAt);

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
    public async Task ChatFlow_ShouldPersistMessages_AndRecoverApproval()
    {
        await AuthenticateAsAdminAsync();

        var languageModelName = $"chat-lm-{Guid.NewGuid():N}";
        var languageModelId = await CreateLanguageModelAsync(
            languageModelName,
            BuildFakeAiBaseUrl(),
            "sk-chat");

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
            onsiteConfirmed = true
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

        var rejectApprovalEvents = await PostChatAsync(new
        {
            sessionId,
            message = "please prepare another diagnostic checklist for device DEV-001"
        });

        var rejectApprovalEvent = rejectApprovalEvents.Single(item => item.Type == "ApprovalRequest");
        using var rejectApprovalPayload = JsonDocument.Parse(rejectApprovalEvent.Content);
        var rejectCallId = rejectApprovalPayload.RootElement.GetProperty("callId").GetString();

        rejectCallId.Should().NotBeNullOrWhiteSpace();

        var rejectedEvents = await PostApprovalDecisionAsync(new
        {
            sessionId,
            message = "拒绝",
            callIds = new[] { rejectCallId },
            callId = rejectCallId,
            decision = "rejected",
            onsiteConfirmed = false
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
        Guid generalTemplateId = Guid.Empty;
        Guid intentRoutingTemplateId = Guid.Empty;
        Guid approvalId = Guid.Empty;
        Guid sessionId = Guid.Empty;

        try
        {
            languageModelId = await CreateLanguageModelAsync(
                $"approval-lock-lm-{Guid.NewGuid():N}",
                BuildFakeAiBaseUrl(),
                "sk-approval-lock");

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
                onsiteConfirmed = true
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

            if (languageModelId != Guid.Empty)
            {
                await SendJsonAsync(HttpMethod.Delete, "/api/aigateway/language-model", new { id = languageModelId }, HttpStatusCode.NoContent);
            }
        }
    }

    [Fact]
    public async Task CloudSimSemanticSource_ShouldBeProvisionedByAppHost()
    {
        var semanticDatabase = await ProvisionSemanticBusinessDatabaseAsync();

        await using var connection = new NpgsqlConnection(semanticDatabase.ConnectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT
                (SELECT COUNT(*) FROM device_master_cloud_sim_view),
                (SELECT COUNT(*) FROM device_log_cloud_sim_view),
                (SELECT COUNT(*) FROM recipe_cloud_sim_view),
                (SELECT COUNT(*) FROM capacity_cloud_sim_view),
                (SELECT COUNT(*) FROM production_data_cloud_sim_view);
            """;

        await using var reader = await command.ExecuteReaderAsync();
        (await reader.ReadAsync()).Should().BeTrue();
        reader.GetInt32(0).Should().BeGreaterThan(0);
        reader.GetInt32(1).Should().BeGreaterThan(0);
        reader.GetInt32(2).Should().BeGreaterThan(0);
        reader.GetInt32(3).Should().BeGreaterThan(0);
        reader.GetInt32(4).Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task SemanticChat_ShouldRejectWritableBusinessDatabase()
    {
        await AuthenticateAsAdminAsync();

        var semanticDatabase = await ProvisionSemanticBusinessDatabaseAsync();
        Guid businessDatabaseId = Guid.Empty;
        Guid languageModelId = Guid.Empty;
        Guid generalTemplateId = Guid.Empty;
        Guid intentRoutingTemplateId = Guid.Empty;
        Guid sessionId = Guid.Empty;

        try
        {
            await DeleteBusinessDatabaseIfExistsAsync(ConfiguredSemanticPhysicalMappingProvider.DefaultDatabaseName);

            businessDatabaseId = await CreateBusinessDatabaseAsync(
                ConfiguredSemanticPhysicalMappingProvider.DefaultDatabaseName,
                connectionString: semanticDatabase.ConnectionString,
                description: "semantic writable device/log database",
                isReadOnly: false);

            languageModelId = await CreateLanguageModelAsync(
                $"semantic-readonly-lm-{Guid.NewGuid():N}",
                BuildFakeAiBaseUrl(),
                "sk-semantic-readonly");

            generalTemplateId = await CreateConversationTemplateAsync(
                $"SemanticReadonlyAgent-{Guid.NewGuid():N}",
                languageModelId,
                "semantic assistant",
                "You are a semantic device and device-log assistant.");

            await DeleteConversationTemplateIfExistsAsync("IntentRoutingAgent");
            intentRoutingTemplateId = await CreateConversationTemplateAsync(
                "IntentRoutingAgent",
                languageModelId,
                "intent routing",
                "You must choose the best matching intent from the list and return a JSON array. {{$IntentList}}");

            sessionId = await CreateSessionAsync(generalTemplateId);

            var events = await PostChatAsync(new
            {
                sessionId,
                message = "status of device DEV-001"
            });

            events.Should().Contain(item => item.Type == "Intent" && item.Content.Contains("Analysis.Device.Status", StringComparison.OrdinalIgnoreCase));

            var text = string.Concat(events.Where(item => item.Type == "Text").Select(item => item.Content));
            text.Should().Contain("只读模式");
        }
        finally
        {
            if (sessionId != Guid.Empty)
            {
                await SendJsonAsync(HttpMethod.Delete, "/api/aigateway/session", new { id = sessionId }, HttpStatusCode.NoContent);
            }

            if (intentRoutingTemplateId != Guid.Empty)
            {
                await SendJsonAsync(HttpMethod.Delete, "/api/aigateway/conversation-template", new { id = intentRoutingTemplateId }, HttpStatusCode.NoContent);
            }

            if (generalTemplateId != Guid.Empty)
            {
                await SendJsonAsync(HttpMethod.Delete, "/api/aigateway/conversation-template", new { id = generalTemplateId }, HttpStatusCode.NoContent);
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
    public async Task SemanticDeviceAndLogChat_ShouldQueryRealReadOnlyBusinessDatabase()
    {
        await AuthenticateAsAdminAsync();

        var semanticDatabase = await ProvisionSemanticBusinessDatabaseAsync();
        Guid businessDatabaseId = Guid.Empty;
        Guid languageModelId = Guid.Empty;
        Guid generalTemplateId = Guid.Empty;
        Guid intentRoutingTemplateId = Guid.Empty;
        Guid sessionId = Guid.Empty;

        try
        {
            await DeleteBusinessDatabaseIfExistsAsync(ConfiguredSemanticPhysicalMappingProvider.DefaultDatabaseName);

            businessDatabaseId = await CreateBusinessDatabaseAsync(
                ConfiguredSemanticPhysicalMappingProvider.DefaultDatabaseName,
                connectionString: semanticDatabase.ConnectionString,
                description: "semantic readonly device/log database");

            languageModelId = await CreateLanguageModelAsync(
                $"semantic-lm-{Guid.NewGuid():N}",
                BuildFakeAiBaseUrl(),
                "sk-semantic-chat");

            generalTemplateId = await CreateConversationTemplateAsync(
                $"SemanticAgent-{Guid.NewGuid():N}",
                languageModelId,
                "semantic assistant",
                "You are a semantic device and device-log assistant.");

            await DeleteConversationTemplateIfExistsAsync("IntentRoutingAgent");
            intentRoutingTemplateId = await CreateConversationTemplateAsync(
                "IntentRoutingAgent",
                languageModelId,
                "intent routing",
                "You must choose the best intent from the list and return a JSON array. {{$IntentList}}");

            sessionId = await CreateSessionAsync(generalTemplateId);

            await AssertSemanticChatAsync(
                sessionId,
                "list devices on line LINE-A",
                "Analysis.Device.List",
                "结论：",
                "关键记录：",
                "DEV-001",
                "Cutter A");

            await AssertSemanticChatAsync(
                sessionId,
                "show detail for device DEV-001",
                "Analysis.Device.Detail",
                "结论：",
                "关键记录：",
                "DEV-001",
                "Cutter A");

            await AssertSemanticChatAsync(
                sessionId,
                "show status of device DEV-001",
                "Analysis.Device.Status",
                "结论：",
                "关键记录：",
                "DEV-001",
                "Running");

            await AssertSemanticChatAsync(
                sessionId,
                "show latest logs for device DEV-001",
                "Analysis.DeviceLog.Latest",
                "结论：",
                "关键记录：",
                "DEV-001",
                "Temperature high");

            await AssertSemanticChatAsync(
                sessionId,
                "show logs for device DEV-001 from 2026-04-20T00:00:00Z to 2026-04-20T23:59:59Z",
                "Analysis.DeviceLog.Range",
                "结论：",
                "关键记录：",
                "DEV-001",
                "Motor overload");

            await AssertSemanticChatAsync(
                sessionId,
                "show error logs for device DEV-001",
                "Analysis.DeviceLog.ByLevel",
                "结论：",
                "关键记录：",
                "DEV-001",
                "Error",
                "Motor overload");

            await AssertSemanticChatAsync(
                sessionId,
                "列出 LINE-A 产线的设备",
                "Analysis.Device.List",
                "结论：",
                "关键记录：",
                "DEV-001",
                "Cutter A");

            await AssertSemanticChatAsync(
                sessionId,
                "查看设备 DEV-001 的详情",
                "Analysis.Device.Detail",
                "结论：",
                "关键记录：",
                "DEV-001",
                "Cutter A");

            await AssertSemanticChatAsync(
                sessionId,
                "设备 DEV-001 现在是什么状态",
                "Analysis.Device.Status",
                "结论：",
                "关键记录：",
                "DEV-001",
                "Running");

            await AssertSemanticChatAsync(
                sessionId,
                "设备 DEV-001 最新日志",
                "Analysis.DeviceLog.Latest",
                "结论：",
                "关键记录：",
                "DEV-001",
                "Temperature high");

            await AssertSemanticChatAsync(
                sessionId,
                "查看设备 DEV-001 在 2026-04-20T00:00:00Z 到 2026-04-20T23:59:59Z 的日志",
                "Analysis.DeviceLog.Range",
                "结论：",
                "关键记录：",
                "DEV-001",
                "Motor overload");

            await AssertSemanticChatAsync(
                sessionId,
                "查看设备 DEV-001 的错误日志",
                "Analysis.DeviceLog.ByLevel",
                "结论：",
                "关键记录：",
                "DEV-001",
                "Error",
                "Motor overload");

            await EventuallyAsync(async () =>
            {
                await using var dbContext = await CreateAiGatewayDbContextAsync();
                return await dbContext.Messages
                    .Where(message => message.SessionId == sessionId)
                    .OrderBy(message => message.Id)
                    .ToListAsync();
            }, messages =>
                messages.Count >= 24 &&
                messages.Count(message => message.Type == MessageType.User) >= 12 &&
                messages.Count(message => message.Type == MessageType.Assistant) >= 12 &&
                messages.Any(message => message.Content.Contains("DEV-001", StringComparison.OrdinalIgnoreCase)) &&
                messages.Any(message => message.Content.Contains("Motor overload", StringComparison.OrdinalIgnoreCase)));
        }
        finally
        {
            if (sessionId != Guid.Empty)
            {
                await SendJsonAsync(HttpMethod.Delete, "/api/aigateway/session", new { id = sessionId }, HttpStatusCode.NoContent);
            }

            if (intentRoutingTemplateId != Guid.Empty)
            {
                await SendJsonAsync(HttpMethod.Delete, "/api/aigateway/conversation-template", new { id = intentRoutingTemplateId }, HttpStatusCode.NoContent);
            }

            if (generalTemplateId != Guid.Empty)
            {
                await SendJsonAsync(HttpMethod.Delete, "/api/aigateway/conversation-template", new { id = generalTemplateId }, HttpStatusCode.NoContent);
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
    public async Task RagAndMcpSmoke_ShouldIndexDocument_SearchContent_AndLoadOnlyEnabledMcp()
    {
        await AuthenticateAsAdminAsync();

        var embeddingModelId = await CreateEmbeddingModelAsync(
            $"rag-embedding-{Guid.NewGuid():N}",
            "fake-embedding-model",
            BuildFakeAiBaseUrl(),
            "sk-rag");

        var knowledgeBaseId = await CreateKnowledgeBaseAsync(
            $"rag-kb-{Guid.NewGuid():N}",
            "RAG smoke",
            embeddingModelId);

        var searchKeyword = $"phase25-keyword-{Guid.NewGuid():N}";
        var upload = await UploadDocumentAsync(
            knowledgeBaseId,
            "phase25.txt",
            $"This is a RAG smoke document.\n{searchKeyword}\n");

        await EventuallyAsync(async () =>
        {
            var documents = await GetJsonAsync<List<KnowledgeDocumentDto>>($"/api/rag/document/list?knowledgeBaseId={knowledgeBaseId}");
            return documents.Single(document => document.Id == upload.Id);
        }, document => document.Status == DocumentStatus.Indexed);

        var searchResults = await PostJsonAsync<List<SearchKnowledgeBaseResult>>("/api/rag/search", new
        {
            knowledgeBaseId,
            queryText = searchKeyword,
            topK = 3,
            minScore = 0.0
        });

        searchResults.Should().Contain(result => result.Text.Contains(searchKeyword));

        var enabledServerName = $"mcp-enabled-{Guid.NewGuid():N}";
        var disabledServerName = $"mcp-disabled-{Guid.NewGuid():N}";
        var mcpServerPath = typeof(TestingMcpServerMarker).Assembly.Location;

        var mcpServerCommand = GetTestingMcpExecutablePath();

        await CreateMcpServerAsync(
            enabledServerName,
            true,
            mcpServerCommand,
            string.Empty,
            ChatExposureMode.Advisory,
            ["Echo"]);
        await CreateMcpServerAsync(
            disabledServerName,
            false,
            mcpServerCommand,
            string.Empty,
            ChatExposureMode.Advisory,
            ["Echo"]);

        var connectionString = await _fixture.GetConnectionStringAsync();
        using var verificationHost = BuildMcpVerificationHost(connectionString);
        await using var scope = verificationHost.Services.CreateAsyncScope();
        var bootstrap = scope.ServiceProvider.GetRequiredService<IMcpServerBootstrap>();
        var pluginLoader = scope.ServiceProvider.GetRequiredService<AgentPluginLoader>();

        var clients = new List<IAsyncDisposable>();
        await foreach (var client in bootstrap.StartAsync(CancellationToken.None))
        {
            clients.Add(client);
        }

        try
        {
            clients.Should().HaveCount(1);
            pluginLoader.GetPlugin(enabledServerName).Should().NotBeNull();
            pluginLoader.GetPlugin(disabledServerName).Should().BeNull();
        }
        finally
        {
            foreach (var client in clients)
            {
                await client.DisposeAsync();
            }
        }
    }

    [Fact]
    public async Task SemanticChat_ShouldSupportRecipeCapacityAndProductionDataDomains()
    {
        await AuthenticateAsAdminAsync();

        var semanticDatabase = await ProvisionSemanticBusinessDatabaseAsync();
        Guid businessDatabaseId = Guid.Empty;
        Guid languageModelId = Guid.Empty;
        Guid generalTemplateId = Guid.Empty;
        Guid intentRoutingTemplateId = Guid.Empty;
        Guid sessionId = Guid.Empty;

        try
        {
            await DeleteBusinessDatabaseIfExistsAsync(ConfiguredSemanticPhysicalMappingProvider.DefaultDatabaseName);

            businessDatabaseId = await CreateBusinessDatabaseAsync(
                ConfiguredSemanticPhysicalMappingProvider.DefaultDatabaseName,
                connectionString: semanticDatabase.ConnectionString,
                description: "semantic readonly business database",
                isReadOnly: true);

            languageModelId = await CreateLanguageModelAsync(
                $"semantic-business-lm-{Guid.NewGuid():N}",
                BuildFakeAiBaseUrl(),
                "sk-semantic-business");

            generalTemplateId = await CreateConversationTemplateAsync(
                $"SemanticBusinessAgent-{Guid.NewGuid():N}",
                languageModelId,
                "semantic business assistant",
                "You are a semantic manufacturing business assistant.");

            await DeleteConversationTemplateIfExistsAsync("IntentRoutingAgent");
            intentRoutingTemplateId = await CreateConversationTemplateAsync(
                "IntentRoutingAgent",
                languageModelId,
                "intent routing",
                "You must choose the best intent from the list and return a JSON array. {{$IntentList}}");

            sessionId = await CreateSessionAsync(generalTemplateId);

            var cases = new (string Message, string Intent, string[] ExpectedFragments)[]
            {
                ("list recipes for device DEV-001", "Analysis.Recipe.List", ["Recipe-Cut-01", "V2.0", "DEV-001"]),
                ("show recipe Recipe-Cut-01 detail", "Analysis.Recipe.Detail", ["Recipe-Cut-01", "V2.0", "Cutting"]),
                ("show recipe Recipe-Cut-01 version history", "Analysis.Recipe.VersionHistory", ["Recipe-Cut-01", "V2.0", "V1.0"]),
                ("show capacity for DEV-001 from 2026-04-20T00:00:00Z to 2026-04-21T23:59:59Z", "Analysis.Capacity.Range", ["DEV-001", "126", "123"]),
                ("show capacity for device DEV-001", "Analysis.Capacity.ByDevice", ["DEV-001", "Cutting", "126"]),
                ("show capacity of process Cutting", "Analysis.Capacity.ByProcess", ["Cutting", "DEV-001", "126"]),
                ("show latest production records for device DEV-001", "Analysis.ProductionData.Latest", ["DEV-001", "CELL-0002", "Fail"]),
                ("show production records for DEV-001 from 2026-04-21T00:00:00Z to 2026-04-21T23:59:59Z", "Analysis.ProductionData.Range", ["DEV-001", "CELL-0001", "CELL-0002"]),
                ("show production records for device DEV-001", "Analysis.ProductionData.ByDevice", ["DEV-001", "CELL-0001", "Station-A"]),
                ("列出设备 DEV-001 的配方", "Analysis.Recipe.List", ["Recipe-Cut-01", "V2.0", "DEV-001"]),
                ("查看配方 Recipe-Cut-01 详情", "Analysis.Recipe.Detail", ["Recipe-Cut-01", "V2.0", "Cutting"]),
                ("查看配方 Recipe-Cut-01 的版本历史", "Analysis.Recipe.VersionHistory", ["Recipe-Cut-01", "V2.0", "V1.0"]),
                ("查看 DEV-001 在 2026-04-20T00:00:00Z 到 2026-04-21T23:59:59Z 的产能", "Analysis.Capacity.Range", ["DEV-001", "126", "123"]),
                ("查看设备 DEV-001 的产能", "Analysis.Capacity.ByDevice", ["DEV-001", "Cutting", "126"]),
                ("查看 Cutting 工序的产能", "Analysis.Capacity.ByProcess", ["Cutting", "DEV-001", "126"]),
                ("查看设备 DEV-001 最新生产记录", "Analysis.ProductionData.Latest", ["DEV-001", "CELL-0002", "Fail"]),
                ("查看 DEV-001 在 2026-04-21T00:00:00Z 到 2026-04-21T23:59:59Z 的生产记录", "Analysis.ProductionData.Range", ["DEV-001", "CELL-0001", "CELL-0002"]),
                ("查看设备 DEV-001 的生产记录", "Analysis.ProductionData.ByDevice", ["DEV-001", "CELL-0001", "Station-A"])
            };

            foreach (var testCase in cases)
            {
                await AssertSemanticChatAsync(sessionId, testCase.Message, testCase.Intent, testCase.ExpectedFragments);
            }
        }
        finally
        {
            if (sessionId != Guid.Empty)
            {
                await SendJsonAsync(HttpMethod.Delete, "/api/aigateway/session", new { id = sessionId }, HttpStatusCode.NoContent);
            }

            if (intentRoutingTemplateId != Guid.Empty)
            {
                await SendJsonAsync(HttpMethod.Delete, "/api/aigateway/conversation-template", new { id = intentRoutingTemplateId }, HttpStatusCode.NoContent);
            }

            if (generalTemplateId != Guid.Empty)
            {
                await SendJsonAsync(HttpMethod.Delete, "/api/aigateway/conversation-template", new { id = generalTemplateId }, HttpStatusCode.NoContent);
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
    public async Task PolicyChat_ShouldReturnAlignedBusinessRuleAnswers()
    {
        await AuthenticateAsAdminAsync();

        Guid languageModelId = Guid.Empty;
        Guid generalTemplateId = Guid.Empty;
        Guid intentRoutingTemplateId = Guid.Empty;
        Guid sessionId = Guid.Empty;

        try
        {
            languageModelId = await CreateLanguageModelAsync(
                $"policy-lm-{Guid.NewGuid():N}",
                BuildFakeAiBaseUrl(),
                "sk-policy");

            generalTemplateId = await CreateConversationTemplateAsync(
                $"PolicyAgent-{Guid.NewGuid():N}",
                languageModelId,
                "policy assistant",
                "You are a manufacturing policy assistant.");

            await DeleteConversationTemplateIfExistsAsync("IntentRoutingAgent");
            intentRoutingTemplateId = await CreateConversationTemplateAsync(
                "IntentRoutingAgent",
                languageModelId,
                "intent routing",
                "You must choose the best intent from the list and return a JSON array. {{$IntentList}}");

            sessionId = await CreateSessionAsync(generalTemplateId);

            var cases = new (string Message, string Intent, string[] ExpectedFragments)[]
            {
                ("员工修改机台参数需要什么权限？", "Policy.EmployeeAuthorization", ["业务结论", "双重", "禁止放宽"]),
                ("Can an operator change recipe settings without device assignment?", "Policy.EmployeeAuthorization", ["业务结论", "功能权限", "设备分配"]),
                ("谁可以注册新设备？", "Policy.DeviceRegistration", ["管理员", "业务结论", "禁止放宽"]),
                ("Can a normal user register a new device?", "Policy.DeviceRegistration", ["管理员", "禁止放宽", "ClientCode"]),
                ("设备删除前要检查什么？", "Policy.DeviceLifecycle", ["历史依赖", "禁止放宽", "配方"]),
                ("Can the device code be edited or the device be hard deleted?", "Policy.DeviceLifecycle", ["寻址码", "硬删除", "历史依赖"]),
                ("ClientCode 和 DeviceId 是什么关系？", "Policy.BootstrapIdentity", ["ClientCode", "DeviceId", "bootstrap"]),
                ("Can the client upload production data directly by device name?", "Policy.BootstrapIdentity", ["不能", "DeviceId", "bootstrap"]),
                ("配方修改是覆盖还是新建版本？", "Policy.RecipeVersioning", ["版本化", "V1.0", "禁止放宽"]),
                ("Does recipe editing overwrite the active version?", "Policy.RecipeVersioning", ["版本化", "归档", "禁止放宽"])
            };

            foreach (var testCase in cases)
            {
                await AssertPolicyChatAsync(sessionId, testCase.Message, testCase.Intent, testCase.ExpectedFragments);
            }
        }
        finally
        {
            if (sessionId != Guid.Empty)
            {
                await SendJsonAsync(HttpMethod.Delete, "/api/aigateway/session", new { id = sessionId }, HttpStatusCode.NoContent);
            }

            if (intentRoutingTemplateId != Guid.Empty)
            {
                await SendJsonAsync(HttpMethod.Delete, "/api/aigateway/conversation-template", new { id = intentRoutingTemplateId }, HttpStatusCode.NoContent);
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

    private async Task AuthenticateAsync(string userName, string password)
    {
        _fixture.ClearAuthToken();

        var result = await PostJsonAsync<LoginUserDto>("/api/identity/login", new
        {
            username = userName,
            password
        });

        _fixture.SetAuthToken(result.Token);
    }

    private async Task<Guid> CreateUserAsync(string userName, string password, string roleName)
    {
        var created = await PostJsonAsync<CreatedUserDto>("/api/identity/user", new
        {
            userName,
            password,
            roleName
        });

        return created.UserId;
    }

    private async Task<Guid> CreateLanguageModelAsync(string name, string baseUrl, string apiKey)
    {
        var created = await PostJsonAsync<CreatedLanguageModelDto>("/api/aigateway/language-model", new
        {
            provider = "OpenAI",
            name,
            baseUrl,
            apiKey,
            maxTokens = 4096,
            temperature = 0.2
        });

        created.Id.Should().NotBeEmpty();
        return created.Id;
    }

    private async Task<Guid> CreateConversationTemplateAsync(string templateName, Guid modelId, string description, string prompt)
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

    private async Task<Guid> CreateApprovalPolicyAsync(
        string name,
        ApprovalTargetType targetType,
        string targetName,
        IReadOnlyCollection<string> toolNames,
        bool isEnabled,
        bool requiresOnsiteAttestation = false)
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

    private async Task<Guid> CreateEmbeddingModelAsync(string name, string modelName, string baseUrl, string apiKey)
    {
        var created = await PostJsonAsync<CreatedEmbeddingModelDto>("/api/rag/embedding-model", new
        {
            name,
            provider = "OpenAI",
            baseUrl,
            apiKey,
            modelName,
            dimensions = 4,
            maxTokens = 256,
            isEnabled = true
        });

        return created.Id;
    }

    private async Task<Guid> CreateKnowledgeBaseAsync(string name, string description, Guid embeddingModelId)
    {
        var created = await PostJsonAsync<CreatedKnowledgeBaseDto>("/api/rag/knowledge-base", new
        {
            name,
            description,
            embeddingModelId
        });

        return created.Id;
    }

    private async Task<Guid> CreateBusinessDatabaseAsync(
        string name,
        string? connectionString = null,
        string description = "readonly db",
        int provider = 1,
        bool isEnabled = true,
        bool isReadOnly = true)
    {
        var created = await PostJsonAsync<CreatedBusinessDatabaseDto>("/api/data-analysis/business-database", new
        {
            name,
            description,
            connectionString = connectionString ?? "Host=localhost;Database=test;Username=test;Password=test;",
            provider,
            isEnabled,
            isReadOnly
        });

        return created.Id;
    }

    private async Task<Guid> CreateMcpServerAsync(
        string name,
        bool isEnabled,
        string command,
        string arguments,
        ChatExposureMode chatExposureMode = ChatExposureMode.Disabled,
        IReadOnlyCollection<string>? allowedToolNames = null)
    {
        var created = await PostJsonAsync<CreatedMcpServerDto>("/api/mcp/server", new
        {
            name,
            description = "testing mcp server",
            transportType = 1,
            command,
            arguments,
            chatExposureMode,
            allowedToolNames = allowedToolNames ?? Array.Empty<string>(),
            isEnabled
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

    private async Task<UploadDocumentDto> UploadDocumentAsync(Guid knowledgeBaseId, string fileName, string content)
    {
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent(knowledgeBaseId.ToString()), "knowledgeBaseId");
        form.Add(new ByteArrayContent(Encoding.UTF8.GetBytes(content)), "file", fileName);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/rag/document")
        {
            Content = form
        };

        using var response = await _fixture.HttpClient.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<UploadDocumentDto>(JsonOptions))!;
    }

    private async Task DeleteBusinessDatabaseIfExistsAsync(string name)
    {
        var businessDatabases = await GetJsonAsync<List<BusinessDatabaseDto>>("/api/data-analysis/business-database/list");
        foreach (var businessDatabase in businessDatabases.Where(item => item.Name == name))
        {
            await SendJsonAsync(
                HttpMethod.Delete,
                "/api/data-analysis/business-database",
                new { id = businessDatabase.Id },
                HttpStatusCode.NoContent);
        }
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

    private async Task<List<ChatChunkDto>> PostChatAsync(object payload)
    {
        return await PostEventStreamAsync("/api/aigateway/chat", payload);
    }

    private async Task<List<ChatChunkDto>> PostApprovalDecisionAsync(object payload)
    {
        return await PostEventStreamAsync("/api/aigateway/approval/decision", payload);
    }

    private static ProblemChunkDto ReadSingleError(IReadOnlyCollection<ChatChunkDto> events)
    {
        var errorChunk = events.Single(item => item.Type == "Error");
        return JsonSerializer.Deserialize<ProblemChunkDto>(errorChunk.Content, JsonOptions)!;
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

    private async Task AssertPolicyChatAsync(
        Guid sessionId,
        string message,
        string expectedIntent,
        params string[] expectedTextFragments)
    {
        var events = await PostChatAsync(new
        {
            sessionId,
            message
        });

        events.Should().NotContain(item => item.Type == "Error");
        events.Should().Contain(item => item.Type == "Intent" && item.Content.Contains(expectedIntent, StringComparison.OrdinalIgnoreCase));

        var text = string.Concat(events.Where(item => item.Type == "Text").Select(item => item.Content));
        text.Should().NotBeNullOrWhiteSpace();
        text.Should().Contain("业务结论");
        text.Should().Contain("适用条件");
        text.Should().Contain("禁止放宽的边界");

        foreach (var expectedTextFragment in expectedTextFragments)
        {
            text.Should().Contain(expectedTextFragment);
        }

        text.Contains("SELECT", StringComparison.OrdinalIgnoreCase).Should().BeFalse();
        text.Contains("device_master_cloud_sim_view", StringComparison.OrdinalIgnoreCase).Should().BeFalse();
        text.Contains("recipe_cloud_sim_view", StringComparison.OrdinalIgnoreCase).Should().BeFalse();
        text.Contains("capacity_cloud_sim_view", StringComparison.OrdinalIgnoreCase).Should().BeFalse();
        text.Contains("production_data_cloud_sim_view", StringComparison.OrdinalIgnoreCase).Should().BeFalse();
        text.Contains("DeviceSemanticReadonly", StringComparison.OrdinalIgnoreCase).Should().BeFalse();
    }

    private async Task AssertSemanticChatAsync(
        Guid sessionId,
        string message,
        string expectedIntent,
        params string[] expectedTextFragments)
    {
        var events = await PostChatAsync(new
        {
            sessionId,
            message
        });

        events.Should().NotContain(item => item.Type == "Error");
        events.Should().Contain(item => item.Type == "Intent" && item.Content.Contains(expectedIntent, StringComparison.OrdinalIgnoreCase));

        var text = string.Concat(events.Where(item => item.Type == "Text").Select(item => item.Content));
        text.Should().NotBeNullOrWhiteSpace();
        text.Should().Contain("结论：");
        text.Should().Contain("关键指标：");
        text.Should().Contain("关键记录：");
        text.Should().Contain("查询范围：");

        foreach (var expectedTextFragment in expectedTextFragments)
        {
            text.Should().Contain(expectedTextFragment);
        }

        text.Contains("SELECT", StringComparison.OrdinalIgnoreCase).Should().BeFalse();
        text.Contains("device_master_view", StringComparison.OrdinalIgnoreCase).Should().BeFalse();
        text.Contains("device_log_view", StringComparison.OrdinalIgnoreCase).Should().BeFalse();
        text.Contains("device_master_cloud_sim_view", StringComparison.OrdinalIgnoreCase).Should().BeFalse();
        text.Contains("device_log_cloud_sim_view", StringComparison.OrdinalIgnoreCase).Should().BeFalse();
        text.Contains("recipe_cloud_sim_view", StringComparison.OrdinalIgnoreCase).Should().BeFalse();
        text.Contains("capacity_cloud_sim_view", StringComparison.OrdinalIgnoreCase).Should().BeFalse();
        text.Contains("production_data_cloud_sim_view", StringComparison.OrdinalIgnoreCase).Should().BeFalse();
        text.Contains("DeviceSemanticReadonly", StringComparison.OrdinalIgnoreCase).Should().BeFalse();
    }

    private async Task<T> GetJsonAsync<T>(string uri)
    {
        using var response = await _fixture.HttpClient.GetAsync(uri);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<T>(JsonOptions))!;
    }

    private async Task<T> PostJsonAsync<T>(string uri, object payload)
    {
        using var response = await _fixture.HttpClient.PostAsJsonAsync(uri, payload, JsonOptions);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync();
            throw new Xunit.Sdk.XunitException(
                $"POST '{uri}' failed with status {(int)response.StatusCode} ({response.StatusCode}). Response body: {body}");
        }

        return (await response.Content.ReadFromJsonAsync<T>(JsonOptions))!;
    }

    private async Task SendJsonAsync(HttpMethod method, string uri, object payload, HttpStatusCode? expectedStatusCode = null)
    {
        using var request = new HttpRequestMessage(method, uri)
        {
            Content = JsonContent.Create(payload)
        };

        using var response = await _fixture.HttpClient.SendAsync(request);
        if (expectedStatusCode.HasValue)
        {
            response.StatusCode.Should().Be(expectedStatusCode.Value);
            return;
        }

        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync();
        throw new Xunit.Sdk.XunitException(
            $"Request to '{uri}' failed with status {(int)response.StatusCode} ({response.StatusCode}). Response body: {body}");
    }

    private async Task<AiGatewayDbContext> CreateAiGatewayDbContextAsync(AICopilotAppFixture? fixture = null)
    {
        var connectionString = await (fixture ?? _fixture).GetConnectionStringAsync();
        var options = new DbContextOptionsBuilder<AiGatewayDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new AiGatewayDbContext(options);
    }

    private async Task<McpServerDbContext> CreateMcpDbContextAsync(AICopilotAppFixture? fixture = null)
    {
        var connectionString = await (fixture ?? _fixture).GetConnectionStringAsync();
        var options = new DbContextOptionsBuilder<McpServerDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new McpServerDbContext(options);
    }

    private async Task<DataAnalysisDbContext> CreateDataAnalysisDbContextAsync(AICopilotAppFixture? fixture = null)
    {
        var connectionString = await (fixture ?? _fixture).GetConnectionStringAsync();
        var options = new DbContextOptionsBuilder<DataAnalysisDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new DataAnalysisDbContext(options);
    }

    private async Task<SemanticBusinessDatabaseContext> ProvisionSemanticBusinessDatabaseAsync()
    {
        var businessConnectionString = await _fixture.GetConnectionStringAsync("cloud-device-semantic-sim");

        await using var businessConnection = new NpgsqlConnection(businessConnectionString);
        await businessConnection.OpenAsync();

        await using var validationCommand = businessConnection.CreateCommand();
        validationCommand.CommandText =
            """
            SELECT
                (SELECT COUNT(*) FROM device_master_cloud_sim_view),
                (SELECT COUNT(*) FROM device_log_cloud_sim_view),
                (SELECT COUNT(*) FROM recipe_cloud_sim_view),
                (SELECT COUNT(*) FROM capacity_cloud_sim_view),
                (SELECT COUNT(*) FROM production_data_cloud_sim_view);
            """;

        await using var reader = await validationCommand.ExecuteReaderAsync();
        if (!await reader.ReadAsync()
            || reader.GetInt32(0) <= 0
            || reader.GetInt32(1) <= 0
            || reader.GetInt32(2) <= 0
            || reader.GetInt32(3) <= 0
            || reader.GetInt32(4) <= 0)
        {
            throw new InvalidOperationException("Cloud semantic simulation source is not ready.");
        }

        return new SemanticBusinessDatabaseContext("cloud-device-semantic-sim", businessConnectionString);
    }

    private static IHost BuildMcpVerificationHost(string connectionString)
    {
        var builder = Host.CreateApplicationBuilder();

        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:ai-copilot"] = connectionString
        });

        builder.AddEfCore();
        builder.Services.AddLogging();
        builder.Services.AddSingleton<AgentPluginLoader>();
        builder.Services.AddScoped<IMcpServerBootstrap, TestMcpServerBootstrap>();

        return builder.Build();
    }

    private string BuildFakeAiBaseUrl()
    {
        return new Uri(_fixture.FakeAiBaseUri, "/v1").ToString().TrimEnd('/');
    }

    private static string GetTestingMcpExecutablePath()
    {
        var assemblyLocation = typeof(TestingMcpServerMarker).Assembly.Location;
        var executablePath = Path.ChangeExtension(assemblyLocation, ".exe");

        return File.Exists(executablePath)
            ? executablePath
            : assemblyLocation;
    }

    private static string GetDiagnosticChecklistToolName()
    {
        var plugin = new DiagnosticAdvisorPlugin();
        var tool = plugin.GetTools()
            ?.First(function => function.Name.Contains("DiagnosticChecklist", StringComparison.OrdinalIgnoreCase));

        return tool?.Name ?? "GenerateDiagnosticChecklist";
    }

    private static async Task<T> EventuallyAsync<T>(Func<Task<T>> action, Func<T, bool> predicate)
    {
        var deadline = DateTime.UtcNow + EventuallyTimeout;
        Exception? lastException = null;

        while (DateTime.UtcNow < deadline)
        {
            try
            {
                var result = await action();
                if (predicate(result))
                {
                    return result;
                }
            }
            catch (Exception ex)
            {
                lastException = ex;
            }

            await Task.Delay(EventuallyInterval);
        }

        if (lastException != null)
        {
            throw lastException;
        }

        throw new TimeoutException("EventuallyAsync timed out before the condition was satisfied.");
    }

    private sealed record InitializationStatusDto(
        bool HasAdminRole,
        bool HasUserRole,
        bool BootstrapAdminConfigured,
        bool HasAdminUser,
        bool IsInitialized);

    private sealed record LoginUserDto(string UserName, string Token);

    private sealed record CreatedUserDto(Guid UserId, string UserName, string RoleName);

    private sealed record CreatedLanguageModelDto(Guid Id, string Provider, string Name);

    private sealed record LanguageModelDto(
        Guid Id,
        string Provider,
        string Name,
        string BaseUrl,
        int MaxTokens,
        double Temperature,
        bool HasApiKey,
        string? ApiKeyMasked);

    private sealed record CreatedConversationTemplateDto(Guid Id, string Name);

    private sealed record ConversationTemplateDto(
        Guid Id,
        string Name,
        string Description,
        string SystemPrompt,
        Guid ModelId,
        int? MaxTokens,
        double? Temperature,
        bool IsEnabled);

    private sealed record CreatedApprovalPolicyDto(Guid Id, string Name);

    private sealed record ApprovalPolicyDto(
        Guid Id,
        string Name,
        string? Description,
        ApprovalTargetType TargetType,
        string TargetName,
        IReadOnlyCollection<string> ToolNames,
        bool IsEnabled,
        bool RequiresOnsiteAttestation);

    private sealed record CreatedEmbeddingModelDto(Guid Id, string Name);

    private sealed record EmbeddingModelDto(
        Guid Id,
        string Name,
        string Provider,
        string BaseUrl,
        string ModelName,
        int Dimensions,
        int MaxTokens,
        bool IsEnabled,
        bool HasApiKey,
        string? ApiKeyMasked);

    private sealed record CreatedKnowledgeBaseDto(Guid Id, string Name);

    private sealed record KnowledgeBaseDto(
        Guid Id,
        string Name,
        string Description,
        Guid EmbeddingModelId,
        int DocumentCount);

    private sealed record CreatedBusinessDatabaseDto(Guid Id, string Name);

    private sealed record BusinessDatabaseDto(
        Guid Id,
        string Name,
        string Description,
        int Provider,
        bool IsEnabled,
        bool IsReadOnly,
        DateTime CreatedAt,
        bool HasConnectionString,
        string? ConnectionStringMasked);

    private sealed record CreatedMcpServerDto(Guid Id, string Name);

    private sealed record McpServerDto(
        Guid Id,
        string Name,
        string Description,
        int TransportType,
        string? Command,
        bool HasArguments,
        string? ArgumentsMasked,
        ChatExposureMode ChatExposureMode,
        IReadOnlyCollection<string> AllowedToolNames,
        bool IsEnabled);

    private sealed record CreatedSessionDto(
        Guid Id,
        string Title,
        DateTimeOffset? OnsiteConfirmedAt,
        string? OnsiteConfirmedBy,
        DateTimeOffset? OnsiteConfirmationExpiresAt);

    private sealed record SessionDto(
        Guid Id,
        string Title,
        DateTimeOffset? OnsiteConfirmedAt,
        string? OnsiteConfirmedBy,
        DateTimeOffset? OnsiteConfirmationExpiresAt);

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
        string? OperatorUserName,
        string? OperatorRoleName,
        string Result,
        string Summary,
        IReadOnlyCollection<string> ChangedFields,
        DateTime CreatedAt);

    private sealed record ChatHistoryMessageDto(
        Guid SessionId,
        string Role,
        string Content,
        DateTime CreatedAt);

    private sealed record SemanticSourceStatusDto(
        string Target,
        string? DatabaseName,
        string? SourceName,
        string? EffectiveSourceName,
        bool IsEnabled,
        bool IsReadOnly,
        bool SourceExists,
        bool ProviderMatched,
        IReadOnlyCollection<string> MissingRequiredFields,
        string Status);

    private sealed record UploadDocumentDto(int Id, string Status);

    private sealed record KnowledgeDocumentDto(
        int Id,
        Guid KnowledgeBaseId,
        string Name,
        string Extension,
        DocumentStatus Status,
        int ChunkCount,
        string? ErrorMessage,
        DateTime CreatedAt,
        DateTime? ProcessedAt);

    private sealed record SearchKnowledgeBaseResult(string Text, double Score, int DocumentId, string? DocumentName);

    private sealed record ChatChunkDto(string Source, string Type, string Content);

    private sealed record ProblemChunkDto(string? Code, string? Detail, string? UserFacingMessage);

    private sealed record SemanticBusinessDatabaseContext(string DatabaseName, string ConnectionString);
}
