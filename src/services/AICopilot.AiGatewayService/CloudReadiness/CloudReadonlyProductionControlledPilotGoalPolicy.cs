using System.Net.Http;
using AICopilot.Services.Contracts;

namespace AICopilot.AiGatewayService.CloudReadiness;

internal static class CloudReadonlyProductionControlledPilotGoalPolicy
{
    private static readonly IReadOnlyDictionary<string, CloudReadonlyProductionControlledEndpointSpec> EndpointSpecs =
        new[]
        {
            new CloudReadonlyProductionControlledEndpointSpec("devices", HttpMethod.Get, "/api/v1/ai/read/devices"),
            new CloudReadonlyProductionControlledEndpointSpec("capacity_summary", HttpMethod.Get, "/api/v1/ai/read/capacity/summary"),
            new CloudReadonlyProductionControlledEndpointSpec("device_logs", HttpMethod.Get, "/api/v1/ai/read/device-logs"),
            new CloudReadonlyProductionControlledEndpointSpec("pass_station_records", HttpMethod.Get, "/api/v1/ai/read/pass-stations/{typeKey}"),
            new CloudReadonlyProductionControlledEndpointSpec("write_path", HttpMethod.Post, "/api/v1/ai/read/devices/update", IsBlockedByPolicy: true)
        }.ToDictionary(item => item.Code, StringComparer.OrdinalIgnoreCase);

    public static bool TryGetEndpoint(string endpointCode, out CloudReadonlyProductionControlledEndpointSpec endpoint) =>
        EndpointSpecs.TryGetValue(endpointCode, out endpoint!);

    public static string[] FilterAllowedEndpointCodes(IEnumerable<string>? endpointCodes)
    {
        return (endpointCodes ?? [])
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .Select(code => code.Trim())
            .Where(code => EndpointSpecs.TryGetValue(code, out var spec) && !spec.IsBlockedByPolicy)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static bool RequiresDeviceId(string endpointCode) =>
        endpointCode is "capacity_summary" or "device_logs" or "pass_station_records";

    public static bool TryResolveEndpointPath(
        CloudReadonlyProductionControlledEndpointSpec endpoint,
        string? passStationTypeKey,
        out string path,
        out string error)
    {
        path = endpoint.Path;
        error = string.Empty;

        if (!endpoint.Path.Contains("{typeKey}", StringComparison.Ordinal))
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(passStationTypeKey) ||
            !CloudAiReadEndpointPolicy.IsSafeRouteSegment(passStationTypeKey))
        {
            error = "P13 pass_station_records intent requires a safe passStationTypeKey.";
            path = string.Empty;
            return false;
        }

        path = endpoint.Path.Replace("{typeKey}", Uri.EscapeDataString(passStationTypeKey.Trim()), StringComparison.Ordinal);
        return true;
    }

    public static string NormalizeGoal(string? goal) =>
        string.Join(' ', (goal ?? string.Empty).Split(default(string[]), StringSplitOptions.RemoveEmptyEntries));

    public static bool ContainsBlockedGoalTerm(string goal)
    {
        var terms = new[]
        {
            "recipe",
            "配方",
            "版本历史",
            "recipe version",
            "write",
            "update",
            "delete",
            "drop",
            "insert",
            "sql",
            "payload",
            "写入",
            "创建",
            "更新",
            "删除",
            "任意",
            "完整 payload"
        };
        return terms.Any(term => goal.Contains(term, StringComparison.OrdinalIgnoreCase));
    }

    public static string? ResolveEndpointCode(string goal)
    {
        if (goal.Contains("过站", StringComparison.OrdinalIgnoreCase) ||
            goal.Contains("pass", StringComparison.OrdinalIgnoreCase))
        {
            return "pass_station_records";
        }

        if (goal.Contains("日志", StringComparison.OrdinalIgnoreCase) ||
            goal.Contains("异常", StringComparison.OrdinalIgnoreCase) ||
            goal.Contains("告警", StringComparison.OrdinalIgnoreCase) ||
            goal.Contains("停机", StringComparison.OrdinalIgnoreCase) ||
            goal.Contains("log", StringComparison.OrdinalIgnoreCase) ||
            goal.Contains("alarm", StringComparison.OrdinalIgnoreCase) ||
            goal.Contains("exception", StringComparison.OrdinalIgnoreCase))
        {
            return "device_logs";
        }

        if (goal.Contains("产能", StringComparison.OrdinalIgnoreCase) ||
            goal.Contains("交付", StringComparison.OrdinalIgnoreCase) ||
            goal.Contains("capacity", StringComparison.OrdinalIgnoreCase) ||
            goal.Contains("delivery", StringComparison.OrdinalIgnoreCase))
        {
            return "capacity_summary";
        }

        if (goal.Contains("设备", StringComparison.OrdinalIgnoreCase) ||
            goal.Contains("device", StringComparison.OrdinalIgnoreCase))
        {
            return "devices";
        }

        return null;
    }

    public static string ResolveAnalysisType(string goal, string? endpointCode)
    {
        if (endpointCode == "device_logs" &&
            (goal.Contains("exception", StringComparison.OrdinalIgnoreCase) ||
             goal.Contains("alarm", StringComparison.OrdinalIgnoreCase)))
        {
            return "DeviceExceptionAnalysis";
        }

        if (endpointCode == "capacity_summary" &&
            goal.Contains("delivery", StringComparison.OrdinalIgnoreCase))
        {
            return "CapacityDeliveryAnalysis";
        }
        if (endpointCode == "device_logs" && goal.Contains("异常", StringComparison.OrdinalIgnoreCase))
        {
            return "DeviceExceptionAnalysis";
        }

        if (endpointCode == "capacity_summary" && goal.Contains("交付", StringComparison.OrdinalIgnoreCase))
        {
            return "CapacityDeliveryAnalysis";
        }

        return endpointCode switch
        {
            "devices" => "DeviceList",
            "capacity_summary" => "CapacitySummary",
            "device_logs" => "DeviceLogs",
            "pass_station_records" => "PassStationRecords",
            _ => "Unknown"
        };
    }

    public static IReadOnlyCollection<string> NormalizeArtifactTypes(
        IReadOnlyCollection<string>? requested,
        CloudReadonlyProductionControlledPilotOptions options,
        ICollection<string> rejected)
    {
        var allowed = new HashSet<string>(options.AllowedArtifactTypes, StringComparer.OrdinalIgnoreCase);
        var values = requested is { Count: > 0 } ? requested : ["Markdown", "Html"];
        var normalized = new List<string>();
        foreach (var item in values)
        {
            if (string.IsNullOrWhiteSpace(item))
            {
                continue;
            }

            var trimmed = item.Trim();
            if (!allowed.Contains(trimmed))
            {
                rejected.Add($"Artifact type '{trimmed}' is not allowed in CloudReadonlyProductionControlledPilot.");
                continue;
            }

            normalized.Add(trimmed);
        }

        return normalized.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public static CloudProductionGoalTimeRangeDto NormalizeTimeRange(
        CloudProductionGoalTimeRangeDto? requested,
        CloudReadonlyProductionControlledPilotOptions options,
        ICollection<string> rejected,
        ICollection<string> warnings)
    {
        var now = DateTimeOffset.UtcNow;
        var from = requested?.From ?? now.AddDays(-Math.Min(options.MaxTimeRangeDays, 1));
        var to = requested?.To ?? now;

        if (from > to)
        {
            rejected.Add("timeRange.from must be earlier than timeRange.to.");
        }

        if (to - from > TimeSpan.FromDays(options.MaxTimeRangeDays))
        {
            rejected.Add($"timeRange cannot exceed {options.MaxTimeRangeDays} days.");
        }

        if (requested is null)
        {
            warnings.Add("timeRange was not provided; defaulted to the last day.");
        }

        return new CloudProductionGoalTimeRangeDto(from, to);
    }
}

internal sealed record CloudReadonlyProductionControlledEndpointSpec(
    string Code,
    HttpMethod Method,
    string Path,
    bool IsBlockedByPolicy = false);
