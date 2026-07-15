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
public sealed class ConfigurationManagementEndToEndTests : EndToEndScenarioTestBase
{
    public ConfigurationManagementEndToEndTests(CloudSemanticSimulationAICopilotAppFixture fixture)
        : base(fixture)
    {
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
        languageModel.ApiKeyPreview.Should().Be("******");
        languageModel.ConnectivityStatus.Should().Be("Unknown");
        JsonSerializer.Serialize(languageModel, JsonOptions).Should().NotContain("sk-language-model");

        await SendJsonAsync(HttpMethod.Put, "/api/aigateway/language-model", new
        {
            id = languageModelId,
            provider = "OpenAI",
            name = "fake-chat-model-updated",
            baseUrl = BuildFakeAiBaseUrl(),
            apiKey = "sk-language-model-updated",
            maxTokens = 4096,
            maxOutputTokens = 1024,
            usages = new[] { "Chat", "Routing" },
            temperature = 0.25
        });

        var languageModelList = await GetJsonAsync<List<LanguageModelDto>>("/api/aigateway/language-model/list");
        languageModelList.Should().Contain(item => item.Id == languageModelId && item.Name == "fake-chat-model-updated");
        languageModelList.Single(item => item.Id == languageModelId).Usages.Should().Contain("Chat").And.Contain("Routing");
        languageModelList.Single(item => item.Id == languageModelId).ConnectivityStatus.Should().Be("Unknown");

        var failedConnectivityTest = await PostJsonAsync<LanguageModelTestResultDto>("/api/aigateway/language-model/test", new
        {
            provider = "OpenAI",
            protocolType = "OpenAICompatible",
            name = "fake-chat-model-unsaved",
            baseUrl = BuildFakeAiBaseUrl(),
            apiKey = "",
            contextWindowTokens = 4096,
            maxOutputTokens = 16,
            usages = new[] { "Chat" },
            temperature = 0,
            persistResult = false
        });
        failedConnectivityTest.Success.Should().BeFalse();
        failedConnectivityTest.Error.Should().Contain("API Key");

        var connectivityTest = await PostJsonAsync<LanguageModelTestResultDto>("/api/aigateway/language-model/test", new
        {
            id = languageModelId,
            persistResult = true
        });
        connectivityTest.Success.Should().BeTrue();
        connectivityTest.Status.Should().Be("Succeeded");

        languageModelList = await GetJsonAsync<List<LanguageModelDto>>("/api/aigateway/language-model/list");
        languageModelList.Single(item => item.Id == languageModelId).ConnectivityStatus.Should().Be("Succeeded");
        languageModelList.Single(item => item.Id == languageModelId).ConnectivityCheckedAt.Should().NotBeNull();

        var selectableChatModels = await GetJsonAsync<List<SelectableChatModelDto>>("/api/aigateway/language-model/chat-options");
        selectableChatModels.Should().Contain(item => item.Id == languageModelId && item.Name == "fake-chat-model-updated");

        var firstRoutingModel = await PostJsonAsync<RoutingModelConfigurationDto>("/api/aigateway/routing-model", new
        {
            name = $"cfg-routing-a-{Guid.NewGuid():N}",
            modelId = languageModelId,
            isActive = true
        });
        firstRoutingModel.IsActive.Should().BeTrue();

        var secondRoutingModel = await PostJsonAsync<RoutingModelConfigurationDto>("/api/aigateway/routing-model", new
        {
            name = $"cfg-routing-b-{Guid.NewGuid():N}",
            modelId = languageModelId,
            isActive = true
        });

        var routingModels = await GetJsonAsync<List<RoutingModelConfigurationDto>>("/api/aigateway/routing-model/list");
        routingModels.Where(item => item.IsActive).Should().ContainSingle()
            .Which.Id.Should().Be(secondRoutingModel.Id);

        await SendJsonAsync(HttpMethod.Put, "/api/aigateway/routing-model/activate", new { id = firstRoutingModel.Id });
        routingModels = await GetJsonAsync<List<RoutingModelConfigurationDto>>("/api/aigateway/routing-model/list");
        routingModels.Where(item => item.IsActive).Should().ContainSingle()
            .Which.Id.Should().Be(firstRoutingModel.Id);

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
        embeddingModel.ApiKeyPreview.Should().Be("******");
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
            externalSystemType = AiToolExternalSystemType.CloudReadOnly,
            capabilityKind = AiToolCapabilityKind.ReadOnlyQuery,
            chatExposureMode = ChatExposureMode.Disabled,
            allowedTools = Array.Empty<object>(),
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
        await SendJsonAsync(HttpMethod.Delete, "/api/aigateway/routing-model", new { id = firstRoutingModel.Id }, HttpStatusCode.NoContent);
        await SendJsonAsync(HttpMethod.Delete, "/api/aigateway/routing-model", new { id = secondRoutingModel.Id }, HttpStatusCode.NoContent);
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

            entity.ApiKey.Should().StartWith(SecretStringEncryptor.CipherPrefix);
            entity.ApiKey.Should().NotContain("sk-keep-original");
            SecretStringEncryptor.Decrypt(entity.ApiKey).Should().Be("sk-keep-original");
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
                provider = 1,
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
}
