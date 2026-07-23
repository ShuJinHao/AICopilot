using System.Text.RegularExpressions;

namespace AICopilot.AiGatewayService.Workflows;

internal static partial class AgentTaskChatEvidenceReusePolicy
{
    private static readonly string[] ExplanationMarkers =
    [
        "为什么", "为何", "原因", "解释", "依据", "怎么得出", "如何得出", "怎么算", "why", "explain", "reason", "how did"
    ];

    private static readonly string[] ScopeMutationMarkers =
    [
        "换成", "换为", "换到", "换个", "改成", "改为", "改查", "改用", "调整为", "调整到", "变更为", "变成",
        "切换到", "另一个", "另一台", "另一条", "其他", "其它",
        "change to", "switch to", "instead"
    ];

    private static readonly string[] QueryActionMarkers =
    [
        "重新查询", "重新查", "重查", "再查询", "再查", "查询一下", "查询", "查一下", "查下", "查一查", "查查", "查看",
        "再看", "看一下", "看下", "看看", "给我看", "显示", "拉取", "对比", "比较", "query", "search", "check", "rerun", "compare"
    ];

    private static readonly string[] ScopeDimensionMarkers =
    [
        "设备", "工序", "机台", "产线", "日志", "错误", "异常", "警告", "告警", "报警", "信息级别", "级别",
        "时间", "范围", "窗口", "今天", "昨日", "昨天", "前天", "明天", "本周", "上周", "本月", "上月", "今年",
        "小时", "分钟", "天", "周", "月", "年", "device", "process", "machine", "line", "log", "error", "warning",
        "warn", "info", "time", "range", "today", "yesterday", "week", "month", "hour", "day"
    ];

    public static bool RequiresFreshReadOnlyQuery(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return false;
        }

        var normalized = message.Trim();
        if (!HasScopeDimension(normalized))
        {
            return false;
        }

        if (ContainsAny(normalized, ScopeMutationMarkers))
        {
            return true;
        }

        var isExplanation = ContainsAny(normalized, ExplanationMarkers);
        if (!isExplanation && ContainsAny(normalized, QueryActionMarkers))
        {
            return true;
        }

        return !isExplanation && IsTerseScopeFollowUp(normalized);
    }

    private static bool HasScopeDimension(string value)
    {
        return ContainsAny(value, ScopeDimensionMarkers) ||
               DeviceCodeRegex().IsMatch(value) ||
               CalendarDateRegex().IsMatch(value) ||
               RelativeQuantityRegex().IsMatch(value);
    }

    private static bool IsTerseScopeFollowUp(string value)
    {
        return value.EndsWith('呢') ||
               value.EndsWith("怎么样", StringComparison.Ordinal) ||
               value.StartsWith("那", StringComparison.Ordinal) ||
               value.Contains("what about", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ContainsAny(string value, IEnumerable<string> terms)
    {
        return terms.Any(term => value.Contains(term, StringComparison.OrdinalIgnoreCase));
    }

    [GeneratedRegex(@"(?<![A-Za-z0-9_-])[A-Za-z0-9]+(?:[-_][A-Za-z0-9]+)+(?![A-Za-z0-9_-])", RegexOptions.CultureInvariant)]
    private static partial Regex DeviceCodeRegex();

    [GeneratedRegex(@"(?<!\d)(?:20\d{2}[-/.年])?\d{1,2}[-/.月]\d{1,2}(?:日)?(?!\d)", RegexOptions.CultureInvariant)]
    private static partial Regex CalendarDateRegex();

    [GeneratedRegex(@"(?<!\d)\d{1,4}\s*(?:分钟|小时|天|周|月|年|minutes?|hours?|days?|weeks?|months?|years?)(?![A-Za-z])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex RelativeQuantityRegex();
}
