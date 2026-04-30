using AICopilot.AgentPlugin;
using AICopilot.SharedKernel.Ai;
using System.ComponentModel;

namespace AICopilot.AiGatewayService.Plugins;

public class SystemOpsPlugin : AgentPluginBase
{
    public override string Description => "提供系统级别的运维操作能力，如时间查询、服务重启等。";

    public override ChatExposureMode ChatExposureMode => ChatExposureMode.Control;

    public override IEnumerable<string> HighRiskTools => [nameof(RestartServer)];

    [Description("获取当前系统时间")]
    public string GetSystemTime() => DateTime.Now.ToString("O");

    [Description("执行服务器重启操作")]
    public string RestartServer()
    {
        return "Server restart command issued successfully.";
    }
}
