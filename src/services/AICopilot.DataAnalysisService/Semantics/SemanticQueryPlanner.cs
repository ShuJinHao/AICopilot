using System.Text.Json;
using System.Text.Json.Serialization;
using AICopilot.Services.Contracts;

namespace AICopilot.DataAnalysisService.Semantics;

public sealed class SemanticQueryPlanner(
    ISemanticIntentCatalog intentCatalog,
    ISemanticDefinitionCatalog definitionCatalog) : ISemanticQueryPlanner
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    public SemanticPlanningResult Plan(string intent, string? query)
    {
        if (!intentCatalog.TryGet(intent, out var descriptor))
        {
            return SemanticPlanningResult.Failure($"Unsupported semantic intent: {intent}");
        }

        var definition = definitionCatalog.Get(descriptor.Target);
        var payloadResult = ParsePayload(query);
        if (!payloadResult.IsSuccess)
        {
            return SemanticPlanningResult.Failure(payloadResult.ErrorMessage!);
        }

        var payload = payloadResult.Value!;
        var projectionFields = payload.Fields?.Where(static field => !string.IsNullOrWhiteSpace(field)).ToArray()
            ?? descriptor.DefaultFields.ToArray();

        if (projectionFields.Length == 0)
        {
            projectionFields = definition.GetDefaultProjection(descriptor.Kind).Fields.ToArray();
        }

        foreach (var field in projectionFields)
        {
            if (!definition.IsProjectionFieldAllowed(field))
            {
                return SemanticPlanningResult.Failure(
                    $"Field '{field}' is not in the allowed projection whitelist for {descriptor.Target}.");
            }
        }

        var filters = new List<SemanticFilter>();
        foreach (var filter in payload.Filters ?? [])
        {
            if (string.IsNullOrWhiteSpace(filter.Field))
            {
                return SemanticPlanningResult.Failure("Filter is missing field.");
            }

            if (!definition.IsFilterFieldAllowed(filter.Field))
            {
                return SemanticPlanningResult.Failure(
                    $"Filter field '{filter.Field}' is not in the allowed whitelist for {descriptor.Target}.");
            }

            if (string.IsNullOrWhiteSpace(filter.Value))
            {
                return SemanticPlanningResult.Failure($"Filter field '{filter.Field}' is missing value.");
            }

            if (!TryParseOperator(filter.Operator, out var filterOperator))
            {
                return SemanticPlanningResult.Failure(
                    $"Filter field '{filter.Field}' uses unsupported operator '{filter.Operator}'.");
            }

            filters.Add(new SemanticFilter(filter.Field, filterOperator, filter.Value));
        }

        if (descriptor.RequiredAllFilterFields?.Any() == true)
        {
            foreach (var field in descriptor.RequiredAllFilterFields)
            {
                if (!filters.Any(filter => field.Equals(filter.Field, StringComparison.OrdinalIgnoreCase)))
                {
                    return SemanticPlanningResult.Failure($"Intent {descriptor.Intent} requires filter field '{field}'.");
                }
            }
        }

        if (descriptor.RequiredAnyFilterFields?.Any() == true &&
            !filters.Any(filter => descriptor.RequiredAnyFilterFields.Contains(filter.Field, StringComparer.OrdinalIgnoreCase)))
        {
            return SemanticPlanningResult.Failure(
                $"Intent {descriptor.Intent} requires at least one of these filter fields: {string.Join(", ", descriptor.RequiredAnyFilterFields)}.");
        }

        SemanticTimeRange? timeRange = null;
        if (payload.TimeRange != null)
        {
            var field = string.IsNullOrWhiteSpace(payload.TimeRange.Field)
                ? "occurredAt"
                : payload.TimeRange.Field;

            if (!definition.IsSortFieldAllowed(field) && !definition.IsProjectionFieldAllowed(field))
            {
                return SemanticPlanningResult.Failure(
                    $"Time range field '{field}' is not in the allowed whitelist for {descriptor.Target}.");
            }

            timeRange = new SemanticTimeRange(field, payload.TimeRange.Start, payload.TimeRange.End);
        }

        if (descriptor.RequiresTimeRange && timeRange == null)
        {
            return SemanticPlanningResult.Failure($"Intent {descriptor.Intent} requires timeRange.");
        }

        if (descriptor.RequiresTimeRange && timeRange is { Start: null, End: null })
        {
            return SemanticPlanningResult.Failure($"Intent {descriptor.Intent} requires timeRange.start or timeRange.end.");
        }

        SemanticSort? sort = null;
        if (payload.Sort != null)
        {
            if (string.IsNullOrWhiteSpace(payload.Sort.Field))
            {
                return SemanticPlanningResult.Failure("Sort is missing field.");
            }

            if (!definition.IsSortFieldAllowed(payload.Sort.Field))
            {
                return SemanticPlanningResult.Failure(
                    $"Sort field '{payload.Sort.Field}' is not in the allowed whitelist for {descriptor.Target}.");
            }

            if (!TryParseDirection(payload.Sort.Direction, out var direction))
            {
                return SemanticPlanningResult.Failure(
                    $"Sort field '{payload.Sort.Field}' uses unsupported direction '{payload.Sort.Direction}'.");
            }

            sort = new SemanticSort(payload.Sort.Field, direction);
        }
        else if (!string.IsNullOrWhiteSpace(descriptor.DefaultSortField))
        {
            sort = new SemanticSort(descriptor.DefaultSortField, descriptor.DefaultSortDirection);
        }

        var requestedLimit = payload.Limit ?? descriptor.DefaultLimit;
        var limit = Math.Clamp(requestedLimit, 1, definition.MaxLimit);

        var plan = new SemanticQueryPlan(
            descriptor.Intent,
            descriptor.Target,
            descriptor.Kind,
            payload.QueryText,
            new SemanticProjection(projectionFields),
            filters,
            timeRange,
            sort,
            limit);

        return SemanticPlanningResult.Success(plan);
    }

    private static PayloadParseResult ParsePayload(string? rawQuery)
    {
        if (string.IsNullOrWhiteSpace(rawQuery))
        {
            return PayloadParseResult.Success(new SemanticIntentPayload(null, null, null, null, null, null));
        }

        var trimmed = rawQuery.Trim();
        if (!trimmed.StartsWith('{'))
        {
            return PayloadParseResult.Success(new SemanticIntentPayload(null, null, null, null, null, trimmed));
        }

        try
        {
            var payload = JsonSerializer.Deserialize<SemanticIntentPayload>(trimmed, JsonOptions);
            return payload == null
                ? PayloadParseResult.Failure("Semantic query payload is empty.")
                : PayloadParseResult.Success(payload);
        }
        catch (JsonException ex)
        {
            return PayloadParseResult.Failure($"Semantic query payload is not valid JSON: {ex.Message}");
        }
    }

    private static bool TryParseOperator(string? value, out SemanticFilterOperator filterOperator)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            filterOperator = SemanticFilterOperator.Equal;
            return true;
        }

        return value.Trim().ToLowerInvariant() switch
        {
            "eq" or "equal" => Match(SemanticFilterOperator.Equal, out filterOperator),
            "contains" => Match(SemanticFilterOperator.Contains, out filterOperator),
            "gte" or "greaterorequal" => Match(SemanticFilterOperator.GreaterOrEqual, out filterOperator),
            "lte" or "lessorequal" => Match(SemanticFilterOperator.LessOrEqual, out filterOperator),
            "in" => Match(SemanticFilterOperator.In, out filterOperator),
            _ => Match(default, out filterOperator, false)
        };
    }

    private static bool TryParseDirection(string? value, out SemanticSortDirection direction)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            direction = SemanticSortDirection.Asc;
            return true;
        }

        return value.Trim().ToLowerInvariant() switch
        {
            "asc" => Match(SemanticSortDirection.Asc, out direction),
            "desc" => Match(SemanticSortDirection.Desc, out direction),
            _ => Match(default, out direction, false)
        };
    }

    private static bool Match<T>(T value, out T parsedValue, bool isSuccess = true)
    {
        parsedValue = value;
        return isSuccess;
    }

    private sealed record PayloadParseResult(bool IsSuccess, SemanticIntentPayload? Value, string? ErrorMessage)
    {
        public static PayloadParseResult Success(SemanticIntentPayload payload)
        {
            return new PayloadParseResult(true, payload, null);
        }

        public static PayloadParseResult Failure(string errorMessage)
        {
            return new PayloadParseResult(false, null, errorMessage);
        }
    }

    private sealed record SemanticIntentPayload(
        [property: JsonPropertyName("fields")] IReadOnlyList<string>? Fields,
        [property: JsonPropertyName("filters")] IReadOnlyList<SemanticIntentFilterPayload>? Filters,
        [property: JsonPropertyName("sort")] SemanticIntentSortPayload? Sort,
        [property: JsonPropertyName("timeRange")] SemanticIntentTimeRangePayload? TimeRange,
        [property: JsonPropertyName("limit")] int? Limit,
        [property: JsonPropertyName("queryText")] string? QueryText);

    private sealed record SemanticIntentFilterPayload(
        [property: JsonPropertyName("field")] string? Field,
        [property: JsonPropertyName("operator")] string? Operator,
        [property: JsonPropertyName("value")] string? Value);

    private sealed record SemanticIntentSortPayload(
        [property: JsonPropertyName("field")] string? Field,
        [property: JsonPropertyName("direction")] string? Direction);

    private sealed record SemanticIntentTimeRangePayload(
        [property: JsonPropertyName("field")] string? Field,
        [property: JsonPropertyName("start")] DateTimeOffset? Start,
        [property: JsonPropertyName("end")] DateTimeOffset? End);
}
