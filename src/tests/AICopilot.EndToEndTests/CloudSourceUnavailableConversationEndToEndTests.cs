using System.Net;
using AICopilot.DataAnalysisService.Semantics;

namespace AICopilot.EndToEndTests;

[Collection(CloudSemanticSimulationBackendTestCollection.Name)]
public sealed class CloudSourceUnavailableConversationEndToEndTests(
    CloudSemanticSimulationAICopilotAppFixture fixture)
    : EndToEndScenarioTestBase(fixture)
{
    [Fact]
    public async Task StructuredIntents_ShouldRefuseDirectDatabaseAndSimulationFallback()
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
            await DeleteBusinessDatabaseIfExistsAsync(
                ConfiguredSemanticPhysicalMappingProvider.DefaultDatabaseName);

            businessDatabaseId = await CreateBusinessDatabaseAsync(
                ConfiguredSemanticPhysicalMappingProvider.DefaultDatabaseName,
                semanticDatabase.ConnectionString,
                "fallback-probe readonly business database");

            languageModelId = await CreateLanguageModelAsync(
                $"cloud-source-boundary-lm-{Guid.NewGuid():N}",
                BuildFakeAiBaseUrl(),
                "sk-cloud-source-boundary",
                usages: ["Chat", "Routing"]);

            generalTemplateId = await CreateConversationTemplateAsync(
                $"CloudSourceBoundary-{Guid.NewGuid():N}",
                languageModelId,
                "Cloud source boundary assistant",
                "Use only the formal Cloud AiRead source.");

            await DeleteConversationTemplateIfExistsAsync("IntentRoutingAgent");
            intentRoutingTemplateId = await CreateConversationTemplateAsync(
                "IntentRoutingAgent",
                languageModelId,
                "intent routing",
                "Choose the best intent and return a JSON array. {{$IntentList}}");
            routingConfigurationId = await CreateActiveRoutingModelAsync(languageModelId);
            sessionId = await CreateSessionAsync(generalTemplateId);

            var structuredCases = new (string Message, string Intent)[]
            {
                ("列出设备主数据", "Analysis.Device.List"),
                ("查看设备 DEV-001 的详情", "Analysis.Device.Detail"),
                ("查看设备 DEV-001 最新日志", "Analysis.DeviceLog.Latest"),
                (
                    "查看设备 DEV-001 在 2026-04-20T00:00:00Z 到 2026-04-20T23:59:59Z 的日志",
                    "Analysis.DeviceLog.Range"),
                ("查看设备 DEV-001 的错误日志", "Analysis.DeviceLog.ByLevel"),
                (
                    "查看设备 DEV-001 在 2026-04-20T00:00:00Z 到 2026-04-21T23:59:59Z 的产能",
                    "Analysis.Capacity.Range"),
                ("查看设备 DEV-001 的产能", "Analysis.Capacity.ByDevice"),
                ("查看设备 DEV-001 最新生产记录", "Analysis.ProductionData.Latest"),
                (
                    "查看 DEV-001 在 2026-04-21T00:00:00Z 到 2026-04-21T23:59:59Z 的生产记录",
                    "Analysis.ProductionData.Range"),
                ("查看设备 DEV-001 的生产记录", "Analysis.ProductionData.ByDevice"),
                ("设备 DEV-001 现在是什么状态？", "Analysis.Device.Status"),
                ("列出工序主数据", "Analysis.Process.List"),
                ("查看 CUT 工序详情", "Analysis.Process.Detail"),
                (
                    "列出 stable 通道、win-x64 运行时的已发布客户端版本",
                    "Analysis.ClientRelease.List")
            };

            foreach (var testCase in structuredCases)
            {
                await AssertCloudOnlySourceUnavailableAsync(
                    sessionId,
                    testCase.Message,
                    testCase.Intent);
            }
        }
        finally
        {
            if (sessionId != Guid.Empty)
            {
                await SendJsonAsync(
                    HttpMethod.Delete,
                    "/api/aigateway/session",
                    new { id = sessionId },
                    HttpStatusCode.NoContent);
            }

            if (intentRoutingTemplateId != Guid.Empty)
            {
                await SendJsonAsync(
                    HttpMethod.Delete,
                    "/api/aigateway/conversation-template",
                    new { id = intentRoutingTemplateId },
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

            if (routingConfigurationId != Guid.Empty)
            {
                await SendJsonAsync(
                    HttpMethod.Delete,
                    "/api/aigateway/routing-model",
                    new { id = routingConfigurationId },
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

            if (businessDatabaseId != Guid.Empty)
            {
                await SendJsonAsync(
                    HttpMethod.Delete,
                    "/api/data-analysis/business-database",
                    new { id = businessDatabaseId },
                    HttpStatusCode.NoContent);
            }
        }
    }
}
