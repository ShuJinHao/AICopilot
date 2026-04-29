using AICopilot.AgentPlugin;
using AICopilot.SharedKernel.Ai;
using System.ComponentModel;

namespace AICopilot.AiGatewayService.Plugins;

public class DiagnosticAdvisorPlugin : AgentPluginBase
{
    public override string Description =>
        "提供设备异常诊断清单、根因排查建议和参数复核建议。输出仅供人工确认，不会执行任何控制动作。";

    public override ChatExposureMode ChatExposureMode => ChatExposureMode.Advisory;

    public override IEnumerable<string> HighRiskTools => [nameof(GenerateDiagnosticChecklist)];

    [Description("根据异常现象生成只读的设备诊断清单与人工复核建议，不执行任何控制动作。")]
    public string GenerateDiagnosticChecklist(string issueSummary)
    {
        return $"""
                诊断清单：
                1. 先核对异常现象与发生时间：{issueSummary}
                2. 查看同时间段设备日志、告警级别和最近一次状态变化。
                3. 对比最近生效配方、关键参数和良率波动。
                4. 如需调整参数，只能形成建议，由现场人员人工确认后执行。
                """;
    }
}
