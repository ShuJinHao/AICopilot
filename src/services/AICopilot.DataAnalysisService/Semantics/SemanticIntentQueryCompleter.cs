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

    public static SemanticIntentQueryCompletion Complete(string intent, string? query)
    {
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
        var inferredLevel = payload.GetFilterValue("level") ?? InferLevel(queryText);
        var asksLatest = ContainsAny(queryText, "最新", "最近") ||
                         ContainsEnglishTerm(queryText, "latest") ||
                         ContainsEnglishTerm(queryText, "recent");

        var completedIntent = intent;
        if (!string.IsNullOrWhiteSpace(inferredLevel))
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
            (completedIntent.Equals(DeviceLogByLevelIntent, StringComparison.OrdinalIgnoreCase) &&
             !payload.HasFilter("level") &&
             !string.IsNullOrWhiteSpace(inferredLevel));

        if (!shouldRewrite)
        {
            return new SemanticIntentQueryCompletion(intent, query, WasCompleted: false);
        }

        if (completedIntent.Equals(DeviceLogByLevelIntent, StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(inferredLevel))
        {
            payload.UpsertFilter("level", inferredLevel);
        }

        return new SemanticIntentQueryCompletion(
            completedIntent,
            payload.ToJson(),
            WasCompleted: true);
    }

    private static string? InferLevel(string? queryText)
    {
        if (string.IsNullOrWhiteSpace(queryText))
        {
            return null;
        }

        if (ContainsAny(queryText, "错误", "故障", "异常") || ContainsEnglishTerm(queryText, "error"))
        {
            return "Error";
        }

        if (ContainsAny(queryText, "警告", "报警", "告警") ||
            ContainsEnglishTerm(queryText, "warn") ||
            ContainsEnglishTerm(queryText, "warning"))
        {
            return "Warn";
        }

        if (ContainsAny(queryText, "信息") || ContainsEnglishTerm(queryText, "info"))
        {
            return "Info";
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

        public void UpsertFilter(string field, string value)
        {
            var filters = GetFilters(createIfMissing: true)!;
            foreach (var filter in filters.OfType<JsonObject>())
            {
                var filterField = GetString(filter, "field");
                if (field.Equals(filterField, StringComparison.OrdinalIgnoreCase))
                {
                    filter["value"] = value;
                    if (string.IsNullOrWhiteSpace(GetString(filter, "operator")))
                    {
                        filter["operator"] = "eq";
                    }

                    return;
                }
            }

            filters.Add(new JsonObject
            {
                ["field"] = field,
                ["operator"] = "eq",
                ["value"] = value
            });
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
}
