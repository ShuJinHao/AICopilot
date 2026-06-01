using System.Globalization;
using AICopilot.Services.Contracts;

namespace AICopilot.Infrastructure.CloudRead;

internal static class CloudAiReadQueryParameterBuilder
{
    public static Dictionary<string, string?> BuildDeviceQueryParameters(CloudAiReadQuery query)
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

    public static Dictionary<string, string?> BuildCapacityQueryParameters(CloudAiReadQuery query)
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

    public static Dictionary<string, string?> BuildDeviceLogQueryParameters(CloudAiReadQuery query)
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

    public static Dictionary<string, string?> BuildPassStationQueryParameters(CloudAiReadQuery query)
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
}
