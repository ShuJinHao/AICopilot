using AICopilot.AgentPlugin;
using AICopilot.Core.AiGateway.Aggregates.Tools;
using AICopilot.SharedKernel.Ai;
using System.ComponentModel;
using System.Text.Json;

namespace AICopilot.AiGatewayService.Plugins;

public class DiagnosticAdvisorPlugin : AgentPluginBase
{
    public const string ToolName = nameof(GenerateDiagnosticChecklist);
    public const string ToolCode = "plugin__diagnosticadvisorplugin__generatediagnosticchecklist";
    private const string ToolDescription =
        "根据异常现象生成只读的设备诊断清单与人工复核建议，不执行任何控制动作。";
    private static readonly JsonElement InputSchema = ParseSchema(
        """{"type":"object","properties":{"issueSummary":{"type":"string"}},"required":["issueSummary"],"additionalProperties":false}""");
    private static readonly JsonElement OutputSchema = ParseSchema(
        """{"type":"object","properties":{"status":{"type":"string","enum":["advisory"]},"checklist":{"type":"array","items":{"type":"string"}}},"required":["status","checklist"],"additionalProperties":false}""");

    public override string Description =>
        "提供设备异常诊断清单、根因排查建议和参数复核建议。输出仅供人工确认，不会执行任何控制动作。";

    public override ChatExposureMode ChatExposureMode => ChatExposureMode.Advisory;

    public override IEnumerable<AiToolDefinition>? GetTools()
    {
        return
        [
            new AiToolDefinition
            {
                Name = ToolCode,
                ToolName = ToolName,
                Description = ToolDescription,
                RequiresApproval = true,
                TargetType = AiToolTargetType.Plugin,
                TargetName = nameof(DiagnosticAdvisorPlugin),
                ExternalSystemType = AiToolExternalSystemType.NonCloud,
                CapabilityKind = AiToolCapabilityKind.Diagnostics,
                RiskLevel = AiToolRiskLevel.RequiresApproval,
                RequiredPermission = "AiGateway.Chat",
                AuditLevel = "Standard",
                DataBoundary = "NoData",
                SchemaVersion = BuiltInToolRegistrations.CurrentSchemaVersion,
                ReadOnlyDeclared = true,
                JsonSchema = InputSchema,
                ReturnJsonSchema = OutputSchema,
                InvokeAsync = (context, _) =>
                {
                    if (!context.Arguments.TryGetValue("issueSummary", out var value) ||
                        value is not string issueSummary ||
                        string.IsNullOrWhiteSpace(issueSummary))
                    {
                        throw new ArgumentException(
                            "Diagnostic advisor requires a non-empty issueSummary.");
                    }

                    return ValueTask.FromResult<object?>(
                        GenerateDiagnosticChecklist(issueSummary.Trim()));
                }
            }
        ];
    }

    [Description(ToolDescription)]
    public DiagnosticChecklistResult GenerateDiagnosticChecklist(string issueSummary)
    {
        return new DiagnosticChecklistResult(
            "advisory",
            [
                $"先核对异常现象与发生时间：{issueSummary}",
                "查看同时间段设备日志、告警级别和最近一次状态变化。",
                "对比最近生效配方、关键参数和良率波动。",
                "如需调整参数，只能形成建议，由现场人员人工确认后执行。"
            ]);
    }

    private static JsonElement ParseSchema(string schema)
    {
        using var document = JsonDocument.Parse(schema);
        return document.RootElement.Clone();
    }
}

public sealed record DiagnosticChecklistResult(
    string Status,
    IReadOnlyCollection<string> Checklist);
