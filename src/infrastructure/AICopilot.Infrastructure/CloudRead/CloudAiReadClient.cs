using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using AICopilot.Services.Contracts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AICopilot.Infrastructure.CloudRead;

public sealed class CloudAiReadClient(
    HttpClient httpClient,
    IOptions<CloudAiReadOptions> options,
    ILogger<CloudAiReadClient> logger) : ICloudAiReadClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    public bool IsEnabled => options.Value.Enabled;

    public async Task<JsonDocument> SendJsonAsync(
        HttpMethod method,
        string path,
        IReadOnlyDictionary<string, string?>? query = null,
        CancellationToken cancellationToken = default)
    {
        var configuredOptions = EnsureConfigured();
        var decision = CloudAiReadEndpointPolicy.Evaluate(
            method,
            path,
            configuredOptions.ExplicitPostQueryPaths);
        if (!decision.IsAllowed)
        {
            throw new CloudAiReadException(
                CloudAiReadProblemCodes.RequestBlocked,
                decision.Reason ?? "Cloud AiRead request was blocked by the allowlist policy.");
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(configuredOptions.TimeoutSeconds));

        using var request = new HttpRequestMessage(
            method,
            BuildUri(configuredOptions, path, query));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", configuredOptions.ServiceAccountToken);

        try
        {
            using var response = await httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                timeoutCts.Token);
            if (!response.IsSuccessStatusCode)
            {
                throw CreateResponseException(response.StatusCode);
            }

            await using var stream = await response.Content.ReadAsStreamAsync(timeoutCts.Token);
            return await JsonDocument.ParseAsync(stream, cancellationToken: timeoutCts.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new CloudAiReadException(
                CloudAiReadProblemCodes.Unavailable,
                "Cloud AiRead request timed out.");
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "Cloud AiRead request failed before receiving a response. Path={Path}", path);
            throw new CloudAiReadException(
                CloudAiReadProblemCodes.Unavailable,
                "Cloud AiRead endpoint is unavailable.");
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "Cloud AiRead response was not valid JSON. Path={Path}", path);
            throw new CloudAiReadException(
                CloudAiReadProblemCodes.Unavailable,
                "Cloud AiRead endpoint returned an invalid JSON payload.");
        }
    }

    public async Task<CloudAiReadResult<CloudAiReadDeviceDto>> GetDevicesAsync(
        CloudAiReadQuery query,
        CancellationToken cancellationToken = default)
    {
        const string path = "/api/v1/ai/read/devices";
        using var document = await SendJsonAsync(
            HttpMethod.Get,
            path,
            BuildDeviceQueryParameters(query),
            cancellationToken);

        return CloudAiReadDocumentAdapter.MapDevices(document.RootElement, path, query.Limit);
    }

    public async Task<CloudAiReadResult<CloudAiReadCapacitySummaryDto>> GetCapacitySummaryAsync(
        CloudAiReadQuery query,
        CancellationToken cancellationToken = default)
    {
        const string path = "/api/v1/ai/read/capacity/summary";
        using var document = await SendJsonAsync(
            HttpMethod.Get,
            path,
            BuildCapacityQueryParameters(query),
            cancellationToken);

        return CloudAiReadDocumentAdapter.MapCapacitySummary(document.RootElement, path, query.Limit);
    }

    public async Task<CloudAiReadResult<CloudAiReadDeviceLogDto>> GetDeviceLogsAsync(
        CloudAiReadQuery query,
        CancellationToken cancellationToken = default)
    {
        const string path = "/api/v1/ai/read/device-logs";
        using var document = await SendJsonAsync(
            HttpMethod.Get,
            path,
            BuildDeviceLogQueryParameters(query),
            cancellationToken);

        return CloudAiReadDocumentAdapter.MapDeviceLogs(document.RootElement, path, query.Limit);
    }

    public async Task<CloudAiReadResult<CloudAiReadPassStationRecordDto>> GetPassStationRecordsAsync(
        CloudAiReadQuery query,
        CancellationToken cancellationToken = default)
    {
        var typeKey = string.IsNullOrWhiteSpace(query.PassStationTypeKey)
            ? options.Value.DefaultPassStationTypeKey
            : query.PassStationTypeKey.Trim();
        var path = $"/api/v1/ai/read/pass-stations/{Uri.EscapeDataString(typeKey)}";
        using var document = await SendJsonAsync(
            HttpMethod.Get,
            path,
            BuildPassStationQueryParameters(query),
            cancellationToken);

        return CloudAiReadDocumentAdapter.MapPassStationRecords(document.RootElement, path, query.Limit);
    }

    public async Task<CloudAiReadResult<object>> QuerySemanticAsync(
        SemanticQueryPlan plan,
        CancellationToken cancellationToken = default)
    {
        var query = CloudAiReadQuery.FromSemanticPlan(plan, options.Value.DefaultPassStationTypeKey);
        return plan.Target switch
        {
            SemanticQueryTarget.Device => ToUntyped(await GetDevicesAsync(query, cancellationToken)),
            SemanticQueryTarget.Capacity => ToUntyped(await GetCapacitySummaryAsync(query, cancellationToken)),
            SemanticQueryTarget.DeviceLog => ToUntyped(await GetDeviceLogsAsync(query, cancellationToken)),
            SemanticQueryTarget.ProductionData => ToUntyped(await GetPassStationRecordsAsync(query, cancellationToken)),
            _ => throw new NotSupportedException($"Cloud AiRead does not support semantic target '{plan.Target}'.")
        };
    }

    private static CloudAiReadResult<object> ToUntyped<T>(CloudAiReadResult<T> result)
    {
        return new CloudAiReadResult<object>(
            result.SourcePath,
            result.SourceLabel,
            result.QueriedAtUtc,
            result.Limit,
            result.IsTruncated,
            result.Items.Cast<object>().ToArray(),
            result.Rows);
    }

    private CloudAiReadOptions EnsureConfigured()
    {
        var configuredOptions = options.Value;
        if (!configuredOptions.Enabled)
        {
            throw new CloudAiReadException(
                CloudAiReadProblemCodes.NotConfigured,
                "Cloud AiRead is not enabled.");
        }

        configuredOptions.EnsureValid();
        return configuredOptions;
    }

    private static Uri BuildUri(
        CloudAiReadOptions options,
        string path,
        IReadOnlyDictionary<string, string?>? query)
    {
        var baseUri = new Uri(options.BaseUrl.TrimEnd('/') + "/", UriKind.Absolute);
        var relativePath = path.TrimStart('/');
        var relativeUri = query is null || query.Count == 0
            ? relativePath
            : $"{relativePath}?{BuildQueryString(query)}";
        return new Uri(baseUri, relativeUri);
    }

    private static string BuildQueryString(IReadOnlyDictionary<string, string?> query)
    {
        return string.Join(
            '&',
            query
                .Where(item => !string.IsNullOrWhiteSpace(item.Value))
                .Select(item =>
                    $"{Uri.EscapeDataString(item.Key)}={Uri.EscapeDataString(item.Value!)}"));
    }

    private static Dictionary<string, string?> BuildDeviceQueryParameters(CloudAiReadQuery query)
    {
        var parameters = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["maxRows"] = FormatMaxRows(query.Limit)
        };

        var keyword = FirstNonBlank(
            GetFilterValue(query, "keyword", "deviceId", "deviceCode", "deviceName", "lineName", "status"),
            query.QueryText);
        if (!string.IsNullOrWhiteSpace(keyword))
        {
            parameters["keyword"] = keyword;
        }

        return parameters;
    }

    private static Dictionary<string, string?> BuildCapacityQueryParameters(CloudAiReadQuery query)
    {
        var deviceId = RequireFilterValue(query, "deviceId", "请补充设备 ID。", "deviceId", "deviceCode");
        var (start, end) = RequireTimeRange(query, "请补充产能查询的开始日期和结束日期。");
        var deviceCode = GetFilterValue(query, "deviceCode");

        var parameters = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["deviceId"] = deviceId,
            ["deviceCode"] = deviceCode,
            ["startDate"] = FormatCloudDate(start),
            ["endDate"] = FormatCloudDate(end),
            ["plcName"] = GetFilterValue(query, "plcName", "processName"),
            ["maxRows"] = FormatMaxRows(query.Limit)
        };

        return parameters;
    }

    private static Dictionary<string, string?> BuildDeviceLogQueryParameters(CloudAiReadQuery query)
    {
        var deviceId = RequireFilterValue(query, "deviceId", "请补充设备 ID。", "deviceId", "deviceCode");
        var (start, end) = RequireTimeRange(query, "请补充日志查询的开始时间和结束时间。");
        var deviceCode = GetFilterValue(query, "deviceCode");

        var parameters = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["deviceId"] = deviceId,
            ["deviceCode"] = deviceCode,
            ["startTime"] = FormatCloudTime(start),
            ["endTime"] = FormatCloudTime(end),
            ["level"] = GetFilterValue(query, "level"),
            ["keyword"] = FirstNonBlank(GetFilterValue(query, "keyword", "message", "source"), query.QueryText),
            ["maxRows"] = FormatMaxRows(query.Limit)
        };

        return parameters;
    }

    private static Dictionary<string, string?> BuildPassStationQueryParameters(CloudAiReadQuery query)
    {
        var (start, end) = RequireTimeRange(query, "请补充过站/生产数据查询的开始时间和结束时间。");

        var parameters = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["deviceId"] = FirstNonBlank(GetFilterValue(query, "deviceId"), GetFilterValue(query, "deviceCode")),
            ["deviceCode"] = GetFilterValue(query, "deviceCode"),
            ["startTime"] = FormatCloudTime(start),
            ["endTime"] = FormatCloudTime(end),
            ["barcode"] = GetFilterValue(query, "barcode"),
            ["maxRows"] = FormatMaxRows(query.Limit)
        };

        return parameters;
    }

    private static string FormatMaxRows(int limit)
    {
        return Math.Max(1, limit).ToString(CultureInfo.InvariantCulture);
    }

    private static string FormatCloudDate(DateTimeOffset value)
    {
        return value.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    private static string FormatCloudTime(DateTimeOffset value)
    {
        return value.UtcDateTime.ToString("O", CultureInfo.InvariantCulture);
    }

    private static string RequireFilterValue(
        CloudAiReadQuery query,
        string parameterName,
        string guidance,
        params string[] fieldNames)
    {
        var value = GetFilterValue(query, fieldNames);
        if (!string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        throw new CloudAiReadException(
            CloudAiReadProblemCodes.MissingRequiredParameter,
            $"Cloud AiRead 查询缺少必需参数 {parameterName}，{guidance}");
    }

    private static (DateTimeOffset Start, DateTimeOffset End) RequireTimeRange(
        CloudAiReadQuery query,
        string guidance)
    {
        if (query.TimeRange?.Start is { } start && query.TimeRange.End is { } end)
        {
            return (start, end);
        }

        throw new CloudAiReadException(
            CloudAiReadProblemCodes.MissingRequiredParameter,
            $"Cloud AiRead 查询缺少必需时间范围，{guidance}");
    }

    private static string? GetFilterValue(CloudAiReadQuery query, params string[] fieldNames)
    {
        foreach (var fieldName in fieldNames)
        {
            var filter = query.Filters.FirstOrDefault(item =>
                fieldName.Equals(item.Field, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(filter?.Value))
            {
                return filter.Value.Trim();
            }
        }

        return null;
    }

    private static string? FirstNonBlank(params string?[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();
    }

    private static CloudAiReadException CreateResponseException(HttpStatusCode statusCode)
    {
        return statusCode switch
        {
            HttpStatusCode.Unauthorized => new CloudAiReadException(
                CloudAiReadProblemCodes.Unauthorized,
                "Cloud AiRead credential is missing or invalid.",
                statusCode),
            HttpStatusCode.Forbidden => new CloudAiReadException(
                CloudAiReadProblemCodes.Forbidden,
                "Cloud AiRead permission or device scope is insufficient.",
                statusCode),
            HttpStatusCode.NotFound => new CloudAiReadException(
                CloudAiReadProblemCodes.NotFound,
                "Cloud AiRead resource was not found.",
                statusCode),
            _ => new CloudAiReadException(
                CloudAiReadProblemCodes.Unavailable,
                $"Cloud AiRead endpoint returned {(int)statusCode}.",
                statusCode)
        };
    }
}

internal static class CloudAiReadDocumentAdapter
{
    public static CloudAiReadResult<CloudAiReadDeviceDto> MapDevices(
        JsonElement root,
        string sourcePath,
        int limit)
    {
        var records = ExtractRecords(root, limit);
        var items = records.Select(record => new CloudAiReadDeviceDto(
            GetString(record, "deviceId", "id"),
            GetString(record, "deviceCode", "clientCode", "code"),
            GetString(record, "deviceName", "name"),
            GetString(record, "status", "state"),
            GetString(record, "lineName", "line"),
            GetDate(record, "updatedAt", "updatedAtUtc", "lastSeenAt"),
            ExtractAdditionalFields(record))).ToArray();

        var rows = items.Select(item => new Dictionary<string, object?>
        {
            ["deviceId"] = item.DeviceId,
            ["deviceCode"] = item.DeviceCode,
            ["deviceName"] = item.DeviceName,
            ["status"] = item.Status,
            ["lineName"] = item.LineName,
            ["updatedAt"] = item.UpdatedAt
        }).ToArray();

        return BuildResult(sourcePath, "Cloud AiRead API（设备正式只读数据）", limit, root, items, rows);
    }

    public static CloudAiReadResult<CloudAiReadCapacitySummaryDto> MapCapacitySummary(
        JsonElement root,
        string sourcePath,
        int limit)
    {
        var records = ExtractRecords(root, limit);
        var items = records.Select(record => new CloudAiReadCapacitySummaryDto(
            GetString(record, "recordId", "id"),
            GetString(record, "deviceId"),
            GetString(record, "deviceCode", "clientCode"),
            GetString(record, "processName", "process"),
            GetString(record, "shiftDate", "date"),
            GetDecimal(record, "outputQty", "output", "totalOutput"),
            GetDecimal(record, "qualifiedQty", "qualified", "goodQty"),
            GetDate(record, "occurredAt", "recordedAt", "updatedAt"),
            ExtractAdditionalFields(record))).ToArray();

        var rows = items.Select(item => new Dictionary<string, object?>
        {
            ["recordId"] = item.RecordId,
            ["deviceId"] = item.DeviceId,
            ["deviceCode"] = item.DeviceCode,
            ["processName"] = item.ProcessName,
            ["shiftDate"] = item.ShiftDate,
            ["outputQty"] = item.OutputQty,
            ["qualifiedQty"] = item.QualifiedQty,
            ["occurredAt"] = item.OccurredAt
        }).ToArray();

        return BuildResult(sourcePath, "Cloud AiRead API（产能正式只读数据）", limit, root, items, rows);
    }

    public static CloudAiReadResult<CloudAiReadDeviceLogDto> MapDeviceLogs(
        JsonElement root,
        string sourcePath,
        int limit)
    {
        var records = ExtractRecords(root, limit);
        var items = records.Select(record => new CloudAiReadDeviceLogDto(
            GetString(record, "logId", "id"),
            GetString(record, "deviceId"),
            GetString(record, "deviceCode", "clientCode"),
            GetString(record, "level", "severity"),
            GetString(record, "message", "content"),
            GetString(record, "source", "logger"),
            GetDate(record, "occurredAt", "createdAt", "timestamp"),
            ExtractAdditionalFields(record))).ToArray();

        var rows = items.Select(item => new Dictionary<string, object?>
        {
            ["logId"] = item.LogId,
            ["deviceId"] = item.DeviceId,
            ["deviceCode"] = item.DeviceCode,
            ["level"] = item.Level,
            ["message"] = item.Message,
            ["source"] = item.Source,
            ["occurredAt"] = item.OccurredAt
        }).ToArray();

        return BuildResult(sourcePath, "Cloud AiRead API（设备日志正式只读数据）", limit, root, items, rows);
    }

    public static CloudAiReadResult<CloudAiReadPassStationRecordDto> MapPassStationRecords(
        JsonElement root,
        string sourcePath,
        int limit)
    {
        var records = ExtractRecords(root, limit);
        var items = records.Select(record => new CloudAiReadPassStationRecordDto(
            GetString(record, "recordId", "id"),
            GetString(record, "deviceId"),
            GetString(record, "deviceCode", "clientCode"),
            GetString(record, "processName", "process"),
            GetString(record, "barcode", "cellCode", "sn"),
            GetString(record, "stationName", "station"),
            GetString(record, "result", "status"),
            GetDate(record, "occurredAt", "passedAt", "createdAt"),
            ExtractAdditionalFields(record))).ToArray();

        var rows = items.Select(item => new Dictionary<string, object?>
        {
            ["recordId"] = item.RecordId,
            ["deviceId"] = item.DeviceId,
            ["deviceCode"] = item.DeviceCode,
            ["processName"] = item.ProcessName,
            ["barcode"] = item.Barcode,
            ["stationName"] = item.StationName,
            ["result"] = item.Result,
            ["occurredAt"] = item.OccurredAt
        }).ToArray();

        return BuildResult(sourcePath, "Cloud AiRead API（过站/生产正式只读数据）", limit, root, items, rows);
    }

    private static CloudAiReadResult<T> BuildResult<T>(
        string sourcePath,
        string sourceLabel,
        int limit,
        JsonElement root,
        IReadOnlyList<T> items,
        IReadOnlyList<Dictionary<string, object?>> rows)
    {
        return new CloudAiReadResult<T>(
            sourcePath,
            sourceLabel,
            DateTimeOffset.UtcNow,
            limit,
            IsTruncated(root, items.Count, limit),
            items,
            rows);
    }

    private static IReadOnlyList<JsonElement> ExtractRecords(JsonElement root, int limit)
    {
        var effectiveLimit = Math.Max(1, limit);
        var source = EnumerateRecords(root);

        return source.Take(effectiveLimit).Select(item => item.Clone()).ToArray();
    }

    private static IEnumerable<JsonElement> EnumerateRecords(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Array)
        {
            return root.EnumerateArray();
        }

        if (root.ValueKind == JsonValueKind.Object && TryGetArray(root, out var array))
        {
            return array.EnumerateArray();
        }

        return root.ValueKind == JsonValueKind.Object ? [root] : [];
    }

    private static bool TryGetArray(JsonElement root, out JsonElement array)
    {
        foreach (var name in new[] { "items", "data", "records", "results" })
        {
            if (root.TryGetProperty(name, out array) && array.ValueKind == JsonValueKind.Array)
            {
                return true;
            }
        }

        array = default;
        return false;
    }

    private static bool IsTruncated(JsonElement root, int itemCount, int limit)
    {
        if (root.ValueKind == JsonValueKind.Object &&
            TryGetBoolean(root, out var isTruncated, "isTruncated", "truncated"))
        {
            return isTruncated;
        }

        return itemCount >= Math.Max(1, limit);
    }

    private static string? GetString(JsonElement record, params string[] names)
    {
        foreach (var name in names)
        {
            if (!record.TryGetProperty(name, out var property))
            {
                continue;
            }

            if (property.ValueKind == JsonValueKind.String)
            {
                return property.GetString();
            }

            if (property.ValueKind is JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False)
            {
                return property.ToString();
            }
        }

        return null;
    }

    private static decimal? GetDecimal(JsonElement record, params string[] names)
    {
        foreach (var name in names)
        {
            if (!record.TryGetProperty(name, out var property))
            {
                continue;
            }

            if (property.ValueKind == JsonValueKind.Number && property.TryGetDecimal(out var decimalValue))
            {
                return decimalValue;
            }

            if (property.ValueKind == JsonValueKind.String &&
                decimal.TryParse(property.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed))
            {
                return parsed;
            }
        }

        return null;
    }

    private static DateTimeOffset? GetDate(JsonElement record, params string[] names)
    {
        foreach (var name in names)
        {
            if (!record.TryGetProperty(name, out var property))
            {
                continue;
            }

            if (property.ValueKind == JsonValueKind.String &&
                DateTimeOffset.TryParse(property.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed))
            {
                return parsed;
            }
        }

        return null;
    }

    private static bool TryGetBoolean(JsonElement record, out bool value, params string[] names)
    {
        foreach (var name in names)
        {
            if (!record.TryGetProperty(name, out var property))
            {
                continue;
            }

            if (property.ValueKind is JsonValueKind.True or JsonValueKind.False)
            {
                value = property.GetBoolean();
                return true;
            }
        }

        value = false;
        return false;
    }

    private static IReadOnlyDictionary<string, object?> ExtractAdditionalFields(JsonElement record)
    {
        if (record.ValueKind != JsonValueKind.Object)
        {
            return new Dictionary<string, object?>();
        }

        return record.EnumerateObject()
            .ToDictionary(
                property => property.Name,
                property => ToObject(property.Value),
                StringComparer.OrdinalIgnoreCase);
    }

    private static object? ToObject(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number when element.TryGetInt64(out var longValue) => longValue,
            JsonValueKind.Number when element.TryGetDecimal(out var decimalValue) => decimalValue,
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            _ => element.GetRawText()
        };
    }
}
