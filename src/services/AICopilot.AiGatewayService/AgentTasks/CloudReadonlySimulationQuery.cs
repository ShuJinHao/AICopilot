using System.Text.Json;
using System.Text.RegularExpressions;

namespace AICopilot.AiGatewayService.AgentTasks;

internal sealed record CloudReadonlySimulationQuery(
    string? LineName,
    string? DeviceCode,
    string? Level,
    string? Shift,
    string? ProductCode,
    string? DefectType,
    string? Status,
    int? Days,
    DateTimeOffset? StartAt,
    DateTimeOffset? EndAt,
    int? Limit,
    string? RawText)
{
    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false
    };

    public static CloudReadonlySimulationQuery Parse(string? query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return FromText(string.Empty);
        }

        try
        {
            using var document = JsonDocument.Parse(query);
            if (document.RootElement.ValueKind == JsonValueKind.Object)
            {
                return new CloudReadonlySimulationQuery(
                    GetString(document.RootElement, "lineName") ?? ExtractLineName(query),
                    GetString(document.RootElement, "deviceCode"),
                    GetString(document.RootElement, "level"),
                    GetString(document.RootElement, "shift"),
                    GetString(document.RootElement, "productCode"),
                    GetString(document.RootElement, "defectType"),
                    GetString(document.RootElement, "status"),
                    GetInt(document.RootElement, "days") ?? ExtractDays(query),
                    GetDate(document.RootElement, "startAt") ?? GetDate(document.RootElement, "dateStart"),
                    GetDate(document.RootElement, "endAt") ?? GetDate(document.RootElement, "dateEnd"),
                    GetInt(document.RootElement, "limit"),
                    GetString(document.RootElement, "rawText") ?? query);
            }
        }
        catch (JsonException)
        {
            // Free text is allowed for Simulation queries; it is parsed below.
        }

        return FromText(query);
    }

    public static CloudReadonlySimulationQuery FromText(string text)
    {
        return new CloudReadonlySimulationQuery(
            ExtractLineName(text),
            ExtractToken(text, @"\bDEV-[A-Z0-9-]+\b"),
            ExtractLevel(text),
            ExtractShift(text),
            ExtractToken(text, @"\bP[A-Z0-9-]{2,}\b"),
            null,
            ExtractStatus(text),
            ExtractDays(text),
            null,
            null,
            ExtractLimit(text),
            text);
    }

    private static string? GetString(JsonElement element, string name)
    {
        return element.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
    }

    private static int? GetInt(JsonElement element, string name)
    {
        return element.TryGetProperty(name, out var property) &&
               property.ValueKind == JsonValueKind.Number &&
               property.TryGetInt32(out var value)
            ? value
            : null;
    }

    private static DateTimeOffset? GetDate(JsonElement element, string name)
    {
        return element.TryGetProperty(name, out var property) &&
               property.ValueKind == JsonValueKind.String &&
               property.TryGetDateTimeOffset(out var value)
            ? value
            : null;
    }

    private static string? ExtractLineName(string text)
    {
        return ExtractToken(text, @"\bLINE-[A-Z0-9-]+\b");
    }

    private static int? ExtractDays(string text)
    {
        var match = Regex.Match(text, @"(?<days>\d+)\s*(天|day|days)", RegexOptions.IgnoreCase);
        return match.Success && int.TryParse(match.Groups["days"].Value, out var days)
            ? Math.Clamp(days, 1, 31)
            : 7;
    }

    private static int? ExtractLimit(string text)
    {
        var match = Regex.Match(text, @"(?i)(limit|top)\s*[:=]?\s*(?<limit>\d+)");
        return match.Success && int.TryParse(match.Groups["limit"].Value, out var limit)
            ? Math.Clamp(limit, 1, 200)
            : null;
    }

    private static string? ExtractLevel(string text)
    {
        foreach (var level in new[] { "Error", "Warning", "Info" })
        {
            if (text.Contains(level, StringComparison.OrdinalIgnoreCase))
            {
                return level;
            }
        }

        if (text.Contains("错误", StringComparison.Ordinal) || text.Contains("告警", StringComparison.Ordinal))
        {
            return "Error";
        }

        return null;
    }

    private static string? ExtractShift(string text)
    {
        if (text.Contains("夜班", StringComparison.OrdinalIgnoreCase) || text.Contains("night", StringComparison.OrdinalIgnoreCase))
        {
            return "Night";
        }

        if (text.Contains("白班", StringComparison.OrdinalIgnoreCase) || text.Contains("day", StringComparison.OrdinalIgnoreCase))
        {
            return "Day";
        }

        return null;
    }

    private static string? ExtractStatus(string text)
    {
        foreach (var status in new[] { "Running", "Idle", "Maintenance", "Offline", "Open", "InProgress", "Closed" })
        {
            if (text.Contains(status, StringComparison.OrdinalIgnoreCase))
            {
                return status;
            }
        }

        return null;
    }

    private static string? ExtractToken(string text, string pattern)
    {
        var match = Regex.Match(text, pattern, RegexOptions.IgnoreCase);
        return match.Success ? match.Value.ToUpperInvariant() : null;
    }
}
