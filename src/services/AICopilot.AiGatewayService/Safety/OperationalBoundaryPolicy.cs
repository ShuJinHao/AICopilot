using System.Diagnostics.CodeAnalysis;
using AICopilot.SharedKernel.Result;

namespace AICopilot.AiGatewayService.Safety;

public interface IOperationalBoundaryPolicy
{
    bool TryBlockControlRequest(
        string message,
        [NotNullWhen(true)] out OperationalBoundaryDecision? decision);
}

public sealed record OperationalBoundaryDecision(
    string Code,
    string Detail,
    string UserFacingMessage);

public sealed class ManufacturingOperationalBoundaryPolicy : IOperationalBoundaryPolicy
{
    private static readonly string[] ControlPhrases =
    [
        "restart the server",
        "restart server",
        "reboot the server",
        "shutdown the server",
        "stop the service",
        "start the service",
        "write plc",
        "write to plc",
        "download recipe",
        "push recipe",
        "set parameter",
        "change parameter",
        "write parameter",
        "重启服务器",
        "重启服务",
        "重启系统",
        "重启设备",
        "停机",
        "启动设备",
        "停止设备",
        "下发配方",
        "下发参数",
        "写入 plc",
        "写 plc",
        "写参数",
        "修改参数",
        "切换状态"
    ];

    public bool TryBlockControlRequest(
        string message,
        [NotNullWhen(true)] out OperationalBoundaryDecision? decision)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            decision = null;
            return false;
        }

        var normalized = message.Trim();
        if (!ControlPhrases.Any(phrase => normalized.Contains(phrase, StringComparison.OrdinalIgnoreCase)))
        {
            decision = null;
            return false;
        }

        const string detail = "AICopilot 仅提供观测、诊断、建议和知识问答，不执行任何控制、写入、下发或状态变更动作。";
        decision = new OperationalBoundaryDecision(
            AppProblemCodes.ControlActionBlocked,
            detail,
            """
            我不能直接执行重启、写参数、下发配方、PLC 写入或状态切换这类控制动作。
            如果你愿意，我可以继续帮你做诊断、根因分析、参数复核建议或人工执行前的检查清单。
            """);
        return true;
    }
}
