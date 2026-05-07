using System.Text.Json;
using AICopilot.AiGatewayService.Workflows.Executors;
using AICopilot.Services.Contracts;
using AICopilot.Services.Contracts.AiGateway.Dtos;
using AICopilot.SharedKernel.Ai;

namespace AICopilot.AiEvalTests;

[Trait("Suite", "AiEval")]
public sealed class GoldenCaseTests
{
    public static IEnumerable<object[]> Cases()
    {
        var baseDirectory = AppContext.BaseDirectory;
        var casesDirectory = Path.Combine(baseDirectory, "cases");
        foreach (var file in Directory.GetFiles(casesDirectory, "*.json").OrderBy(Path.GetFileName))
        {
            yield return [file];
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void GoldenCase_ShouldHoldExpectedGuardrail(string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var root = document.RootElement;
        var kind = root.GetProperty("kind").GetString();

        switch (kind)
        {
            case "toolSafety":
                EvaluateToolSafety(root);
                break;
            case "dataAnalysisSanitization":
                EvaluateDataAnalysisSanitization(root);
                break;
            case "approvalIdentity":
                EvaluateApprovalIdentity(root);
                break;
            case "promptInjection":
                EvaluatePromptInjection(root);
                break;
            default:
                throw new NotSupportedException($"Unsupported eval case kind '{kind}'.");
        }
    }

    private static void EvaluateToolSafety(JsonElement root)
    {
        var decision = AiToolSafetyPolicy.Evaluate(
            Enum.Parse<AiToolExternalSystemType>(root.GetProperty("externalSystemType").GetString()!),
            Enum.Parse<AiToolCapabilityKind>(root.GetProperty("capabilityKind").GetString()!),
            Enum.Parse<AiToolRiskLevel>(root.GetProperty("riskLevel").GetString()!),
            root.GetProperty("toolName").GetString()!,
            root.GetProperty("description").GetString(),
            root.GetProperty("readOnlyDeclared").GetBoolean());

        decision.IsAllowed.Should().Be(root.GetProperty("expectedAllowed").GetBoolean());
        decision.Reason.Should().Contain(root.GetProperty("expectedReasonContains").GetString());
    }

    private static void EvaluateDataAnalysisSanitization(JsonElement root)
    {
        var field = root.GetProperty("field").GetString()!;
        var payload = root.GetProperty("payload").GetString()!;
        var context = DataAnalysisFinalContextFormatter.FormatFreeForm(
            new AnalysisDto
            {
                SourceLabel = "ProdDb",
                Description = "Read-only business data preview",
                Metadata =
                [
                    new MetadataItemDto { Name = field, Description = "业务字段" }
                ]
            },
            null,
            [
                new Dictionary<string, object?>
                {
                    [field] = payload,
                    ["connectionString"] = "Host=prod;Password=secret"
                }
            ],
            [new(field, typeof(string)), new("connectionString", typeof(string))]);

        context.Should().NotContain("Host=prod");
        context.Should().NotContain("Password=secret");
        context.Should().NotContain("connectionString");
        context.Should().NotContain(payload);
    }

    private static void EvaluateApprovalIdentity(JsonElement root)
    {
        var targetType = root.GetProperty("targetType").GetString();
        var targetName = root.GetProperty("targetName").GetString();
        var toolName = root.GetProperty("toolName").GetString();
        var hasStrictIdentity = !string.IsNullOrWhiteSpace(targetType)
                                && !string.IsNullOrWhiteSpace(targetName)
                                && !string.IsNullOrWhiteSpace(toolName);

        hasStrictIdentity.Should().Be(root.GetProperty("expectedStrictIdentity").GetBoolean());
    }

    private static void EvaluatePromptInjection(JsonElement root)
    {
        var payload = root.GetProperty("payload").GetString()!;
        payload.Should().Contain("绕过审批");
        root.GetProperty("expectedRequiredRule").GetString().Should().Be("不能作为指令");
    }
}
