namespace AICopilot.AiGatewayService.Safety;

public enum ManufacturingSceneType
{
    DeviceAnomalyDiagnosis,
    ParameterRecommendation,
    LogRootCause,
    KnowledgeQnA,
    ControlBlocked,
    FallbackToExistingRouting
}

public sealed record ManufacturingSceneDecision(
    ManufacturingSceneType Scene,
    string Reason);

public interface IManufacturingSceneClassifier
{
    ManufacturingSceneDecision Classify(string? message);
}

public sealed class KeywordManufacturingSceneClassifier : IManufacturingSceneClassifier
{
    private static readonly string[] ControlKeywords =
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

    private static readonly string[] ParameterRecommendationKeywords =
    [
        "parameter recommendation",
        "parameter suggestion",
        "recipe recommendation",
        "recipe suggestion",
        "recommend parameter",
        "recommend recipe",
        "optimize parameter",
        "optimize recipe",
        "yield",
        "良率",
        "参数建议",
        "配方建议",
        "推荐参数",
        "推荐配方",
        "优化参数",
        "优化配方",
        "怎么调参数"
    ];

    private static readonly string[] LogRootCauseKeywords =
    [
        "root cause",
        "timeline",
        "log",
        "alarm history",
        "error history",
        "日志",
        "根因",
        "时间线",
        "报警记录",
        "错误日志",
        "告警日志"
    ];

    private static readonly string[] DeviceAnomalyKeywords =
    [
        "alarm",
        "fault",
        "abnormal",
        "down",
        "trip",
        "diagnose",
        "诊断",
        "异常",
        "故障",
        "报警",
        "停机原因",
        "为什么停了"
    ];

    private static readonly string[] KnowledgeKeywords =
    [
        "policy",
        "rule",
        "knowledge",
        "standard",
        "sop",
        "process rule",
        "权限",
        "规则",
        "流程",
        "规范",
        "知识",
        "bootstrap",
        "生命周期",
        "版本"
    ];

    public ManufacturingSceneDecision Classify(string? message)
    {
        var normalized = message?.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return new ManufacturingSceneDecision(ManufacturingSceneType.FallbackToExistingRouting, "empty message");
        }

        if (ContainsAny(normalized, ControlKeywords))
        {
            return new ManufacturingSceneDecision(ManufacturingSceneType.ControlBlocked, "matched control keyword");
        }

        if (ContainsAny(normalized, ParameterRecommendationKeywords))
        {
            return new ManufacturingSceneDecision(ManufacturingSceneType.ParameterRecommendation, "matched recommendation keyword");
        }

        if (ContainsAny(normalized, LogRootCauseKeywords))
        {
            return new ManufacturingSceneDecision(ManufacturingSceneType.LogRootCause, "matched log/root-cause keyword");
        }

        if (ContainsAny(normalized, DeviceAnomalyKeywords))
        {
            return new ManufacturingSceneDecision(ManufacturingSceneType.DeviceAnomalyDiagnosis, "matched anomaly keyword");
        }

        if (ContainsAny(normalized, KnowledgeKeywords))
        {
            return new ManufacturingSceneDecision(ManufacturingSceneType.KnowledgeQnA, "matched knowledge keyword");
        }

        return new ManufacturingSceneDecision(ManufacturingSceneType.FallbackToExistingRouting, "no manufacturing scene keyword matched");
    }

    private static bool ContainsAny(string message, IEnumerable<string> keywords)
    {
        return keywords.Any(keyword => message.Contains(keyword, StringComparison.OrdinalIgnoreCase));
    }
}
