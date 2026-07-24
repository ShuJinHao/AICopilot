using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace AICopilot.DataAnalysisService.Semantics;

internal sealed record SemanticIntentQueryCompletion(
    string Intent,
    string? Query,
    bool WasCompleted);

internal static partial class SemanticIntentQueryCompleter
{
    private const string DeviceLogLatestIntent = "Analysis.DeviceLog.Latest";
    private const string DeviceLogByLevelIntent = "Analysis.DeviceLog.ByLevel";
    private const string ProductionIntentPrefix = "Analysis.ProductionData.";
    private const string EqualOperator = "eq";
    private const string InOperator = "in";

    public static SemanticIntentQueryCompletion Complete(string intent, string? query)
    {
        if (intent.StartsWith(ProductionIntentPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return CompleteProductionData(intent, query);
        }

        if (!intent.StartsWith("Analysis.DeviceLog.", StringComparison.OrdinalIgnoreCase))
        {
            return new SemanticIntentQueryCompletion(intent, query, WasCompleted: false);
        }

        var payload = MutableSemanticPayload.Parse(query);
        if (!payload.CanRewrite)
        {
            return new SemanticIntentQueryCompletion(intent, query, WasCompleted: false);
        }

        var queryText = payload.QueryText;
        var existingLevel = payload.GetFilterValue("level");
        var existingLevelOperator = payload.GetFilterOperator("level");
        var normalizedExistingLevel = NormalizeLevelFilter(existingLevel);
        var inferredLevel = normalizedExistingLevel ?? (existingLevel is null ? InferLevelFilter(queryText) : null);
        var inferredTimeRange = payload.HasTimeRange ? null : InferRelativeTimeRange(queryText);
        var asksLatest = ContainsAny(queryText, "最新", "最近") ||
                         ContainsEnglishTerm(queryText, "latest") ||
                         ContainsEnglishTerm(queryText, "recent");

        var completedIntent = intent;
        if (inferredLevel is not null)
        {
            completedIntent = DeviceLogByLevelIntent;
        }
        else if (intent.Equals(DeviceLogByLevelIntent, StringComparison.OrdinalIgnoreCase) || asksLatest)
        {
            completedIntent = DeviceLogLatestIntent;
        }

        var shouldRewrite =
            !completedIntent.Equals(intent, StringComparison.OrdinalIgnoreCase) ||
            !payload.IsJson ||
            (normalizedExistingLevel is not null &&
             (!string.Equals(existingLevel, normalizedExistingLevel.Value, StringComparison.Ordinal) ||
             !string.Equals(
                  string.IsNullOrWhiteSpace(existingLevelOperator) ? EqualOperator : existingLevelOperator,
                  normalizedExistingLevel.Operator,
                  StringComparison.OrdinalIgnoreCase))) ||
            inferredTimeRange is not null ||
            (completedIntent.Equals(DeviceLogByLevelIntent, StringComparison.OrdinalIgnoreCase) &&
             !payload.HasFilter("level") &&
             inferredLevel is not null);

        if (!shouldRewrite)
        {
            return new SemanticIntentQueryCompletion(intent, query, WasCompleted: false);
        }

        if (completedIntent.Equals(DeviceLogByLevelIntent, StringComparison.OrdinalIgnoreCase) &&
            inferredLevel is not null)
        {
            payload.UpsertFilter("level", inferredLevel.Value, inferredLevel.Operator);
        }

        if (inferredTimeRange is not null)
        {
            payload.SetTimeRange("occurredAt", inferredTimeRange.Start, inferredTimeRange.End);
        }

        return new SemanticIntentQueryCompletion(
            completedIntent,
            payload.ToJson(),
            WasCompleted: true);
    }

    private static SemanticIntentQueryCompletion CompleteProductionData(string intent, string? query)
    {
        var payload = MutableSemanticPayload.Parse(query);
        if (!payload.CanRewrite)
        {
            return new SemanticIntentQueryCompletion(intent, query, WasCompleted: false);
        }

        var queryText = payload.QueryText;
        var inferredScope = InferProductionScope(queryText);
        var existingTypeKey = payload.GetFilterValue("typeKey");
        var normalizedTypeKey = NormalizeProductionTypeKey(existingTypeKey);
        var inferredPreset = payload.HasFilter("preset")
            ? null
            : InferProductionPreset(queryText);
        var shouldRewrite = !payload.IsJson;

        if (normalizedTypeKey is not null &&
            !string.Equals(existingTypeKey, normalizedTypeKey, StringComparison.Ordinal))
        {
            payload.UpsertFilter("typeKey", normalizedTypeKey);
            shouldRewrite = true;
        }
        else if (existingTypeKey is null && inferredScope is not null)
        {
            payload.UpsertFilter("typeKey", inferredScope.TypeKey);
            shouldRewrite = true;
        }

        if (!payload.HasFilter("plcName") &&
            !string.IsNullOrWhiteSpace(inferredScope?.PlcName))
        {
            payload.UpsertFilter("plcName", inferredScope.PlcName);
            shouldRewrite = true;
        }

        if (inferredPreset is not null)
        {
            payload.UpsertFilter("preset", inferredPreset);
            shouldRewrite = true;
        }

        return shouldRewrite
            ? new SemanticIntentQueryCompletion(intent, payload.ToJson(), WasCompleted: true)
            : new SemanticIntentQueryCompletion(intent, query, WasCompleted: false);
    }

    private static ProductionScope? InferProductionScope(string? queryText)
    {
        if (string.IsNullOrWhiteSpace(queryText))
        {
            return null;
        }

        var match = ProductionPlcNameRegex().Match(queryText);
        if (match.Success)
        {
            var processName = match.Groups["process"].Value;
            var suffix = NormalizeDigits(match.Groups["suffix"].Value);
            return new ProductionScope(
                processName == "正极模切" ? "cp" : "ap",
                processName + suffix);
        }

        if (queryText.Contains("正极模切", StringComparison.Ordinal))
        {
            return new ProductionScope("cp", null);
        }

        if (queryText.Contains("负极模切", StringComparison.Ordinal))
        {
            return new ProductionScope("ap", null);
        }

        return null;
    }

    private static string? NormalizeProductionTypeKey(string? typeKey)
    {
        if (string.IsNullOrWhiteSpace(typeKey))
        {
            return null;
        }

        return typeKey.Trim() switch
        {
            var value when value.Equals("cp", StringComparison.OrdinalIgnoreCase) => "cp",
            var value when value.Equals("ap", StringComparison.OrdinalIgnoreCase) => "ap",
            "正极模切" => "cp",
            "负极模切" => "ap",
            _ => null
        };
    }

    private static string? InferProductionPreset(string? queryText)
    {
        if (string.IsNullOrWhiteSpace(queryText))
        {
            return null;
        }

        if (ContainsAny(queryText, "今天", "今日") || ContainsEnglishTerm(queryText, "today"))
        {
            return "today";
        }

        if (ContainsAny(queryText, "昨天", "昨日") || ContainsEnglishTerm(queryText, "yesterday"))
        {
            return "yesterday";
        }

        if (ContainsAny(queryText, "最近24小时", "近24小时", "过去24小时", "last 24h", "last_24h"))
        {
            return "last_24h";
        }

        return null;
    }

    private static string NormalizeDigits(string value)
    {
        return string.Concat(value.Select(character => character is >= '０' and <= '９'
            ? (char)('0' + character - '０')
            : character));
    }

    private static LevelFilter? InferLevelFilter(string? queryText)
    {
        if (string.IsNullOrWhiteSpace(queryText))
        {
            return null;
        }

        var asksError = ContainsAny(queryText, "错误", "故障", "异常") || ContainsEnglishTerm(queryText, "error");
        var asksWarning = ContainsAny(queryText, "警告", "报警", "告警") ||
                          ContainsEnglishTerm(queryText, "warn") ||
                          ContainsEnglishTerm(queryText, "warning");
        var asksAnalysis = ContainsAny(queryText, "分析", "归因", "排查", "诊断");

        if ((asksError && asksWarning) ||
            (asksAnalysis && asksError) ||
            ContainsAny(queryText, "错误警告", "异常日志", "异常告警", "异常报警"))
        {
            return new LevelFilter(InOperator, "ERROR,WARN");
        }

        if (asksError)
        {
            return new LevelFilter(EqualOperator, "ERROR");
        }

        if (asksWarning)
        {
            return new LevelFilter(EqualOperator, "WARN");
        }

        if (AsksInfoLevel(queryText))
        {
            return new LevelFilter(EqualOperator, "INFO");
        }

        return null;
    }

    private static RelativeTimeRange? InferRelativeTimeRange(string? queryText)
    {
        if (string.IsNullOrWhiteSpace(queryText))
        {
            return null;
        }

        var duration = TryParseRelativeDuration(queryText);
        if (duration is null)
        {
            return null;
        }

        var end = DateTimeOffset.UtcNow;
        return new RelativeTimeRange(end.Subtract(duration.Value), end);
    }

    private static TimeSpan? TryParseRelativeDuration(string queryText)
    {
        var match = RelativeTimeRangeRegex().Match(queryText);
        if (!match.Success)
        {
            return null;
        }

        var amount = ParseRelativeAmount(match.Groups["amount"].Value);
        if (amount <= 0)
        {
            return null;
        }

        var unit = match.Groups["unit"].Value;
        return unit switch
        {
            "天" or "日" or "d" or "D" => TimeSpan.FromDays(amount),
            "小时" or "时" or "h" or "H" => TimeSpan.FromHours(amount),
            _ => null
        };
    }

    private static int ParseRelativeAmount(string value)
    {
        if (int.TryParse(value, out var numeric))
        {
            return numeric;
        }

        return value switch
        {
            "一" => 1,
            "二" or "两" => 2,
            "三" => 3,
            "四" => 4,
            "五" => 5,
            "六" => 6,
            "七" => 7,
            "八" => 8,
            "九" => 9,
            "十" => 10,
            _ => 0
        };
    }

    private static LevelFilter? NormalizeLevelFilter(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalizedLevels = value
            .Split([',', '|', ';', '，', '；', '/', '、'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(NormalizeSingleLevelValue)
            .Where(level => level is not null)
            .Select(level => level!)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        return normalizedLevels.Length switch
        {
            0 => null,
            1 => new LevelFilter(EqualOperator, normalizedLevels[0]),
            _ => new LevelFilter(InOperator, string.Join(',', normalizedLevels))
        };
    }

    private static string? NormalizeSingleLevelValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim();
        if (string.Equals(normalized, "ERROR", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(normalized, "ERR", StringComparison.OrdinalIgnoreCase) ||
            normalized is "错误" or "故障" or "异常")
        {
            return "ERROR";
        }

        if (string.Equals(normalized, "WARN", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(normalized, "WARNING", StringComparison.OrdinalIgnoreCase) ||
            normalized is "警告" or "报警" or "告警")
        {
            return "WARN";
        }

        if (string.Equals(normalized, "INFO", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(normalized, "INFORMATION", StringComparison.OrdinalIgnoreCase) ||
            normalized is "信息" or "正常")
        {
            return "INFO";
        }

        return null;
    }

    private static bool ContainsAny(string? value, params string[] terms)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return terms.Any(term => value.Contains(term, StringComparison.OrdinalIgnoreCase));
    }

    private static bool AsksInfoLevel(string? value)
    {
        return ContainsEnglishTerm(value, "info") ||
               ContainsAny(
                   value,
                   "INFO日志",
                   "INFO级别",
                   "级别INFO",
                   "信息日志",
                   "信息级别",
                   "日志级别为信息",
                   "日志级别是信息",
                   "正常日志",
                   "正常信息");
    }

    private static bool ContainsEnglishTerm(string? value, string term)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return Regex.IsMatch(
            value,
            $@"(?i)(^|[^\p{{L}}\p{{N}}_]){Regex.Escape(term)}([^\p{{L}}\p{{N}}_]|$)",
            RegexOptions.CultureInvariant);
    }

    private sealed class MutableSemanticPayload
    {
        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
        private readonly JsonObject root;

        private MutableSemanticPayload(JsonObject root, bool isJson, bool canRewrite = true)
        {
            this.root = root;
            IsJson = isJson;
            CanRewrite = canRewrite;
        }

        public bool IsJson { get; }

        public bool CanRewrite { get; }

        public string? QueryText => root.TryGetPropertyValue("queryText", out var value)
            ? value?.GetValue<string>()
            : null;

        public bool HasTimeRange => root.TryGetPropertyValue("timeRange", out var value) && value is JsonObject;

        public static MutableSemanticPayload Parse(string? rawQuery)
        {
            if (string.IsNullOrWhiteSpace(rawQuery))
            {
                return new MutableSemanticPayload(new JsonObject(), isJson: false);
            }

            var trimmed = rawQuery.Trim();
            if (!trimmed.StartsWith('{'))
            {
                return new MutableSemanticPayload(
                    new JsonObject { ["queryText"] = trimmed },
                    isJson: false);
            }

            try
            {
                var root = JsonNode.Parse(trimmed) as JsonObject;
                return root is null
                    ? new MutableSemanticPayload(new JsonObject { ["queryText"] = trimmed }, isJson: false)
                    : new MutableSemanticPayload(root, isJson: true);
            }
            catch (JsonException)
            {
                return new MutableSemanticPayload(new JsonObject(), isJson: true, canRewrite: false);
            }
        }

        public bool HasFilter(string field)
        {
            return GetFilterValue(field) is not null;
        }

        public bool HasAnyFilter(params string[] fields)
        {
            return fields.Any(HasFilter);
        }

        public string? GetFilterValue(string field)
        {
            var filters = GetFilters(createIfMissing: false);
            if (filters is null)
            {
                return null;
            }

            foreach (var filter in filters.OfType<JsonObject>())
            {
                var filterField = GetString(filter, "field");
                if (field.Equals(filterField, StringComparison.OrdinalIgnoreCase))
                {
                    return GetString(filter, "value");
                }
            }

            return null;
        }

        public string? GetFilterOperator(string field)
        {
            var filters = GetFilters(createIfMissing: false);
            if (filters is null)
            {
                return null;
            }

            foreach (var filter in filters.OfType<JsonObject>())
            {
                var filterField = GetString(filter, "field");
                if (field.Equals(filterField, StringComparison.OrdinalIgnoreCase))
                {
                    return GetString(filter, "operator");
                }
            }

            return null;
        }

        public void UpsertFilter(string field, string value, string filterOperator = EqualOperator)
        {
            var filters = GetFilters(createIfMissing: true)!;
            foreach (var filter in filters.OfType<JsonObject>())
            {
                var filterField = GetString(filter, "field");
                if (field.Equals(filterField, StringComparison.OrdinalIgnoreCase))
                {
                    filter["value"] = value;
                    filter["operator"] = filterOperator;

                    return;
                }
            }

            filters.Add(new JsonObject
            {
                ["field"] = field,
                ["operator"] = filterOperator,
                ["value"] = value
            });
        }

        public void SetTimeRange(string field, DateTimeOffset start, DateTimeOffset end)
        {
            root["timeRange"] = new JsonObject
            {
                ["field"] = field,
                ["start"] = start.ToUniversalTime().ToString("O"),
                ["end"] = end.ToUniversalTime().ToString("O")
            };
        }

        public string ToJson()
        {
            return root.ToJsonString(JsonOptions);
        }

        private JsonArray? GetFilters(bool createIfMissing)
        {
            if (root.TryGetPropertyValue("filters", out var value) && value is JsonArray existing)
            {
                return existing;
            }

            if (!createIfMissing)
            {
                return null;
            }

            var filters = new JsonArray();
            root["filters"] = filters;
            return filters;
        }

        private static string? GetString(JsonObject source, string propertyName)
        {
            return source.TryGetPropertyValue(propertyName, out var value)
                ? value?.GetValue<string>()
                : null;
        }
    }

    private sealed record LevelFilter(string Operator, string Value);

    private sealed record RelativeTimeRange(DateTimeOffset Start, DateTimeOffset End);

    private sealed record ProductionScope(string TypeKey, string? PlcName);

    [GeneratedRegex(@"(?:最近|近|过去)\s*(?<amount>\d+|一|二|两|三|四|五|六|七|八|九|十)\s*(?<unit>天|日|小时|时|d|D|h|H)", RegexOptions.CultureInvariant)]
    private static partial Regex RelativeTimeRangeRegex();

    [GeneratedRegex(@"(?<process>正极模切|负极模切)\s*(?<suffix>[0-9０-９]{1,4})", RegexOptions.CultureInvariant)]
    private static partial Regex ProductionPlcNameRegex();

}
