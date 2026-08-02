using System.Reflection;
using System.Text.Json;
using AICopilot.AiGatewayService.Agents;
using AICopilot.AiGatewayService.Tools;
using AICopilot.Core.AiGateway.Aggregates.Tools;
using AICopilot.HarnessTestKit;
using AICopilot.SharedKernel.Ai;

namespace AICopilot.GoldenEvalTests;

public sealed class AgentSafetyGoldenDatasetTests
{
    private const string DatasetResource =
        "AICopilot.GoldenEvalTests.datasets.v1.agent-safety-matrix.json";
    private const string TargetName = "golden-harness";
    private const string InputSchema =
        """{"type":"object","properties":{},"additionalProperties":false}""";
    private const string OutputSchema =
        """{"type":"object","properties":{},"additionalProperties":false}""";
    private static readonly Guid UserId =
        Guid.Parse("11111111-1111-4111-8111-111111111111");

    public static IEnumerable<object[]> Cases()
    {
        using var document = LoadDataset();
        foreach (var testCase in document.RootElement.GetProperty("cases").EnumerateArray())
        {
            yield return [testCase.GetProperty("id").GetString()!];
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task HarnessToolGate_ShouldMatchVersionedReadOnlySafetyDataset(string caseId)
    {
        using var document = LoadDataset();
        document.RootElement.GetProperty("changeReason").GetString()
            .Should().NotBeNullOrWhiteSpace();
        var testCase = document.RootElement.GetProperty("cases")
            .EnumerateArray()
            .Single(candidate => candidate.GetProperty("id").GetString() == caseId);
        var input = testCase.GetProperty("input");
        var toolName = input.GetProperty("toolName").GetString()!;
        var riskLevel = Enum.Parse<AiToolRiskLevel>(input.GetProperty("riskLevel").GetString()!);
        var requiresApproval = riskLevel is
            AiToolRiskLevel.RequiresApproval or AiToolRiskLevel.High or AiToolRiskLevel.Critical;
        var toolCode = AiToolIdentity.CreateRuntimeName(
            AiToolTargetType.Plugin,
            TargetName,
            toolName);
        using var inputSchema = JsonDocument.Parse(InputSchema);
        using var outputSchema = JsonDocument.Parse(OutputSchema);
        var tool = new AiToolDefinition
        {
            Name = toolCode,
            ToolName = toolName,
            Description = input.GetProperty("description").GetString(),
            TargetType = AiToolTargetType.Plugin,
            TargetName = TargetName,
            ExternalSystemType = Enum.Parse<AiToolExternalSystemType>(
                input.GetProperty("externalSystemType").GetString()!),
            CapabilityKind = Enum.Parse<AiToolCapabilityKind>(
                input.GetProperty("capabilityKind").GetString()!),
            RiskLevel = riskLevel,
            RequiresApproval = requiresApproval,
            RequiredPermission = "AiGateway.Chat",
            AuditLevel = nameof(ToolAuditLevel.Standard),
            DataBoundary = nameof(ToolDataBoundary.GovernedBusinessReadOnly),
            SchemaVersion = 1,
            ReadOnlyDeclared = input.GetProperty("readOnlyDeclared").GetBoolean(),
            JsonSchema = inputSchema.RootElement.Clone(),
            ReturnJsonSchema = outputSchema.RootElement.Clone()
        };
        var registration = new ToolRegistration(
            toolCode,
            toolName,
            tool.Description!,
            ToolProviderType.BuiltIn,
            ToolRegistrationTargetType.Plugin,
            TargetName,
            InputSchema,
            OutputSchema,
            riskLevel,
            "AiGateway.Chat",
            requiresApproval,
            isEnabled: true,
            timeoutSeconds: 30,
            ToolAuditLevel.Standard,
            DateTimeOffset.UtcNow,
            dataBoundary: ToolDataBoundary.GovernedBusinessReadOnly,
            schemaVersion: 1);
        var access = new StubIdentityAccessService(["AiGateway.Chat"]);
        var gate = new MainChatToolGate(
            new ToolRegistryGuard(
                new InMemoryRepository<ToolRegistration>(registration),
                access),
            access,
            new TestCurrentUser(UserId));

        var exposed = await gate.FilterRegisteredAsync([tool], CancellationToken.None);

        exposed.Contains(tool).Should().Be(
            testCase.GetProperty("expected").GetProperty("exposed").GetBoolean());
    }

    private static JsonDocument LoadDataset()
    {
        var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(DatasetResource)
                     ?? throw new InvalidOperationException(
                         $"Golden dataset resource is missing: {DatasetResource}");
        return JsonDocument.Parse(stream);
    }
}
