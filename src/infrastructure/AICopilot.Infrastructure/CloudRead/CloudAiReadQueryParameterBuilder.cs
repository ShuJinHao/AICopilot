using System.Globalization;
using AICopilot.Services.Contracts;

namespace AICopilot.Infrastructure.CloudRead;

internal static class CloudAiReadQueryParameterBuilder
{
    private static readonly HashSet<string> TimePresetValues = new(StringComparer.OrdinalIgnoreCase)
    {
        "last_24h",
        "last_7d",
        "today",
        "yesterday"
    };

    private static readonly HashSet<string> CapacityHourlyPresetValues = new(StringComparer.OrdinalIgnoreCase)
    {
        "last_24h",
        "today",
        "yesterday"
    };

    public static Dictionary<string, string?> BuildDeviceQueryParameters(CloudAiReadQuery query)
    {
        RejectUnsupportedFilters(
            query,
            "Cloud /devices 不支持运行状态、产线或投影时间过滤，这些条件不能降级为 keyword。",
            "status",
            "lineName",
            "processName",
            "runtimeStatus",
            "softwareStatus",
            "updatedAt",
            "updatedAtUtc");
        RejectUnknownFilters(query, "Cloud /devices 收到未批准的过滤字段。", "deviceId", "deviceCode", "processId", "keyword", "deviceName");
        return new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["deviceId"] = GetOptionalGuidFilterValue(query, "deviceId"),
            ["deviceCode"] = GetFilterValue(query, "deviceCode"),
            ["processId"] = GetOptionalGuidFilterValue(query, "processId"),
            ["keyword"] = GetFilterValue(query, "keyword", "deviceName"),
            ["maxRows"] = FormatMaxRows(query.Limit)
        };
    }

    public static Dictionary<string, string?> BuildProcessQueryParameters(CloudAiReadQuery query)
    {
        RejectUnknownFilters(query, "Cloud /processes 收到未批准的过滤字段。", "processId", "keyword", "processCode", "processName");
        return new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["processId"] = GetOptionalGuidFilterValue(query, "processId"),
            ["keyword"] = GetFilterValue(query, "keyword", "processCode", "processName"),
            ["maxRows"] = FormatMaxRows(query.Limit)
        };
    }

    public static Dictionary<string, string?> BuildClientReleaseQueryParameters(CloudAiReadQuery query)
    {
        RejectUnknownFilters(query, "Cloud /client-releases 收到未批准的过滤字段。", "channel", "targetRuntime", "status", "includeArchived");
        return new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["channel"] = GetFilterValue(query, "channel"),
            ["targetRuntime"] = GetFilterValue(query, "targetRuntime"),
            ["status"] = GetFilterValue(query, "status"),
            ["includeArchived"] = NormalizeBoolean(GetFilterValue(query, "includeArchived")),
            ["maxRows"] = FormatMaxRows(query.Limit)
        };
    }

    public static Dictionary<string, string?> BuildDeviceClientStateQueryParameters(CloudAiReadQuery query)
    {
        RejectUnsupportedFilters(
            query,
            "Cloud /device-client-states 当前不支持按状态、IP、版本或时间过滤，这些条件不能降级为 keyword。",
            "primaryIp",
            "hostVersion",
            "hostApiVersion",
            "runtimeStatus",
            "softwareStatus",
            "status",
            "updatedAt",
            "updatedAtUtc",
            "lastRuntimeHeartbeatAtUtc");
        RejectUnknownFilters(query, "Cloud /device-client-states 收到未批准的过滤字段。", "deviceId", "deviceCode", "clientCode", "processId", "keyword", "deviceName");
        return new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["deviceId"] = GetOptionalGuidFilterValue(query, "deviceId"),
            ["deviceCode"] = GetFilterValue(query, "deviceCode", "clientCode"),
            ["processId"] = GetOptionalGuidFilterValue(query, "processId"),
            ["keyword"] = GetFilterValue(query, "keyword", "deviceName"),
            ["maxRows"] = FormatMaxRows(query.Limit)
        };
    }

    public static Dictionary<string, string?> BuildCapacityQueryParameters(CloudAiReadQuery query)
    {
        RejectUnsupportedFilters(
            query,
            "Cloud /capacity/summary 不支持设备编码、工序名称或工位名称过滤；设备编码必须先唯一解析为 deviceId。",
            "deviceCode",
            "clientCode",
            "processName",
            "stationName");
        RejectUnknownFilters(query, "Cloud /capacity/summary 收到未批准的过滤字段。", "deviceId", "plcName", "shiftDate");
        var deviceId = RequireGuidFilterValue(query, "deviceId", "请补充设备 ID。", "deviceId");
        var (start, end) = RequireTimeRange(query, "请补充产能查询的开始日期和结束日期。");

        var parameters = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["deviceId"] = deviceId,
            ["startDate"] = FormatCloudDate(start),
            ["endDate"] = FormatCloudDate(end),
            ["plcName"] = GetFilterValue(query, "plcName"),
            ["maxRows"] = FormatMaxRows(query.Limit)
        };

        return parameters;
    }

    public static Dictionary<string, string?> BuildDeviceLogQueryParameters(CloudAiReadQuery query)
    {
        RejectUnsupportedFilters(
            query,
            "Cloud /device-logs 不支持设备编码、工序、日志来源或设备名称过滤；设备编码必须先唯一解析为 deviceId。",
            "deviceCode",
            "clientCode",
            "deviceName",
            "processName",
            "source");
        RejectUnknownFilters(query, "Cloud /device-logs 收到未批准的过滤字段。", "deviceId", "preset", "level", "minLevel", "keyword", "message");
        var deviceId = RequireGuidFilterValue(query, "deviceId", "请补充设备 ID。", "deviceId");
        var (start, end, preset) = ResolveTimeRangeOrPreset(
            query,
            "请补充日志查询的开始时间和结束时间，或使用 preset。",
            TimePresetValues);
        var level = GetFilterValue(query, "level");
        var minLevel = GetFilterValue(query, "minLevel");
        if (!string.IsNullOrWhiteSpace(level) && !string.IsNullOrWhiteSpace(minLevel))
        {
            throw new CloudAiReadException(
                CloudAiReadProblemCodes.MissingRequiredParameter,
                "Cloud AiRead 查询参数 level 和 minLevel 不能同时传，请只保留一种日志级别条件。");
        }

        var parameters = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["deviceId"] = deviceId,
            ["startTime"] = start.HasValue ? FormatCloudTime(start.Value) : null,
            ["endTime"] = end.HasValue ? FormatCloudTime(end.Value) : null,
            ["preset"] = preset,
            ["level"] = level,
            ["minLevel"] = minLevel,
            ["keyword"] = GetFilterValue(query, "keyword", "message"),
            ["maxRows"] = FormatMaxRows(query.Limit)
        };

        return parameters;
    }

    public static Dictionary<string, string?> BuildCapacityHourlyQueryParameters(CloudAiReadQuery query)
    {
        RejectUnsupportedFilters(
            query,
            "Cloud /capacity/hourly 不支持设备编码、工序名称或工位名称过滤；设备编码必须先唯一解析为 deviceId。",
            "deviceCode",
            "clientCode",
            "processName",
            "stationName");
        RejectUnknownFilters(query, "Cloud /capacity/hourly 收到未批准的过滤字段。", "deviceId", "date", "shiftDate", "preset", "plcName");
        var deviceId = RequireGuidFilterValue(query, "deviceId", "请补充设备 ID。", "deviceId");
        var date = GetFilterValue(query, "date", "shiftDate");
        var preset = GetFilterValue(query, "preset");
        if (!string.IsNullOrWhiteSpace(date) && !string.IsNullOrWhiteSpace(preset))
        {
            throw new CloudAiReadException(
                CloudAiReadProblemCodes.MissingRequiredParameter,
                "Cloud AiRead 查询参数 date 和 preset 不能同时传，请只保留一种小时产能时间条件。");
        }

        if (string.IsNullOrWhiteSpace(date) && string.IsNullOrWhiteSpace(preset))
        {
            throw new CloudAiReadException(
                CloudAiReadProblemCodes.MissingRequiredParameter,
                "Cloud AiRead 查询缺少必需参数 date 或 preset，请补充小时产能查询日期。");
        }

        if (!string.IsNullOrWhiteSpace(preset) && !CapacityHourlyPresetValues.Contains(preset))
        {
            throw new CloudAiReadException(
                CloudAiReadProblemCodes.MissingRequiredParameter,
                "Cloud AiRead 查询参数 preset 只支持 last_24h、today、yesterday。");
        }

        return new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["deviceId"] = deviceId,
            ["date"] = string.IsNullOrWhiteSpace(preset) ? date : null,
            ["preset"] = preset,
            ["plcName"] = GetFilterValue(query, "plcName"),
            ["maxRows"] = FormatMaxRows(query.Limit)
        };
    }

    public static Dictionary<string, string?> BuildProductionRecordQueryParameters(CloudAiReadQuery query)
    {
        RejectUnsupportedFilters(
            query,
            "Cloud /production-records 不支持设备编码、工序名称、工位名称或类型名称过滤；只能使用正式 typeKey/processId/deviceId 范围。",
            "deviceCode",
            "clientCode",
            "processName",
            "stationName",
            "typeName");
        RejectUnknownFilters(query, "Cloud /production-records 收到未批准的过滤字段。", "typeKey", "processId", "deviceId", "preset", "barcode", "result", "fieldMode");
        var (start, end, preset) = ResolveTimeRangeOrPreset(
            query,
            "请补充生产数据查询的开始时间和结束时间，或使用 preset。",
            TimePresetValues);
        var typeKey = GetFilterValue(query, "typeKey");
        var processId = GetOptionalGuidFilterValue(query, "processId");
        var deviceId = GetOptionalGuidFilterValue(query, "deviceId");
        if (string.IsNullOrWhiteSpace(typeKey) &&
            string.IsNullOrWhiteSpace(processId) &&
            string.IsNullOrWhiteSpace(deviceId))
        {
            throw new CloudAiReadException(
                CloudAiReadProblemCodes.MissingRequiredParameter,
                "Cloud AiRead 查询缺少必需参数 typeKey、processId 或 deviceId，请补充生产数据查询范围。");
        }

        var fieldMode = (GetFilterValue(query, "fieldMode") ?? "list").Trim().ToLowerInvariant();
        if (fieldMode is not ("list" or "full"))
        {
            throw new CloudAiReadException(
                CloudAiReadProblemCodes.MissingRequiredParameter,
                "Cloud AiRead 查询参数 fieldMode 只支持 list 或 full。");
        }

        var parameters = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["typeKey"] = typeKey,
            ["processId"] = processId,
            ["deviceId"] = deviceId,
            ["startTime"] = start.HasValue ? FormatCloudTime(start.Value) : null,
            ["endTime"] = end.HasValue ? FormatCloudTime(end.Value) : null,
            ["preset"] = preset,
            ["barcode"] = GetFilterValue(query, "barcode"),
            ["result"] = GetFilterValue(query, "result"),
            ["fieldMode"] = fieldMode,
            ["maxRows"] = FormatMaxRows(query.Limit)
        };

        return parameters;
    }

    private static string FormatMaxRows(int limit)
    {
        return Math.Max(1, limit).ToString(CultureInfo.InvariantCulture);
    }

    private static string? NormalizeBoolean(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (bool.TryParse(value, out var parsed))
        {
            return parsed ? "true" : "false";
        }

        return value.Trim() switch
        {
            "1" => "true",
            "0" => "false",
            _ => value.Trim()
        };
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

    private static string RequireGuidFilterValue(
        CloudAiReadQuery query,
        string parameterName,
        string guidance,
        params string[] fieldNames)
    {
        var value = RequireFilterValue(query, parameterName, guidance, fieldNames);
        return ValidateGuid(parameterName, value);
    }

    private static string? GetOptionalGuidFilterValue(CloudAiReadQuery query, params string[] fieldNames)
    {
        var value = GetFilterValue(query, fieldNames);
        return string.IsNullOrWhiteSpace(value)
            ? null
            : ValidateGuid(fieldNames[0], value);
    }

    private static string ValidateGuid(string parameterName, string value)
    {
        if (Guid.TryParse(value, out var parsed) && parsed != Guid.Empty)
        {
            return parsed.ToString("D");
        }

        throw new CloudAiReadException(
            CloudAiReadProblemCodes.MissingRequiredParameter,
            $"Cloud AiRead 查询参数 {parameterName} 必须是非空 GUID。");
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

    private static (DateTimeOffset? Start, DateTimeOffset? End, string? Preset) ResolveTimeRangeOrPreset(
        CloudAiReadQuery query,
        string guidance,
        IReadOnlySet<string> allowedPresets)
    {
        var preset = GetFilterValue(query, "preset");
        var hasPreset = !string.IsNullOrWhiteSpace(preset);
        var hasStartOrEnd = query.TimeRange?.Start is not null || query.TimeRange?.End is not null;
        if (hasPreset && hasStartOrEnd)
        {
            throw new CloudAiReadException(
                CloudAiReadProblemCodes.MissingRequiredParameter,
                "Cloud AiRead 查询参数 preset 和 startTime/endTime 不能同时传，请只保留一种时间条件。");
        }

        if (hasPreset)
        {
            if (!allowedPresets.Contains(preset!))
            {
                throw new CloudAiReadException(
                    CloudAiReadProblemCodes.MissingRequiredParameter,
                    $"Cloud AiRead 查询参数 preset 不支持 {preset}。");
            }

            return (null, null, preset);
        }

        var (start, end) = RequireTimeRange(query, guidance);
        return (start, end, null);
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

    private static void RejectUnsupportedFilters(
        CloudAiReadQuery query,
        string message,
        params string[] fieldNames)
    {
        if (fieldNames.Any(fieldName => !string.IsNullOrWhiteSpace(GetFilterValue(query, fieldName))))
        {
            throw new CloudAiReadException(
                CloudAiReadProblemCodes.MissingRequiredParameter,
                message);
        }
    }

    private static void RejectUnknownFilters(
        CloudAiReadQuery query,
        string message,
        params string[] allowedFieldNames)
    {
        var allowed = new HashSet<string>(allowedFieldNames, StringComparer.OrdinalIgnoreCase);
        if (query.Filters.Any(filter => !allowed.Contains(filter.Field)))
        {
            throw new CloudAiReadException(
                CloudAiReadProblemCodes.MissingRequiredParameter,
                message);
        }
    }

}
