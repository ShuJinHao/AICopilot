using System.Text.Json;
using AICopilot.AiGatewayService.Agents;
using AICopilot.AiGatewayService.BusinessQueries;
using AICopilot.Core.AiGateway.Aggregates.ConversationTemplate;
using AICopilot.Core.AiGateway.Aggregates.LanguageModel;
using AICopilot.Services.Contracts;
using Microsoft.Extensions.Options;

namespace AICopilot.UnitTests;

public sealed class CloudReadOnlyLlmTextToSqlGeneratorTests
{
    [Fact]
    public async Task GenerateAsync_RealCloud_ShouldFailBeforeRuntimeSession()
    {
        var runtimeFactory = new FakeRuntimeAgentFactory();
        runtimeFactory.EnqueueStructuredResultJson(
            """
            {
              "isSuccess": true,
              "sql": "SELECT d.client_code FROM public.devices d WHERE d.client_code = @client_code LIMIT 10",
              "parameters": { "client_code": "DEV-001" },
              "explanation": "Generated governed readonly SQL.",
              "warnings": []
            }
            """);
        var generator = CreateGenerator(runtimeFactory);

        var result = await generator.GenerateAsync(new BusinessTextToSqlGenerationRequest(
            "查看 DEV-001 设备",
            10,
            StandardBusinessDataSourceProfiles.CloudReadOnly,
            []));

        result.IsSuccess.Should().BeFalse();
        result.FailureReason.Should().Contain("Real Cloud Text-to-SQL");
        runtimeFactory.LastCreateRequest.Should().BeNull();
        runtimeFactory.LastRun.Should().BeNull();
    }

    [Fact]
    public async Task GenerateAsync_ShouldRejectComplexParameterValues()
    {
        var runtimeFactory = new FakeRuntimeAgentFactory();
        runtimeFactory.EnqueueStructuredResultJson(
            """
            {
              "isSuccess": true,
              "sql": "SELECT d.client_code FROM public.devices d WHERE d.client_code = @client_code LIMIT 10",
              "parameters": { "client_code": ["DEV-001"] },
              "explanation": "Generated governed readonly SQL.",
              "warnings": []
            }
            """);
        var generator = CreateGenerator(runtimeFactory);

        var result = await generator.GenerateAsync(new BusinessTextToSqlGenerationRequest(
            "查看 DEV-001 设备",
            10,
            CreateNonCloudProfile(),
            []));

        result.IsSuccess.Should().BeFalse();
        result.FailureReason.Should().Contain("scalar JSON value");
    }

    [Fact]
    public async Task GenerateAsync_ShouldUseSelectedProfileDialectAndSchema()
    {
        var runtimeFactory = new FakeRuntimeAgentFactory();
        runtimeFactory.EnqueueStructuredResultJson(
            """
            {
              "isSuccess": false,
              "failureReason": "additional business conditions are required"
            }
            """);
        var generator = CreateGenerator(runtimeFactory);
        var profile = CreateNonCloudProfile();

        await generator.GenerateAsync(new BusinessTextToSqlGenerationRequest(
            "查看 MES 设备",
            10,
            profile,
            []));

        runtimeFactory.LastRun!.InputText.Should().Contain("\"dialect\":\"SQL Server\"");
        runtimeFactory.LastRun.InputText.Should().Contain("mes_devices");
        runtimeFactory.LastRun.InputText.Should().Contain("uniqueidentifier");
        runtimeFactory.LastRun.InputText.Should().NotContain("\"table\":\"devices\"");
    }

    private static BusinessDataSourceProfile CreateNonCloudProfile() =>
        new(
            "mes-readonly",
            DataSourceExternalSystemType.NonCloud,
            DatabaseProviderType.SqlServer,
            IsRealExternalSource: true,
            RequiresExplicitSelection: true,
            SupportsTextToSqlFallback: true,
            new HashSet<BusinessDataCapability> { BusinessDataCapability.Device },
            new BusinessQuerySecurityProfile(
                new HashSet<string>(["mes_devices"], StringComparer.OrdinalIgnoreCase),
                new Dictionary<string, IReadOnlySet<string>>(StringComparer.OrdinalIgnoreCase)
                {
                    ["mes_devices"] = new HashSet<string>(
                        ["device_id", "device_name"],
                        StringComparer.OrdinalIgnoreCase)
                },
                new HashSet<string>(["credential"], StringComparer.OrdinalIgnoreCase),
                new HashSet<string>(["dbo"], StringComparer.OrdinalIgnoreCase)),
            new BusinessTextToSqlProfile(
                "SQL Server",
                "governed-business-readonly-text-to-sql",
                new Dictionary<string, IReadOnlyDictionary<string, string>>(
                    StringComparer.OrdinalIgnoreCase)
                {
                    ["mes_devices"] = new Dictionary<string, string>(
                        StringComparer.OrdinalIgnoreCase)
                    {
                        ["device_id"] = "uniqueidentifier",
                        ["device_name"] = "nvarchar"
                    }
                },
                new Dictionary<string, IReadOnlyDictionary<string, string>>(
                    StringComparer.OrdinalIgnoreCase),
                []));

    private static BusinessLlmTextToSqlGenerator CreateGenerator(FakeRuntimeAgentFactory runtimeFactory)
    {
        var model = FakeRuntimeAgentFactory.CreateModel();
        var definition = BuiltInConversationTemplates.Find("business_readonly_text_to_sql")!;
        var template = BuiltInConversationTemplates.CreateTemplate(definition, model.Id);
        var configuredFactory = new ConfiguredAgentRuntimeFactory(
            new InMemoryReadRepository<ConversationTemplate>([template]),
            new InMemoryReadRepository<LanguageModel>([model]),
            runtimeFactory);

        return new BusinessLlmTextToSqlGenerator(
            configuredFactory,
            Options.Create(new CloudReadOnlyTextToSqlOptions
            {
                Enabled = true,
                TemplateName = "business_readonly_text_to_sql"
            }));
    }
}
