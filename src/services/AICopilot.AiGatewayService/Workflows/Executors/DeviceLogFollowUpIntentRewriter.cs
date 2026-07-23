using System.Text.Json;
using System.Text.RegularExpressions;
using AICopilot.AiGatewayService.Models;
using AICopilot.SharedKernel.Ai;

namespace AICopilot.AiGatewayService.Workflows.Executors;

internal static partial class DeviceLogFollowUpIntentRewriter
{
    private const string DeviceLogLatestIntent = "Analysis.DeviceLog.Latest";
    private const string DeviceLogByLevelIntent = "Analysis.DeviceLog.ByLevel";

    public static void Rewrite(List<IntentResult> intents, IReadOnlyList<AiChatMessage> history)
    {
        if (history.Count < 2)
        {
            return;
        }

        var currentMessage = history.LastOrDefault(message => message.Role == AiChatRole.User)?.Text;
        if (!IsDeviceLogFollowUp(currentMessage))
        {
            return;
        }

        var previousDeviceLogQuestion = history
            .Take(history.Count - 1)
            .Where(message => message.Role == AiChatRole.User)
            .Select(message => message.Text)
            .Where(IsDeviceLogSeedQuestion)
            .LastOrDefault();
        if (string.IsNullOrWhiteSpace(previousDeviceLogQuestion))
        {
            return;
        }

        var levelFilter = InferCurrentLevelFilter(currentMessage);
        var scopeFilter = InferDeviceCodeFilter(currentMessage) ?? InferDeviceCodeFilter(previousDeviceLogQuestion);
        var payload = BuildPayload(previousDeviceLogQuestion, currentMessage!, levelFilter, scopeFilter);
        var targetIntent = levelFilter is null && !ContainsLevelSignal(previousDeviceLogQuestion)
            ? DeviceLogLatestIntent
            : DeviceLogByLevelIntent;

        var existingDeviceLogIntent = intents.FirstOrDefault(intent =>
            intent.Intent.StartsWith("Analysis.DeviceLog.", StringComparison.OrdinalIgnoreCase));
        if (existingDeviceLogIntent is not null)
        {
            existingDeviceLogIntent.Intent = targetIntent;
            existingDeviceLogIntent.Query = payload;
            existingDeviceLogIntent.Confidence = Math.Max(existingDeviceLogIntent.Confidence, 0.92);
            existingDeviceLogIntent.RoutingNote = AppendReason(existingDeviceLogIntent.RoutingNote);
            return;
        }

        intents.Add(new IntentResult
        {
            Intent = targetIntent,
            Confidence = 0.92,
            Query = payload,
            RoutingNote = "deterministic DeviceLog follow-up rewrite; inherited previous readonly query scope"
        });
    }

    private static string BuildPayload(
        string previousDeviceLogQuestion,
        string currentMessage,
        SemanticFallbackFilter? levelFilter,
        SemanticFallbackFilter? scopeFilter)
    {
        var filters = new[] { levelFilter, scopeFilter }
            .OfType<SemanticFallbackFilter>()
            .ToArray();
        var payload = new SemanticFallbackPayload(
            $"{previousDeviceLogQuestion}。追问：{currentMessage}",
            filters,
            new SemanticFallbackSort("occurredAt", "desc"),
            20);
        return JsonSerializer.Serialize(payload, JsonSerializerOptions.Web);
    }

    private static bool IsDeviceLogFollowUp(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return false;
        }

        var normalized = message.Trim();
        if (normalized.Length > 80)
        {
            return false;
        }

        var hasFollowUpMarker = ContainsAny(normalized, "呢", "也", "那", "是否", "有没有", "还有", "刚才", "刚刚", "上一轮", "上次", "前面");
        var hasLogOrLevelSignal = ContainsAny(normalized, "日志", "log", "错误", "异常", "警告", "告警", "报警", "正常", "信息") ||
                                  ContainsEnglishTerm(normalized, "error") ||
                                  ContainsEnglishTerm(normalized, "warn") ||
                                  ContainsEnglishTerm(normalized, "warning") ||
                                  ContainsEnglishTerm(normalized, "info");
        var hasScopeSignal = DeviceCodeRegex().IsMatch(normalized) ||
                             ContainsAny(normalized, "设备", "工序", "机台");

        return hasFollowUpMarker && (hasLogOrLevelSignal || hasScopeSignal);
    }

    private static bool IsDeviceLogSeedQuestion(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return false;
        }

        return ContainsAny(message, "日志", "log", "告警", "报警", "警告", "错误", "异常") ||
               ContainsEnglishTerm(message, "log") ||
               ContainsEnglishTerm(message, "error") ||
               ContainsEnglishTerm(message, "warn") ||
               ContainsEnglishTerm(message, "warning");
    }

    private static bool ContainsLevelSignal(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return false;
        }

        return InferCurrentLevelFilter(message) is not null;
    }

    private static SemanticFallbackFilter? InferCurrentLevelFilter(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return null;
        }

        var asksError = ContainsAny(message, "错误", "故障", "异常") || ContainsEnglishTerm(message, "error");
        var asksWarning = ContainsAny(message, "警告", "报警", "告警") ||
                          ContainsEnglishTerm(message, "warn") ||
                          ContainsEnglishTerm(message, "warning");
        var asksInfo = ContainsAny(message, "信息", "正常") || ContainsEnglishTerm(message, "info");

        if (asksError && asksWarning)
        {
            return new SemanticFallbackFilter("level", "in", "ERROR,WARN");
        }

        if (asksWarning)
        {
            return new SemanticFallbackFilter("level", "eq", "WARN");
        }

        if (asksError)
        {
            return new SemanticFallbackFilter("level", "eq", "ERROR");
        }

        return asksInfo
            ? new SemanticFallbackFilter("level", "eq", "INFO")
            : null;
    }

    private static SemanticFallbackFilter? InferDeviceCodeFilter(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return null;
        }

        var match = DeviceCodeRegex().Match(message);
        if (!match.Success)
        {
            return null;
        }

        return new SemanticFallbackFilter("deviceCode", "eq", match.Groups["code"].Value);
    }

    private static string AppendReason(string? existing)
    {
        const string reason = "deterministic DeviceLog follow-up rewrite; inherited previous readonly query scope";
        return string.IsNullOrWhiteSpace(existing)
            ? reason
            : $"{existing}; {reason}";
    }

    private static bool ContainsAny(string value, params string[] terms)
    {
        return terms.Any(term => value.Contains(term, StringComparison.OrdinalIgnoreCase));
    }

    private static bool ContainsEnglishTerm(string value, string term)
    {
        return Regex.IsMatch(
            value,
            $@"(?i)(^|[^\p{{L}}\p{{N}}_]){Regex.Escape(term)}([^\p{{L}}\p{{N}}_]|$)",
            RegexOptions.CultureInvariant);
    }

    [GeneratedRegex(@"(?<![A-Za-z0-9_-])(?<code>[A-Za-z0-9]+(?:[-_][A-Za-z0-9]+)+)(?![A-Za-z0-9_-])", RegexOptions.CultureInvariant)]
    private static partial Regex DeviceCodeRegex();

    private sealed record SemanticFallbackPayload(
        string QueryText,
        IReadOnlyList<SemanticFallbackFilter> Filters,
        SemanticFallbackSort Sort,
        int Limit);

    private sealed record SemanticFallbackFilter(string Field, string Operator, string Value);

    private sealed record SemanticFallbackSort(string Field, string Direction);
}
