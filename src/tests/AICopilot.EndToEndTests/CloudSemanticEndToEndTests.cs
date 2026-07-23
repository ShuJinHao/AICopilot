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
using AICopilot.SharedKernel.Result;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Npgsql;


namespace AICopilot.EndToEndTests;

[Collection(CloudSemanticSimulationBackendTestCollection.Name)]
public sealed class CloudSemanticEndToEndTests : EndToEndScenarioTestBase
{
    public CloudSemanticEndToEndTests(CloudSemanticSimulationAICopilotAppFixture fixture)
        : base(fixture)
    {
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
    public async Task CreateBusinessDatabase_ShouldRejectWritableBusinessDatabase()
    {
        await AuthenticateAsAdminAsync();

        var semanticDatabase = await ProvisionSemanticBusinessDatabaseAsync();
        Guid businessDatabaseId = Guid.Empty;
        Guid languageModelId = Guid.Empty;
        Guid routingConfigurationId = Guid.Empty;
        Guid generalTemplateId = Guid.Empty;
        Guid intentRoutingTemplateId = Guid.Empty;
        Guid sessionId = Guid.Empty;

        try
        {
            await DeleteBusinessDatabaseIfExistsAsync(ConfiguredSemanticPhysicalMappingProvider.DefaultDatabaseName);

            var exception = await Record.ExceptionAsync(async () =>
            {
                businessDatabaseId = await CreateBusinessDatabaseAsync(
                    ConfiguredSemanticPhysicalMappingProvider.DefaultDatabaseName,
                    connectionString: semanticDatabase.ConnectionString,
                    description: "semantic writable device/log database",
                    isReadOnly: false,
                    readOnlyCredentialVerified: true);
            });

            exception.Should().NotBeNull();
            exception!.Message.Should().Contain("业务库必须配置为只读");

            if (exception is null)
            {
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
    public async Task SemanticDeviceAndLogChat_ShouldNotFallbackWhenCloudAiReadIsDisabled()
    {
        await AuthenticateAsAdminAsync();

        var semanticDatabase = await ProvisionSemanticBusinessDatabaseAsync();
        Guid businessDatabaseId = Guid.Empty;
        Guid languageModelId = Guid.Empty;
        Guid routingConfigurationId = Guid.Empty;
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
                "sk-semantic-chat",
                usages: ["Chat", "Routing"]);

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
            routingConfigurationId = await CreateActiveRoutingModelAsync(languageModelId);

            sessionId = await CreateSessionAsync(generalTemplateId);

            await AssertSemanticChatAsync(
                sessionId,
                "list device master data",
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
                "ERROR",
                "Motor overload");

            await AssertSemanticChatAsync(
                sessionId,
                "列出设备主数据",
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
                "ERROR",
                "Motor overload");

            await EventuallyAsync(async () =>
            {
                await using var dbContext = await CreateAiGatewayDbContextAsync();
                return await dbContext.Messages
                    .Where(message => message.SessionId == sessionId)
                    .OrderBy(message => message.Id)
                    .ToListAsync();
            }, messages =>
                messages.Count >= 20 &&
                messages.Count(message => message.Type == MessageType.User) >= 10 &&
                messages.Count(message => message.Type == MessageType.Assistant) >= 10 &&
                messages.Where(message => message.Type == MessageType.Assistant).All(message =>
                    message.RenderPayloadJson is not null &&
                    message.RenderPayloadJson.Contains(AppProblemCodes.ChatStreamFailed, StringComparison.Ordinal) &&
                    !message.Content.Contains("Motor overload", StringComparison.OrdinalIgnoreCase)));
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

            if (routingConfigurationId != Guid.Empty)
            {
                await SendJsonAsync(HttpMethod.Delete, "/api/aigateway/routing-model", new { id = routingConfigurationId }, HttpStatusCode.NoContent);
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
    public async Task SemanticChat_ShouldBlockRecipeDataAndKeepCoveredDomainsCloudOnly()
    {
        await AuthenticateAsAdminAsync();

        var semanticDatabase = await ProvisionSemanticBusinessDatabaseAsync();
        Guid businessDatabaseId = Guid.Empty;
        Guid languageModelId = Guid.Empty;
        Guid routingConfigurationId = Guid.Empty;
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
                "sk-semantic-business",
                usages: ["Chat", "Routing"]);

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
            routingConfigurationId = await CreateActiveRoutingModelAsync(languageModelId);

            sessionId = await CreateSessionAsync(generalTemplateId);

            var cases = new (string Message, string Intent, string[] ExpectedFragments)[]
            {
                ("show capacity for DEV-001 from 2026-04-20T00:00:00Z to 2026-04-21T23:59:59Z", "Analysis.Capacity.Range", ["DEV-001", "126", "123"]),
                ("show capacity for device DEV-001", "Analysis.Capacity.ByDevice", ["DEV-001", "Cutting", "126"]),
                ("show latest production records for device DEV-001", "Analysis.ProductionData.Latest", ["DEV-001", "CELL-0002", "Fail"]),
                ("show production records for DEV-001 from 2026-04-21T00:00:00Z to 2026-04-21T23:59:59Z", "Analysis.ProductionData.Range", ["DEV-001", "CELL-0001", "CELL-0002"]),
                ("show production records for device DEV-001", "Analysis.ProductionData.ByDevice", ["DEV-001", "CELL-0001", "Station-A"]),
                ("查看 DEV-001 在 2026-04-20T00:00:00Z 到 2026-04-21T23:59:59Z 的产能", "Analysis.Capacity.Range", ["DEV-001", "126", "123"]),
                ("查看设备 DEV-001 的产能", "Analysis.Capacity.ByDevice", ["DEV-001", "Cutting", "126"]),
                ("查看设备 DEV-001 最新生产记录", "Analysis.ProductionData.Latest", ["DEV-001", "CELL-0002", "Fail"]),
                ("查看 DEV-001 在 2026-04-21T00:00:00Z 到 2026-04-21T23:59:59Z 的生产记录", "Analysis.ProductionData.Range", ["DEV-001", "CELL-0001", "CELL-0002"]),
                ("查看设备 DEV-001 的生产记录", "Analysis.ProductionData.ByDevice", ["DEV-001", "CELL-0001", "Station-A"])
            };

            foreach (var testCase in cases)
            {
                await AssertSemanticChatAsync(sessionId, testCase.Message, testCase.Intent, testCase.ExpectedFragments);
            }

            var recipeBoundaryCases = new (string Message, string Intent)[]
            {
                ("list recipes for device DEV-001", "Analysis.Recipe.List"),
                ("show recipe Recipe-Cut-01 detail", "Analysis.Recipe.Detail"),
                ("show recipe Recipe-Cut-01 version history", "Analysis.Recipe.VersionHistory"),
                ("列出设备 DEV-001 的配方", "Analysis.Recipe.List"),
                ("查看配方 Recipe-Cut-01 详情", "Analysis.Recipe.Detail"),
                ("查看配方 Recipe-Cut-01 的版本历史", "Analysis.Recipe.VersionHistory")
            };

            foreach (var testCase in recipeBoundaryCases)
            {
                await AssertRecipeDataReadBlockedAsync(sessionId, testCase.Message, testCase.Intent);
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

            if (routingConfigurationId != Guid.Empty)
            {
                await SendJsonAsync(HttpMethod.Delete, "/api/aigateway/routing-model", new { id = routingConfigurationId }, HttpStatusCode.NoContent);
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
}
