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
        query = CloudAiReadSemanticSchemaRegistry.NormalizeQuery(CloudAiReadOperation.Device, query);
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
        query = CloudAiReadSemanticSchemaRegistry.NormalizeQuery(CloudAiReadOperation.Process, query);
        return new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["processId"] = GetOptionalGuidFilterValue(query, "processId"),
            ["keyword"] = GetFilterValue(query, "keyword", "processCode", "processName"),
            ["maxRows"] = FormatMaxRows(query.Limit)
        };
    }

    public static Dictionary<string, string?> BuildClientReleaseQueryParameters(CloudAiReadQuery query)
    {
        query = CloudAiReadSemanticSchemaRegistry.NormalizeQuery(CloudAiReadOperation.ClientRelease, query);
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
        query = CloudAiReadSemanticSchemaRegistry.NormalizeQuery(CloudAiReadOperation.DeviceClientState, query);
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
        query = CloudAiReadSemanticSchemaRegistry.NormalizeQuery(CloudAiReadOperation.CapacitySummary, query);
        var deviceId = RequireGuidFilterValue(query, "deviceId", "请补充设备 ID。", "deviceId");
        var (start, end) = RequireTimeRangeOrShiftDate(query, "请补充产能查询的开始日期和结束日期。");

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
        query = CloudAiReadSemanticSchemaRegistry.NormalizeQuery(CloudAiReadOperation.DeviceLog, query);
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
        query = CloudAiReadSemanticSchemaRegistry.NormalizeQuery(CloudAiReadOperation.CapacityHourly, query);
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
        query = CloudAiReadSemanticSchemaRegistry.NormalizeQuery(CloudAiReadOperation.ProductionRecord, query);
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
            ["plcCode"] = GetFilterValue(query, "plcCode"),
            ["plcName"] = GetFilterValue(query, "plcName"),
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
        return CloudAiReadRowLimitPolicy.Normalize(limit).ToString(CultureInfo.InvariantCulture);
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

    private static (DateTimeOffset Start, DateTimeOffset End) RequireTimeRangeOrShiftDate(
        CloudAiReadQuery query,
        string guidance)
    {
        if (query.TimeRange?.Start is { } start && query.TimeRange.End is { } end)
        {
            return (start, end);
        }

        var shiftDate = GetFilterValue(query, "shiftDate");
        if (DateOnly.TryParseExact(
                shiftDate,
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var date))
        {
            var startUtc = new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
            return (startUtc, startUtc.AddDays(1).AddTicks(-1));
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

}
