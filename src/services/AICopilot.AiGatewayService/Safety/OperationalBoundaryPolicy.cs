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
        "修改配方",
        "禁用设备",
        "启用设备",
        "删除设备",
        "删除日志",
        "删除设备日志",
        "删除这条设备日志",
        "补录",
        "补录产能",
        "补录昨天",
        "纠正产能",
        "上传生产数据",
        "上传这批生产数据",
        "上传这批",
        "提交生产数据",
        "写入云端",
        "修改 Cloud",
        "修改Cloud",
        "处理 Cloud 业务",
        "处理Cloud业务",
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

        if (LooksLikeReadonlyQuestion(normalized) && !LooksLikeExecutionRequest(normalized))
        {
            decision = null;
            return false;
        }

        const string detail = "AICopilot 仅提供观测、诊断、建议和知识问答，不执行任何控制、写入、下发、Cloud 业务修改或状态变更动作。";
        decision = new OperationalBoundaryDecision(
            AppProblemCodes.ControlActionBlocked,
            detail,
            """
            我不能直接执行重启、写参数、下发配方、PLC 写入、Cloud 业务修改或状态切换这类动作。
            需要修改 Cloud 业务数据时，请到 Cloud 端由人工执行，或等待 Cloud 未来显式提供受控 AI action API。我可以继续帮你做只读诊断、根因分析、参数复核建议或人工执行前检查清单。
            """);
        return true;
    }

    private static bool LooksLikeReadonlyQuestion(string message)
    {
        var questionCues = new[]
        {
            "吗",
            "能否",
            "能不能",
            "可以",
            "需要什么",
            "是什么",
            "什么关系",
            "要检查什么",
            "覆盖还是",
            "规则",
            "解释",
            "分析",
            "查看",
            "查询"
        };

        return questionCues.Any(cue => message.Contains(cue, StringComparison.OrdinalIgnoreCase));
    }

    private static bool LooksLikeExecutionRequest(string message)
    {
        var executionCues = new[]
        {
            "帮我修改",
            "帮我禁用",
            "帮我启用",
            "帮我删除",
            "帮我上传",
            "帮我补录",
            "请修改",
            "请禁用",
            "请启用",
            "请删除",
            "请上传",
            "请补录",
            "立即",
            "直接执行",
            "执行一下",
            "让它生效",
            "让它立即生效"
        };

        return executionCues.Any(cue => message.Contains(cue, StringComparison.OrdinalIgnoreCase));
    }
}
