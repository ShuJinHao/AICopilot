using System.Text.Json;
using System.Text.RegularExpressions;
using AICopilot.AiGatewayService.Models;

namespace AICopilot.AiGatewayService.Workflows.Executors;

internal static class IntentRoutingFallbackClassifier
{
    private static readonly Regex LineNamePattern = new(@"\bLINE[-_ ]?[A-Z0-9]+\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex DeviceCodePattern = new(@"\bDEV[-_ ]?\d{3,}\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static bool TryClassify(string? message, string reason, out List<IntentResult> intents)
    {
        intents = [];
        if (string.IsNullOrWhiteSpace(message))
        {
            return false;
        }

        var normalized = message.Trim();
        var lineName = NormalizeCode(LineNamePattern.Match(normalized).Value);
        var deviceCode = NormalizeCode(DeviceCodePattern.Match(normalized).Value);

        if (ContainsAny(normalized, "日志", "log", "告警", "报警"))
        {
            var intent = ContainsAny(normalized, "错误", "error", "异常")
                ? "Analysis.DeviceLog.ByLevel"
                : "Analysis.DeviceLog.Latest";
            intents = [CreateIntent(intent, normalized, reason, lineName, deviceCode)];
            return true;
        }

        if (ContainsAny(normalized, "产能", "产量", "合格", "良率", "output", "yield", "capacity"))
        {
            intents = [CreateIntent("Analysis.Capacity.ByDevice", normalized, reason, null, deviceCode)];
            return true;
        }

        if (ContainsAny(normalized, "生产记录", "过站", "条码", "barcode", "record"))
        {
            var intent = ContainsAny(normalized, "最新", "current", "当前")
                ? "Analysis.ProductionData.Latest"
                : "Analysis.ProductionData.ByDevice";
            intents = [CreateIntent(intent, normalized, reason, null, deviceCode)];
            return true;
        }

        if (ContainsAny(normalized, "设备", "device") &&
            ContainsAny(normalized, "状态", "当前", "列出", "列表", "清单", "status", "list"))
        {
            var intent = string.IsNullOrWhiteSpace(deviceCode)
                ? "Analysis.Device.List"
                : "Analysis.Device.Status";
            intents = [CreateIntent(intent, normalized, reason, lineName, deviceCode)];
            return true;
        }

        return false;
    }

    private static IntentResult CreateIntent(
        string intent,
        string queryText,
        string reason,
        string? lineName,
        string? deviceCode)
    {
        var filters = new List<SemanticFallbackFilter>();
        if (!string.IsNullOrWhiteSpace(lineName) && intent.StartsWith("Analysis.Device.", StringComparison.OrdinalIgnoreCase))
        {
            filters.Add(new SemanticFallbackFilter("lineName", "eq", lineName));
        }

        if (!string.IsNullOrWhiteSpace(deviceCode))
        {
            filters.Add(new SemanticFallbackFilter("deviceCode", "eq", deviceCode));
        }

        var payload = new SemanticFallbackPayload(
            queryText,
            filters,
            new SemanticFallbackSort(ResolveSortField(intent), ResolveSortDirection(intent)),
            ResolveLimit(intent));

        return new IntentResult
        {
            Intent = intent,
            Confidence = 0.88,
            Reasoning = $"{reason}; deterministic read-only semantic fallback",
            Query = JsonSerializer.Serialize(payload, JsonSerializerOptions.Web)
        };
    }

    private static string ResolveSortField(string intent)
    {
        if (intent.StartsWith("Analysis.Device.", StringComparison.OrdinalIgnoreCase))
        {
            return intent.EndsWith(".List", StringComparison.OrdinalIgnoreCase)
                ? "deviceCode"
                : "updatedAt";
        }

        return "occurredAt";
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
        SemanticFallbackSort Sort,
        int Limit);

    private sealed record SemanticFallbackFilter(string Field, string Operator, string Value);

    private sealed record SemanticFallbackSort(string Field, string Direction);
}
