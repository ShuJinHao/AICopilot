using System.Text.Json;
using System.Text.RegularExpressions;
using AICopilot.AiGatewayService.AgentTasks;
using AICopilot.AiGatewayService.Models;

namespace AICopilot.AiGatewayService.Workflows.Executors;

internal static class IntentRoutingFallbackClassifier
{
    private static readonly Regex DeviceCodePattern = new(@"\bDEV[-_ ]?\d{3,}\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static bool TryClassify(
        string? message,
        string reason,
        AgentIntentRegistrySnapshot registry,
        out List<IntentResult> intents)
    {
        intents = [];
        if (string.IsNullOrWhiteSpace(message))
        {
            return false;
        }

        var normalized = message.Trim();
        var deviceCode = NormalizeCode(DeviceCodePattern.Match(normalized).Value);

        if (ContainsAny(normalized, "客户端发布", "发布版本", "客户端版本", "client release", "client version"))
        {
            return TryCreateRegistered(
                "Analysis.ClientRelease.List",
                normalized,
                reason,
                deviceCode: null,
                registry,
                out intents);
        }

        if (ContainsAny(normalized, "工序主数据", "工序列表", "process master", "process list") ||
            (ContainsAny(normalized, "工序", "process") && ContainsAny(normalized, "列出", "列表", "清单", "查询", "list", "show")))
        {
            return TryCreateRegistered(
                "Analysis.Process.List",
                normalized,
                reason,
                deviceCode: null,
                registry,
                out intents);
        }

        if (ContainsAny(normalized, "日志", "log", "告警", "报警"))
        {
            var intent = ContainsAny(normalized, "错误", "error", "异常")
                ? "Analysis.DeviceLog.ByLevel"
                : "Analysis.DeviceLog.Latest";
            return TryCreateRegistered(intent, normalized, reason, deviceCode, registry, out intents);
        }

        if (ContainsAny(normalized, "产能", "产量", "合格", "良率", "output", "yield", "capacity"))
        {
            return TryCreateRegistered(
                "Analysis.Capacity.ByDevice",
                normalized,
                reason,
                deviceCode,
                registry,
                out intents);
        }

        if (ContainsAny(normalized, "生产记录", "过站", "条码", "barcode", "record"))
        {
            var intent = ContainsAny(normalized, "最新", "current", "当前")
                ? "Analysis.ProductionData.Latest"
                : "Analysis.ProductionData.ByDevice";
            return TryCreateRegistered(intent, normalized, reason, deviceCode, registry, out intents);
        }

        if (LooksLikeRecentDeviceInformationRequest(normalized))
        {
            return TryCreateRegistered(
                "Analysis.DeviceLog.Latest",
                normalized,
                reason,
                deviceCode,
                registry,
                out intents);
        }

        if (ContainsAny(normalized, "设备", "device") &&
            ContainsAny(normalized, "状态", "当前", "列出", "列表", "清单", "status", "list"))
        {
            var intent = string.IsNullOrWhiteSpace(deviceCode)
                ? "Analysis.Device.List"
                : "Analysis.Device.Status";
            return TryCreateRegistered(intent, normalized, reason, deviceCode, registry, out intents);
        }

        return false;
    }

    private static bool TryCreateRegistered(
        string intentCode,
        string queryText,
        string reason,
        string? deviceCode,
        AgentIntentRegistrySnapshot registry,
        out List<IntentResult> intents)
    {
        intents = [];
        if (!AgentIntentRegistryV1.FallbackIntentCodes.Contains(intentCode, StringComparer.Ordinal) ||
            !registry.TryGet(intentCode, out _))
        {
            return false;
        }

        intents = [CreateIntent(intentCode, queryText, reason, deviceCode)];
        return true;
    }

    private static bool LooksLikeRecentDeviceInformationRequest(string message)
    {
        return ContainsAny(message, "设备", "机台", "device", "machine") &&
               ContainsAny(message, "最近", "近", "过去", "latest", "recent") &&
               ContainsAny(message, "信息", "情况", "数据", "整理", "分类", "表格", "图表", "摘要", "汇总", "分析", "chart", "table", "summary");
    }

    private static IntentResult CreateIntent(
        string intent,
        string queryText,
        string reason,
        string? deviceCode)
    {
        var filters = new List<SemanticFallbackFilter>();
        if (!string.IsNullOrWhiteSpace(deviceCode))
        {
            filters.Add(new SemanticFallbackFilter("deviceCode", "eq", deviceCode));
        }

        var payload = new SemanticFallbackPayload(
            queryText,
            filters,
            ResolveSort(intent),
            ResolveLimit(intent));

        return new IntentResult
        {
            Intent = intent,
            Confidence = 0.88,
            RoutingNote = $"{reason}; deterministic read-only semantic fallback",
            Query = JsonSerializer.Serialize(payload, JsonSerializerOptions.Web)
        };
    }

    private static SemanticFallbackSort? ResolveSort(string intent)
    {
        if (intent.StartsWith("Analysis.Process.", StringComparison.OrdinalIgnoreCase) ||
            intent.StartsWith("Analysis.ClientRelease.", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (intent.StartsWith("Analysis.Device.", StringComparison.OrdinalIgnoreCase))
        {
            return new SemanticFallbackSort(
                intent.EndsWith(".List", StringComparison.OrdinalIgnoreCase)
                    ? "deviceCode"
                    : "updatedAtUtc",
                ResolveSortDirection(intent));
        }

        return new SemanticFallbackSort("occurredAt", ResolveSortDirection(intent));
    }

    private static string ResolveSortDirection(string intent)
    {
        return intent.EndsWith(".List", StringComparison.OrdinalIgnoreCase)
            ? "asc"
            : "desc";
    }

    private static int ResolveLimit(string intent)
    {
        return intent.EndsWith(".Status", StringComparison.OrdinalIgnoreCase)
            ? 10
            : 20;
    }

    private static string? NormalizeCode(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Replace('_', '-').Replace(" ", string.Empty).ToUpperInvariant();
    }

    private static bool ContainsAny(string value, params string[] terms)
    {
        return terms.Any(term => value.Contains(term, StringComparison.OrdinalIgnoreCase));
    }

    private sealed record SemanticFallbackPayload(
        string QueryText,
        IReadOnlyList<SemanticFallbackFilter> Filters,
        SemanticFallbackSort? Sort,
        int Limit);

    private sealed record SemanticFallbackFilter(string Field, string Operator, string Value);

    private sealed record SemanticFallbackSort(string Field, string Direction);
}
