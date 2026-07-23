using System.Text.Json;
using AICopilot.AiGatewayService.Agents;
using AICopilot.AiGatewayService.Workflows.Executors;
using AICopilot.Core.AiGateway.Aggregates.ConversationTemplate;
using AICopilot.Core.AiGateway.Aggregates.LanguageModel;
using AICopilot.Services.Contracts;
using Microsoft.Extensions.Options;

namespace AICopilot.UnitTests;

public sealed class CloudReadOnlyLlmTextToSqlGeneratorTests
{
    [Fact]
    public async Task GenerateAsync_ShouldUseConfiguredRuntimeSessionAndStructuredJson()
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

        result.IsSuccess.Should().BeTrue();
        result.Sql.Should().Contain("@client_code");
        result.Parameters.Should().ContainKey("client_code")
            .WhoseValue.Should().Be("DEV-001");

        runtimeFactory.LastCreateRequest.Should().NotBeNull();
        runtimeFactory.LastCreateRequest!.Template.Name.Should().Be("business_readonly_text_to_sql");
        runtimeFactory.LastCreateRequest.Options.Tools.Should().BeEmpty();
        runtimeFactory.LastCreateRequest.Options.Temperature.Should().Be(0);
        runtimeFactory.LastRun.Should().NotBeNull();
        runtimeFactory.LastRun!.Options!.Options.Tools.Should().BeEmpty();
        runtimeFactory.LastRun.Options.Options.Temperature.Should().Be(0);
        runtimeFactory.LastRun.InputText.Should().Contain("governedSchema");
        runtimeFactory.LastRun.InputText.Should().Contain("allowedSchemas");
        runtimeFactory.LastRun.InputText.Should().Contain("schema.table");
        runtimeFactory.LastRun.InputText.Should().Contain("columnTypes");
        runtimeFactory.LastRun.InputText.Should().Contain("valueHint");
        runtimeFactory.LastRun.InputText.Should().Contain("joinHints");
        runtimeFactory.LastRun.InputText.Should().Contain("devices");
        runtimeFactory.LastRun.InputText.Should().Contain("client_code");
        runtimeFactory.LastRun.InputText.Should().Contain("uuid");
        runtimeFactory.LastRun.InputText.Should().Contain("Allowed values are ERROR, WARN, INFO");
        runtimeFactory.LastRun.InputText.Should().Contain("device_logs.device_id");
        runtimeFactory.LastRun.InputText.Should().Contain("devices.id");
        runtimeFactory.LastRun.InputText.Should().NotContain("Password=");
        runtimeFactory.LastRun.InputText.Should().NotContain("bootstrap_secret_hash");
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
            StandardBusinessDataSourceProfiles.CloudReadOnly,
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
        var profile = new BusinessDataSourceProfile(
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
                TemplateName = "business_readonly_text_to_sql"
            }));
    }
}
