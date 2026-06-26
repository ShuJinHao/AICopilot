using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AICopilot.Services.Contracts;

public static class CloudAiReadProblemCodes
{
    public const string NotConfigured = "cloud_ai_read_not_configured";
    public const string RequestBlocked = "cloud_ai_read_request_blocked";
    public const string Unauthorized = "cloud_ai_read_unauthorized";
    public const string Forbidden = "cloud_ai_read_forbidden";
    public const string NotFound = "cloud_ai_read_not_found";
    public const string Unavailable = "cloud_ai_read_unavailable";
    public const string MissingRequiredParameter = "cloud_ai_read_missing_required_parameter";
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

    public string[] ExplicitPostQueryPaths { get; init; } = [];

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

        foreach (var path in ExplicitPostQueryPaths)
        {
            var decision = CloudAiReadEndpointPolicy.Evaluate(HttpMethod.Post, path, ExplicitPostQueryPaths);
            if (!decision.IsAllowed)
            {
                throw new InvalidOperationException($"CloudAiRead:ExplicitPostQueryPaths contains an unsafe path '{path}': {decision.Reason}");
            }
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
        "/api/v1/ai/read/capacity/summary",
        "/api/v1/ai/read/device-logs"
    ];

    private static readonly string[] ReadOnlyPostNameTokens =
    [
        "query",
        "search",
        "analyze"
    ];

    private static readonly string[] ForbiddenWriteTokens =
    [
        "create",
        "update",
        "delete",
        "register",
        "disable",
        "approve",
        "dispatch",
        "trigger",
        "backfill",
        "correct",
        "upload",
        "submit",
        "write"
    ];

    public static CloudAiReadRequestDecision Evaluate(
        HttpMethod method,
        string path,
        IReadOnlyCollection<string>? explicitPostQueryPaths = null)
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

        if (method == HttpMethod.Post)
        {
            if (!IsAiBoundaryPath(normalizedPath))
            {
                return CloudAiReadRequestDecision.Block("Cloud AiRead POST path must stay under /api/v1/ai/read/* or /api/v1/ai/identity/*.");
            }

            if (explicitPostQueryPaths is null || explicitPostQueryPaths.Count == 0)
            {
                return CloudAiReadRequestDecision.Block("Cloud AiRead POST is disabled unless explicitly allowlisted.");
            }

            var normalizedPostPaths = explicitPostQueryPaths
                .Select(path => TryNormalizePath(path, out var normalized, out _) ? normalized : string.Empty)
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            if (!normalizedPostPaths.Contains(normalizedPath))
            {
                return CloudAiReadRequestDecision.Block("Cloud AiRead POST path is not explicitly allowlisted.");
            }

            var lastSegment = normalizedPath.Split('/', StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? string.Empty;
            if (!ReadOnlyPostNameTokens.Any(token => lastSegment.Contains(token, StringComparison.OrdinalIgnoreCase)))
            {
                return CloudAiReadRequestDecision.Block("Cloud AiRead POST path must be named as query/search/analyze.");
            }

            if (ForbiddenWriteTokens.Any(token => normalizedPath.Contains(token, StringComparison.OrdinalIgnoreCase)))
            {
                return CloudAiReadRequestDecision.Block("Cloud AiRead POST path contains forbidden write semantics.");
            }

            return CloudAiReadRequestDecision.Allow;
        }

        return CloudAiReadRequestDecision.Block("Cloud AiRead only allows GET by default and explicitly allowlisted read-only POST.");
    }

    private static bool IsAiBoundaryPath(string normalizedPath)
    {
        return normalizedPath.StartsWith("/api/v1/ai/read/", StringComparison.OrdinalIgnoreCase) ||
               normalizedPath.StartsWith("/api/v1/ai/identity/", StringComparison.OrdinalIgnoreCase);
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
        if (AllowedGetPaths.Contains(normalizedPath, StringComparer.OrdinalIgnoreCase))
        {
            return true;
        }

        if (normalizedPath.StartsWith("/api/v1/ai/identity/", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        const string passStationPrefix = "/api/v1/ai/read/pass-stations/";
        if (!normalizedPath.StartsWith(passStationPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var typeKey = normalizedPath[passStationPrefix.Length..];
        return IsSafeRouteSegment(typeKey);
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
            or SemanticQueryTarget.ProductionData;
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
    int Limit,
    string? PassStationTypeKey = null)
{
    public static CloudAiReadQuery FromSemanticPlan(
        SemanticQueryPlan plan,
        string? passStationTypeKey = null)
    {
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
            plan.Limit,
            passStationTypeKey);
    }
}

public sealed record CloudAiReadResult<T>(
    string SourcePath,
    string SourceLabel,
    DateTimeOffset QueriedAtUtc,
    int Limit,
    bool IsTruncated,
    IReadOnlyList<T> Items,
    IReadOnlyList<Dictionary<string, object?>> Rows);

public sealed record CloudAiReadDeviceDto(
    string? DeviceId,
    string? DeviceCode,
    string? DeviceName,
    string? Status,
    string? LineName,
    DateTimeOffset? UpdatedAt,
    IReadOnlyDictionary<string, object?> AdditionalFields);

public sealed record CloudAiReadCapacitySummaryDto(
    string? RecordId,
    string? DeviceId,
    string? DeviceCode,
    string? ProcessName,
    string? ShiftDate,
    decimal? OutputQty,
    decimal? QualifiedQty,
    DateTimeOffset? OccurredAt,
    IReadOnlyDictionary<string, object?> AdditionalFields);

public sealed record CloudAiReadDeviceLogDto(
    string? LogId,
    string? DeviceId,
    string? DeviceCode,
    string? Level,
    string? Message,
    string? Source,
    DateTimeOffset? OccurredAt,
    IReadOnlyDictionary<string, object?> AdditionalFields);

public sealed record CloudAiReadPassStationRecordDto(
    string? RecordId,
    string? DeviceId,
    string? DeviceCode,
    string? ProcessName,
    string? Barcode,
    string? StationName,
    string? Result,
    DateTimeOffset? OccurredAt,
    IReadOnlyDictionary<string, object?> AdditionalFields);

public interface ICloudAiReadClient
{
    bool IsEnabled { get; }

    Task<JsonDocument> SendJsonAsync(
        HttpMethod method,
        string path,
        IReadOnlyDictionary<string, string?>? query = null,
        CancellationToken cancellationToken = default);

    Task<CloudAiReadResult<CloudAiReadDeviceDto>> GetDevicesAsync(
        CloudAiReadQuery query,
        CancellationToken cancellationToken = default);

    Task<CloudAiReadResult<CloudAiReadCapacitySummaryDto>> GetCapacitySummaryAsync(
        CloudAiReadQuery query,
        CancellationToken cancellationToken = default);

    Task<CloudAiReadResult<CloudAiReadDeviceLogDto>> GetDeviceLogsAsync(
        CloudAiReadQuery query,
        CancellationToken cancellationToken = default);

    Task<CloudAiReadResult<CloudAiReadPassStationRecordDto>> GetPassStationRecordsAsync(
        CloudAiReadQuery query,
        CancellationToken cancellationToken = default);

    Task<CloudAiReadResult<object>> QuerySemanticAsync(
        SemanticQueryPlan plan,
        CancellationToken cancellationToken = default);
}

public interface ICloudReadonlySandboxClient
{
    Task<JsonDocument> SendJsonAsync(
        CloudReadonlySandboxOptions options,
        HttpMethod method,
        string path,
        IReadOnlyDictionary<string, string?>? query = null,
        CancellationToken cancellationToken = default);
}
