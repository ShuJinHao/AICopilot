using System.Net.Http;
using AICopilot.Services.Contracts;

namespace AICopilot.AiGatewayService.CloudReadiness;

internal static class CloudReadonlySandboxControlledTrialGoalPolicy
{
    private static readonly IReadOnlyDictionary<string, CloudReadonlySandboxControlledEndpointSpec> EndpointSpecs =
        new[]
        {
            new CloudReadonlySandboxControlledEndpointSpec("devices", HttpMethod.Get, "/api/v1/ai/read/devices"),
            new CloudReadonlySandboxControlledEndpointSpec("capacity_summary", HttpMethod.Get, "/api/v1/ai/read/capacity/summary"),
            new CloudReadonlySandboxControlledEndpointSpec("device_logs", HttpMethod.Get, "/api/v1/ai/read/device-logs"),
            new CloudReadonlySandboxControlledEndpointSpec("pass_station_records", HttpMethod.Get, "/api/v1/ai/read/pass-stations/default"),
            new CloudReadonlySandboxControlledEndpointSpec("recipe", HttpMethod.Get, "/api/v1/ai/read/recipes", IsBlockedByPolicy: true),
            new CloudReadonlySandboxControlledEndpointSpec("recipe_versions", HttpMethod.Get, "/api/v1/ai/read/recipes/versions", IsBlockedByPolicy: true),
            new CloudReadonlySandboxControlledEndpointSpec("write_path", HttpMethod.Post, "/api/v1/ai/read/devices/update", IsBlockedByPolicy: true)
        }.ToDictionary(item => item.Code, StringComparer.OrdinalIgnoreCase);

    public static bool TryGetEndpoint(string endpointCode, out CloudReadonlySandboxControlledEndpointSpec endpoint) =>
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
            "写入",
            "创建",
            "更新",
            "删除",
            "生产读取",
            "production cloud",
            "real cloud",
            "prod cloud"
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
        CloudReadonlySandboxControlledTrialOptions options,
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
                rejected.Add($"Artifact type '{trimmed}' is not allowed in CloudReadonlySandboxControlledTrial.");
                continue;
            }

            normalized.Add(trimmed);
        }

        return normalized
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static CloudSandboxGoalTimeRangeDto NormalizeTimeRange(
        CloudSandboxGoalTimeRangeDto? requested,
        CloudReadonlySandboxControlledTrialOptions options,
        ICollection<string> rejected,
        ICollection<string> warnings)
    {
        var now = DateTimeOffset.UtcNow;
        var from = requested?.From ?? now.AddDays(-7);
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
            warnings.Add("timeRange was not provided; defaulted to the last 7 days.");
        }

        return new CloudSandboxGoalTimeRangeDto(from, to);
    }
}

internal sealed record CloudReadonlySandboxControlledEndpointSpec(
    string Code,
    HttpMethod Method,
    string Path,
    bool IsBlockedByPolicy = false);
