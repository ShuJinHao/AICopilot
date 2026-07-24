using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using AICopilot.SharedKernel.Result;

namespace AICopilot.Services.Contracts;

public static class CloudAiReadProblemCodes
{
    public const string NotConfigured = "cloud_ai_read_not_configured";
    public const string RequestBlocked = "cloud_ai_read_request_blocked";
    public const string Unauthorized = "cloud_ai_read_unauthorized";
    public const string Forbidden = "cloud_ai_read_forbidden";
    public const string NotFound = "cloud_ai_read_not_found";
    public const string InvalidRequest = "cloud_ai_read_invalid_request";
    public const string RateLimited = "cloud_ai_read_rate_limited";
    public const string Unavailable = "cloud_ai_read_unavailable";
    public const string MissingRequiredParameter = "cloud_ai_read_missing_required_parameter";
}

public static class CloudAiReadRowLimitPolicy
{
    public const int MinimumRows = 1;
    public const int MaxRows = 100;

    public static bool IsWithinBounds(int rows)
    {
        return rows is >= MinimumRows and <= MaxRows;
    }

    public static int Normalize(int rows)
    {
        return Math.Clamp(rows, MinimumRows, MaxRows);
    }
}

public sealed class CloudAiReadException : InvalidOperationException
{
    public CloudAiReadException(string code, string message, HttpStatusCode? statusCode = null)
        : base(message)
    {
        Code = code;
        StatusCode = statusCode;
    }

    public string Code { get; }

    public HttpStatusCode? StatusCode { get; }
}

public sealed class CloudAiReadOptions
{
    public const string SectionName = "CloudAiRead";

    public bool Enabled { get; init; }

    public string BaseUrl { get; init; } = string.Empty;

    public string ServiceAccountToken { get; init; } = string.Empty;

    public int TimeoutSeconds { get; init; } = 10;

    public bool IsConfigured() => Enabled;

    public void EnsureValid()
    {
        if (!Enabled)
        {
            return;
        }

        if (!Uri.TryCreate(BaseUrl, UriKind.Absolute, out var baseUri) ||
            (baseUri.Scheme != Uri.UriSchemeHttp && baseUri.Scheme != Uri.UriSchemeHttps))
        {
            throw new InvalidOperationException("CloudAiRead:BaseUrl must be an absolute HTTP/HTTPS URL when enabled.");
        }

        if (string.IsNullOrWhiteSpace(ServiceAccountToken))
        {
            throw new InvalidOperationException("CloudAiRead:ServiceAccountToken is required when enabled.");
        }

        if (TimeoutSeconds is < 1 or > 30)
        {
            throw new InvalidOperationException("CloudAiRead:TimeoutSeconds must be between 1 and 30.");
        }

    }
}

public sealed record CloudAiReadRequestDecision(bool IsAllowed, string? Reason)
{
    public static CloudAiReadRequestDecision Allow { get; } = new(true, null);

    public static CloudAiReadRequestDecision Block(string reason) => new(false, reason);
}

public static class CloudAiReadEndpointPolicy
{
    private static readonly string[] AllowedGetPaths =
    [
        "/api/v1/ai/read/devices",
        "/api/v1/ai/read/processes",
        "/api/v1/ai/read/client-releases",
        "/api/v1/ai/read/device-client-states",
        "/api/v1/ai/read/capacity/summary",
        "/api/v1/ai/read/capacity/hourly",
        "/api/v1/ai/read/production-records",
        "/api/v1/ai/read/device-logs"
    ];

    public static CloudAiReadRequestDecision Evaluate(
        HttpMethod method,
        string path)
    {
        if (!TryNormalizePath(path, out var normalizedPath, out var error))
        {
            return CloudAiReadRequestDecision.Block(error);
        }

        if (method == HttpMethod.Get)
        {
            return IsAllowedGetPath(normalizedPath)
                ? CloudAiReadRequestDecision.Allow
                : CloudAiReadRequestDecision.Block("Cloud AiRead GET path is not in the fixed allowlist.");
        }

        return CloudAiReadRequestDecision.Block("Cloud AiRead only allows fixed GET endpoints.");
    }

    public static bool IsSafeRouteSegment(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return value.All(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_');
    }

    private static bool IsAllowedGetPath(string normalizedPath)
    {
        return AllowedGetPaths.Contains(normalizedPath, StringComparer.OrdinalIgnoreCase) ||
               normalizedPath.StartsWith("/api/v1/ai/identity/", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryNormalizePath(
        string rawPath,
        out string normalizedPath,
        out string error)
    {
        normalizedPath = string.Empty;
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(rawPath))
        {
            error = "Cloud AiRead path is required.";
            return false;
        }

        var candidate = rawPath.Trim();
        if (candidate[0] != '/' &&
            Uri.TryCreate(candidate, UriKind.Absolute, out _))
        {
            error = "Cloud AiRead path must be relative to the configured Cloud base URL.";
            return false;
        }

        var path = candidate.Split('?', 2)[0].Trim();
        if (!path.StartsWith('/'))
        {
            path = "/" + path;
        }

        if (path.Contains("//", StringComparison.Ordinal) ||
            path.Contains("..", StringComparison.Ordinal))
        {
            error = "Cloud AiRead path must not contain path traversal or duplicate separators.";
            return false;
        }

        normalizedPath = path.TrimEnd('/');
        return true;
    }
}

public static class CloudAiReadSemanticSupport
{
    public static bool IsSupported(SemanticQueryTarget target)
    {
        return target is SemanticQueryTarget.Device
            or SemanticQueryTarget.DeviceLog
            or SemanticQueryTarget.Capacity
            or SemanticQueryTarget.ProductionData
            or SemanticQueryTarget.Process
            or SemanticQueryTarget.ClientRelease;
    }
}

public enum CloudAiReadOperation
{
    Device = 1,
    Process = 2,
    ClientRelease = 3,
    DeviceClientState = 4,
    CapacitySummary = 5,
    CapacityHourly = 6,
    DeviceLog = 7,
    ProductionRecord = 8
}

public enum CloudAiReadFilterValueKind
{
    Token = 1,
    Guid = 2,
    Boolean = 3,
    LogLevel = 4,
    Date = 5,
    Preset = 6,
    HourlyPreset = 7,
    FieldMode = 8
}

public sealed record CloudAiReadFilterRule(
    string Field,
    CloudAiReadFilterValueKind ValueKind,
    IReadOnlyCollection<string> Operators);

public sealed record CloudAiReadOperationSchema(
    CloudAiReadOperation Operation,
    string EndpointPath,
    bool AllowsTimeRange,
    IReadOnlyCollection<CloudAiReadFilterRule> Filters);

public sealed record CloudAiReadIntentSchema(
    string IntentCode,
    CloudAiReadOperation Operation,
    IReadOnlyCollection<string> RequiredAllFilterFields,
    IReadOnlyCollection<string> RequiredAnyFilterFields,
    IReadOnlyCollection<string> RequiredTimeAlternativeFilterFields,
    bool AllowsTimeRange,
    bool RequiresTimeRange);

/// <summary>
/// Single semantic field/operator/value/scope authority shared by Plan sealing and
/// the production Cloud AiRead query-parameter builder.
/// </summary>
public static class CloudAiReadSemanticSchemaRegistry
{
    public const string ContractVersion = "cloud-ai-read-semantic-schema:v1";

    private static readonly Regex TokenPattern = new(
        "^[\\p{L}\\p{N}][\\p{L}\\p{N}._:/-]{0,79}$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly IReadOnlyDictionary<string, string> CanonicalLogLevels =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["INFO"] = "INFO",
            ["WARN"] = "WARN",
            ["ERROR"] = "ERROR"
        };

    private static readonly IReadOnlyDictionary<CloudAiReadOperation, CloudAiReadOperationSchema> OperationSchemas =
        new[]
        {
            Schema(CloudAiReadOperation.Device, "/api/v1/ai/read/devices",
                Rule("deviceId", CloudAiReadFilterValueKind.Guid),
                Rule("deviceCode"),
                Rule("processId", CloudAiReadFilterValueKind.Guid),
                KeywordRule("keyword"),
                KeywordRule("deviceName")),
            Schema(CloudAiReadOperation.Process, "/api/v1/ai/read/processes",
                Rule("processId", CloudAiReadFilterValueKind.Guid),
                KeywordRule("keyword"),
                KeywordRule("processCode"),
                KeywordRule("processName")),
            Schema(CloudAiReadOperation.ClientRelease, "/api/v1/ai/read/client-releases",
                Rule("channel"),
                Rule("targetRuntime"),
                Rule("status"),
                Rule("includeArchived", CloudAiReadFilterValueKind.Boolean)),
            Schema(CloudAiReadOperation.DeviceClientState, "/api/v1/ai/read/device-client-states",
                Rule("deviceId", CloudAiReadFilterValueKind.Guid),
                Rule("deviceCode"),
                Rule("clientCode"),
                Rule("processId", CloudAiReadFilterValueKind.Guid),
                KeywordRule("keyword"),
                KeywordRule("deviceName")),
            SchemaWithTimeRange(CloudAiReadOperation.CapacitySummary, "/api/v1/ai/read/capacity/summary",
                Rule("deviceId", CloudAiReadFilterValueKind.Guid),
                Rule("plcName"),
                Rule("shiftDate", CloudAiReadFilterValueKind.Date)),
            SchemaWithTimeRange(CloudAiReadOperation.CapacityHourly, "/api/v1/ai/read/capacity/hourly",
                Rule("deviceId", CloudAiReadFilterValueKind.Guid),
                Rule("date", CloudAiReadFilterValueKind.Date),
                Rule("shiftDate", CloudAiReadFilterValueKind.Date),
                Rule("preset", CloudAiReadFilterValueKind.HourlyPreset),
                Rule("plcName")),
            SchemaWithTimeRange(CloudAiReadOperation.DeviceLog, "/api/v1/ai/read/device-logs",
                Rule("deviceId", CloudAiReadFilterValueKind.Guid),
                Rule("preset", CloudAiReadFilterValueKind.Preset),
                Rule("level", CloudAiReadFilterValueKind.LogLevel),
                Rule("minLevel", CloudAiReadFilterValueKind.LogLevel),
                KeywordRule("keyword"),
                KeywordRule("message")),
            SchemaWithTimeRange(CloudAiReadOperation.ProductionRecord, "/api/v1/ai/read/production-records",
                Rule("typeKey"),
                Rule("processId", CloudAiReadFilterValueKind.Guid),
                Rule("deviceId", CloudAiReadFilterValueKind.Guid),
                Rule("plcCode"),
                Rule("plcName"),
                Rule("preset", CloudAiReadFilterValueKind.Preset),
                Rule("barcode"),
                Rule("result"),
                Rule("fieldMode", CloudAiReadFilterValueKind.FieldMode))
        }.ToDictionary(schema => schema.Operation);

    private static readonly IReadOnlyDictionary<string, CloudAiReadIntentSchema> IntentSchemas =
        new[]
        {
            Intent("Analysis.Device.List", CloudAiReadOperation.Device),
            Intent("Analysis.Device.Detail", CloudAiReadOperation.Device, requiredAny: ["deviceCode", "deviceId"]),
            Intent("Analysis.Device.Status", CloudAiReadOperation.DeviceClientState),
            Intent("Analysis.DeviceLog.Latest", CloudAiReadOperation.DeviceLog, requiredAny: ["deviceId"]),
            Intent("Analysis.DeviceLog.Range", CloudAiReadOperation.DeviceLog, requiredAny: ["deviceId"], allowsTimeRange: true, requiresTimeRange: true),
            Intent("Analysis.DeviceLog.ByLevel", CloudAiReadOperation.DeviceLog, requiredAll: ["level"], requiredAny: ["deviceId"], requiredTimeAlternatives: ["preset"], allowsTimeRange: true),
            Intent("Analysis.Capacity.Range", CloudAiReadOperation.CapacitySummary, requiredAny: ["deviceId"], allowsTimeRange: true, requiresTimeRange: true),
            Intent("Analysis.Capacity.ByDevice", CloudAiReadOperation.CapacitySummary, requiredAny: ["deviceId"], requiredTimeAlternatives: ["shiftDate"], allowsTimeRange: true),
            Intent("Analysis.ProductionData.Latest", CloudAiReadOperation.ProductionRecord, requiredAny: ["typeKey", "processId", "deviceId"], allowsTimeRange: true),
            Intent("Analysis.ProductionData.Range", CloudAiReadOperation.ProductionRecord, requiredAny: ["typeKey", "processId", "deviceId"], allowsTimeRange: true, requiresTimeRange: true),
            Intent("Analysis.ProductionData.ByDevice", CloudAiReadOperation.ProductionRecord, requiredAny: ["typeKey", "processId", "deviceId"], requiredTimeAlternatives: ["preset"], allowsTimeRange: true),
            Intent("Analysis.Process.List", CloudAiReadOperation.Process),
            Intent("Analysis.Process.Detail", CloudAiReadOperation.Process, requiredAny: ["processId", "processCode", "processName"]),
            Intent("Analysis.ClientRelease.List", CloudAiReadOperation.ClientRelease)
        }.ToDictionary(schema => schema.IntentCode, StringComparer.Ordinal);

    public static IReadOnlyCollection<CloudAiReadOperationSchema> GetOperationSchemas()
    {
        return OperationSchemas.Values.OrderBy(schema => schema.Operation).ToArray();
    }

    public static IReadOnlyCollection<CloudAiReadIntentSchema> GetIntentSchemas()
    {
        return IntentSchemas.Values.OrderBy(schema => schema.IntentCode, StringComparer.Ordinal).ToArray();
    }

    public static bool TryGetIntentSchema(string intentCode, out CloudAiReadIntentSchema schema)
    {
        return IntentSchemas.TryGetValue(intentCode, out schema!);
    }

    public static bool IsAllowedField(string intentCode, string field)
    {
        return TryGetIntentSchema(intentCode, out var intent) &&
               TryGetRule(OperationSchemas[intent.Operation], field, out _);
    }

    public static bool TryNormalizeFilter(
        string intentCode,
        string field,
        string @operator,
        string value,
        out CloudAiReadFilter normalized)
    {
        normalized = null!;
        if (!TryGetIntentSchema(intentCode, out var intent) ||
            !TryNormalizeFilter(OperationSchemas[intent.Operation], field, @operator, value, out normalized))
        {
            return false;
        }

        return true;
    }

    public static bool MatchesIntentScope(
        string intentCode,
        IReadOnlyCollection<CloudAiReadFilter> filters,
        bool hasTimeRange)
    {
        if (!TryGetIntentSchema(intentCode, out var intent) ||
            hasTimeRange && !intent.AllowsTimeRange ||
            intent.RequiresTimeRange && !hasTimeRange)
        {
            return false;
        }

        var fields = filters.Select(filter => filter.Field).ToHashSet(StringComparer.Ordinal);
        return intent.RequiredAllFilterFields.All(fields.Contains) &&
               (intent.RequiredAnyFilterFields.Count == 0 || intent.RequiredAnyFilterFields.Any(fields.Contains)) &&
               (hasTimeRange || intent.RequiredTimeAlternativeFilterFields.Count == 0 ||
                intent.RequiredTimeAlternativeFilterFields.Any(fields.Contains));
    }

    public static CloudAiReadQuery NormalizeQuery(CloudAiReadOperation operation, CloudAiReadQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(query.Filters);
        var schema = OperationSchemas[operation];
        if (query.TimeRange is not null && !schema.AllowsTimeRange)
        {
            throw new CloudAiReadException(
                CloudAiReadProblemCodes.MissingRequiredParameter,
                $"Cloud AiRead operation '{operation}' does not support a time range.");
        }

        var normalized = new List<CloudAiReadFilter>(query.Filters.Count);
        var fields = new HashSet<string>(StringComparer.Ordinal);
        foreach (var filter in query.Filters)
        {
            if (filter is null ||
                !TryNormalizeFilter(schema, filter.Field, filter.Operator, filter.Value, out var canonical) ||
                !fields.Add(canonical.Field))
            {
                throw new CloudAiReadException(
                    CloudAiReadProblemCodes.MissingRequiredParameter,
                    $"Cloud AiRead operation '{operation}' received a duplicate or unsupported typed filter.");
            }

            normalized.Add(canonical);
        }

        return query with
        {
            QueryText = null,
            Filters = normalized
                .OrderBy(filter => filter.Field, StringComparer.Ordinal)
                .ThenBy(filter => filter.Operator, StringComparer.Ordinal)
                .ThenBy(filter => filter.Value, StringComparer.Ordinal)
                .ToArray()
        };
    }

    private static bool TryNormalizeFilter(
        CloudAiReadOperationSchema schema,
        string field,
        string @operator,
        string value,
        out CloudAiReadFilter normalized)
    {
        normalized = null!;
        if (!TryGetRule(schema, field, out var rule) ||
            string.IsNullOrWhiteSpace(@operator) ||
            string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var canonicalOperator = @operator.Trim().ToLowerInvariant();
        if (!rule.Operators.Contains(canonicalOperator, StringComparer.Ordinal) ||
            !TryNormalizeValue(rule.ValueKind, value, out var canonicalValue))
        {
            return false;
        }

        normalized = new CloudAiReadFilter(rule.Field, canonicalOperator, canonicalValue);
        return true;
    }

    private static bool TryNormalizeValue(CloudAiReadFilterValueKind kind, string value, out string normalized)
    {
        normalized = value.Trim();
        switch (kind)
        {
            case CloudAiReadFilterValueKind.Guid:
                if (Guid.TryParse(normalized, out var id) && id != Guid.Empty)
                {
                    normalized = id.ToString("D");
                    return true;
                }

                return false;
            case CloudAiReadFilterValueKind.Boolean:
                if (bool.TryParse(normalized, out var boolean))
                {
                    normalized = boolean ? "true" : "false";
                    return true;
                }

                if (normalized is "1" or "0")
                {
                    normalized = normalized == "1" ? "true" : "false";
                    return true;
                }

                return false;
            case CloudAiReadFilterValueKind.LogLevel:
                return CanonicalLogLevels.TryGetValue(normalized, out normalized!);
            case CloudAiReadFilterValueKind.Date:
                return DateOnly.TryParseExact(
                    normalized,
                    "yyyy-MM-dd",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out _);
            case CloudAiReadFilterValueKind.Preset:
                normalized = normalized.ToLowerInvariant();
                return normalized is "last_24h" or "last_7d" or "today" or "yesterday";
            case CloudAiReadFilterValueKind.HourlyPreset:
                normalized = normalized.ToLowerInvariant();
                return normalized is "last_24h" or "today" or "yesterday";
            case CloudAiReadFilterValueKind.FieldMode:
                normalized = normalized.ToLowerInvariant();
                return normalized is "list" or "full";
            default:
                return TokenPattern.IsMatch(normalized);
        }
    }

    private static bool TryGetRule(
        CloudAiReadOperationSchema schema,
        string field,
        out CloudAiReadFilterRule rule)
    {
        rule = schema.Filters.FirstOrDefault(candidate =>
            string.Equals(candidate.Field, field, StringComparison.OrdinalIgnoreCase))!;
        return rule is not null;
    }

    private static CloudAiReadFilterRule Rule(
        string field,
        CloudAiReadFilterValueKind valueKind = CloudAiReadFilterValueKind.Token)
    {
        return new CloudAiReadFilterRule(field, valueKind, ["eq"]);
    }

    private static CloudAiReadFilterRule KeywordRule(string field)
    {
        return new CloudAiReadFilterRule(
            field,
            CloudAiReadFilterValueKind.Token,
            ["contains", "eq"]);
    }

    private static CloudAiReadOperationSchema Schema(
        CloudAiReadOperation operation,
        string endpointPath,
        params CloudAiReadFilterRule[] filters)
    {
        return new CloudAiReadOperationSchema(operation, endpointPath, false, filters);
    }

    private static CloudAiReadOperationSchema SchemaWithTimeRange(
        CloudAiReadOperation operation,
        string endpointPath,
        params CloudAiReadFilterRule[] filters)
    {
        return new CloudAiReadOperationSchema(operation, endpointPath, true, filters);
    }

    private static CloudAiReadIntentSchema Intent(
        string intentCode,
        CloudAiReadOperation operation,
        IReadOnlyCollection<string>? requiredAll = null,
        IReadOnlyCollection<string>? requiredAny = null,
        IReadOnlyCollection<string>? requiredTimeAlternatives = null,
        bool allowsTimeRange = false,
        bool requiresTimeRange = false)
    {
        return new CloudAiReadIntentSchema(
            intentCode,
            operation,
            requiredAll ?? [],
            requiredAny ?? [],
            requiredTimeAlternatives ?? [],
            allowsTimeRange,
            requiresTimeRange);
    }
}

public sealed record CloudAiReadFilter(string Field, string Operator, string Value);

public sealed record CloudAiReadTimeRange(string Field, DateTimeOffset? Start, DateTimeOffset? End);

public sealed record CloudAiReadQuery(
    string? QueryText,
    IReadOnlyList<CloudAiReadFilter> Filters,
    CloudAiReadTimeRange? TimeRange,
    string? SortField,
    bool SortDescending,
    int Limit)
{
    public static CloudAiReadQuery FromSemanticPlan(SemanticQueryPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (!CloudAiReadRowLimitPolicy.IsWithinBounds(plan.Limit))
        {
            throw new CloudAiReadException(
                AppProblemCodes.CloudReadonlyIntentUnsupported,
                "Cloud readonly intent violates the frozen typed semantic plan contract.");
        }

        return new CloudAiReadQuery(
            plan.QueryText,
            plan.Filters
                .Select(filter => new CloudAiReadFilter(
                    filter.Field,
                    filter.Operator switch
                    {
                        SemanticFilterOperator.Contains => "contains",
                        SemanticFilterOperator.GreaterOrEqual => "gte",
                        SemanticFilterOperator.LessOrEqual => "lte",
                        SemanticFilterOperator.In => "in",
                        _ => "eq"
                    },
                    filter.Value))
                .ToArray(),
            plan.TimeRange is null
                ? null
                : new CloudAiReadTimeRange(plan.TimeRange.Field, plan.TimeRange.Start, plan.TimeRange.End),
            plan.Sort?.Field,
            plan.Sort?.Direction == SemanticSortDirection.Desc,
            plan.Limit);
    }

    public CloudAiReadQuery WithFilters(IReadOnlyList<CloudAiReadFilter> filters)
    {
        return this with { Filters = filters };
    }
}

public sealed record CloudAiReadResult<T>(
    string SourcePath,
    string SourceLabel,
    DateTimeOffset QueriedAtUtc,
    int Limit,
    bool IsTruncated,
    IReadOnlyList<T> Items,
    IReadOnlyList<Dictionary<string, object?>> Rows,
    string ProviderSource = "",
    string QueryScope = "",
    int RowCount = 0,
    string? NextCursor = null)
{
    public DateTimeOffset AsOfUtc => QueriedAtUtc;
}

public sealed record CloudAiReadDeviceDto(
    Guid DeviceId,
    string DeviceCode,
    string DeviceName,
    Guid ProcessId,
    IReadOnlyDictionary<string, object?> AdditionalFields);

public sealed record CloudAiReadProcessDto(
    Guid ProcessId,
    string ProcessCode,
    string ProcessName,
    IReadOnlyDictionary<string, object?> AdditionalFields);

public sealed record CloudAiReadClientReleaseVersionDto(
    Guid ReleaseId,
    string ComponentKind,
    string ComponentKey,
    string DisplayName,
    string Channel,
    string TargetRuntime,
    string Version,
    string Status,
    string? ReleaseNotes,
    DateTime CreatedAtUtc,
    DateTime? PublishedAtUtc,
    DateTime? DeletedAtUtc,
    IReadOnlyDictionary<string, object?> AdditionalFields);

public sealed record CloudAiReadDeviceClientStateDto(
    Guid DeviceId,
    string DeviceName,
    string ClientCode,
    string? PrimaryIp,
    string? Channel,
    string? HostVersion,
    string? HostApiVersion,
    DateTime? VersionReportedAtUtc,
    DateTime? VersionReceivedAtUtc,
    string SoftwareStatus,
    string? RuntimeStatus,
    DateTime? RuntimeStartedAtUtc,
    DateTime? LastRuntimeHeartbeatAtUtc,
    DateTime? UpdatedAtUtc,
    IReadOnlyDictionary<string, object?> AdditionalFields);

public sealed record CloudAiReadCapacitySummaryDto(
    DateOnly Date,
    int TotalCount,
    int OkCount,
    int NgCount,
    int DayShiftTotal,
    int NightShiftTotal,
    IReadOnlyDictionary<string, object?> AdditionalFields);

public sealed record CloudAiReadCapacityHourlyDto(
    DateTime Time,
    DateOnly Date,
    int Hour,
    int Minute,
    string TimeLabel,
    string ShiftCode,
    int TotalCount,
    int OkCount,
    int NgCount,
    decimal OkRate,
    IReadOnlyDictionary<string, object?> AdditionalFields);

public sealed record CloudAiReadDeviceLogDto(
    Guid LogId,
    Guid DeviceId,
    string DeviceName,
    string Level,
    string Message,
    DateTime LogTime,
    DateTime ReceivedAt,
    IReadOnlyDictionary<string, object?> AdditionalFields);

public sealed record CloudAiReadProductionFieldSchemaDto(
    string Key,
    string Label,
    string Type,
    string? Unit,
    int? Precision,
    bool Required,
    IReadOnlyDictionary<string, object?> AdditionalFields);

public sealed record CloudAiReadProductionRecordDto(
    Guid RecordId,
    string TypeKey,
    string TypeName,
    Guid DeviceId,
    string DeviceName,
    string? Barcode,
    string? Result,
    DateTime? CompletedAt,
    DateTime? ReceivedAt,
    IReadOnlyDictionary<string, object?> Fields,
    IReadOnlyList<CloudAiReadProductionFieldSchemaDto> FieldSchema,
    IReadOnlyDictionary<string, object?> AdditionalFields);

public partial interface ICloudAiReadClient
{
    bool IsEnabled { get; }

    Task<CloudAiReadResult<object>> QuerySemanticAsync(
        SemanticQueryPlan plan,
        CancellationToken cancellationToken = default);
}
