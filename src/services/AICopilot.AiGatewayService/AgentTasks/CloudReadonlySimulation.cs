using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Result;
using Microsoft.Extensions.Options;

namespace AICopilot.AiGatewayService.AgentTasks;

internal sealed class CloudReadonlySimulationIntentPlanner : ICloudReadonlySimulationIntentPlanner
{
    public Result<CloudReadonlyAgentPlanIntent> CreateIntent(string goal)
    {
        var query = CloudReadonlySimulationQuery.FromText(goal);
        var intent = ResolveIntent(goal);
        var (target, kind) = ResolveTargetKind(intent);
        var queryJson = JsonSerializer.Serialize(query with { RawText = goal }, CloudReadonlySimulationQuery.JsonOptions);
        return Result.Success(new CloudReadonlyAgentPlanIntent(
            intent,
            queryJson,
            0.95,
            target,
            kind,
            $"sourceMode=Simulation; target={target}; kind={kind}; lineName={query.LineName ?? "ALL"}; days={query.Days ?? 7}; limit={query.Limit ?? 20}"));
    }

    private static string ResolveIntent(string goal)
    {
        if (ContainsAny(goal, "周报", "weekly", "report", "产线运行"))
        {
            return "Analysis.Line.WeeklyReport";
        }

        if (ContainsAny(goal, "质量", "缺陷", "defect", "quality"))
        {
            return "Analysis.Quality.Defect";
        }

        if (ContainsAny(goal, "工单", "维修", "维护", "maintenance", "work order"))
        {
            return "Analysis.WorkOrder.Maintenance";
        }

        if (ContainsAny(goal, "产能", "趋势", "capacity", "trend"))
        {
            return "Analysis.Capacity.Trend";
        }

        if (ContainsAny(goal, "日志", "告警", "报警", "log", "alarm"))
        {
            return "Analysis.DeviceLog.Recent";
        }

        return "Analysis.Device.Status";
    }

    private static (string Target, string Kind) ResolveTargetKind(string intent)
    {
        return intent switch
        {
            "Analysis.Line.WeeklyReport" => ("Line", "WeeklyReport"),
            "Analysis.Quality.Defect" => ("Quality", "Defect"),
            "Analysis.WorkOrder.Maintenance" => ("WorkOrder", "Maintenance"),
            "Analysis.Capacity.Trend" => ("Capacity", "Trend"),
            "Analysis.DeviceLog.Recent" => ("DeviceLog", "Recent"),
            _ => ("Device", "Status")
        };
    }

    private static bool ContainsAny(string value, params string[] tokens)
    {
        return tokens.Any(token => value.Contains(token, StringComparison.OrdinalIgnoreCase));
    }
}

internal sealed class SimulationCloudReadonlyDataProvider(
    CloudReadonlySimulationDataSet dataSet,
    IOptions<CloudReadonlyOptions> options) : ICloudReadonlyDataProvider
{
    public CloudReadonlyDataSourceMode Mode => CloudReadonlyDataSourceMode.Simulation;

    public Task<CloudReadonlyAgentToolResult> QueryAsync(
        CloudReadonlyAgentToolRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!options.Value.Simulation.Enabled)
        {
            throw new CloudAiReadException(
                CloudAiReadProblemCodes.NotConfigured,
                "CloudReadonly Simulation mode requires CloudReadonly:Simulation:Enabled=true.");
        }

        var query = CloudReadonlySimulationQuery.Parse(request.Query);
        var result = Execute(request.Intent, query);
        return Task.FromResult(result);
    }

    private CloudReadonlyAgentToolResult Execute(string intent, CloudReadonlySimulationQuery query)
    {
        var normalizedIntent = NormalizeIntent(intent);
        var (target, kind) = ResolveTargetKind(normalizedIntent);
        var rows = normalizedIntent switch
        {
            "Analysis.Device.Status" => QueryDevices(query),
            "Analysis.DeviceLog.Recent" => QueryDeviceLogs(query),
            "Analysis.Capacity.Trend" => QueryCapacityTrend(query),
            "Analysis.Quality.Defect" => QueryQualityDefects(query),
            "Analysis.WorkOrder.Maintenance" => QueryMaintenanceWorkOrders(query),
            "Analysis.Line.WeeklyReport" => QueryWeeklyReport(query),
            _ => throw new CloudAiReadException(
                AppProblemCodes.CloudReadonlyIntentUnsupported,
                $"Simulation intent '{intent}' is not supported.")
        };

        var limit = CloudReadonlyAgentToolResultBuilder.ResolveLimit(query, 20);
        var limitedRows = rows
            .Take(limit)
            .Select(row => CloudReadonlyAgentToolResultBuilder.NormalizeRow(
                row,
                CloudReadonlySourceMarkers.SimulationSourceMode,
                isSimulation: true,
                CloudReadonlySourceMarkers.SimulationSourceLabel))
            .ToArray();
        var isTruncated = rows.Count > limitedRows.Length;
        var sourcePath = $"simulation://cloud-readonly/manufacturing-demo/{normalizedIntent}";
        var summary = CloudReadonlyAgentToolResultBuilder.BuildSummary(
            normalizedIntent,
            target,
            kind,
            sourcePath,
            CloudReadonlySourceMarkers.SimulationSourceLabel,
            CloudReadonlySourceMarkers.SimulationSourceMode,
            true,
            isTruncated,
            rows.Count,
            limitedRows);

        return new CloudReadonlyAgentToolResult(
            "completed",
            normalizedIntent,
            target,
            kind,
            sourcePath,
            CloudReadonlySourceMarkers.SimulationSourceLabel,
            CloudReadonlySourceMarkers.SimulationSourceMode,
            true,
            DateTimeOffset.UtcNow,
            limit,
            isTruncated,
            rows.Count,
            limitedRows,
            summary);
    }

    private IReadOnlyList<Dictionary<string, object?>> QueryDevices(CloudReadonlySimulationQuery query)
    {
        return dataSet.Devices
            .Where(device => MatchesLine(device.LineName, query.LineName))
            .Where(device => MatchesValue(device.DeviceCode, query.DeviceCode))
            .Where(device => MatchesValue(device.Status, query.Status))
            .OrderBy(device => device.LineName)
            .ThenBy(device => device.DeviceCode)
            .Select(device => new Dictionary<string, object?>
            {
                ["deviceCode"] = device.DeviceCode,
                ["deviceName"] = device.DeviceName,
                ["lineName"] = device.LineName,
                ["processName"] = device.ProcessName,
                ["status"] = device.Status,
                ["updatedAt"] = device.UpdatedAt
            })
            .ToArray();
    }

    private IReadOnlyList<Dictionary<string, object?>> QueryDeviceLogs(CloudReadonlySimulationQuery query)
    {
        return dataSet.DeviceLogs
            .Where(log => MatchesLine(log.LineName, query.LineName))
            .Where(log => MatchesValue(log.DeviceCode, query.DeviceCode))
            .Where(log => MatchesValue(log.Level, query.Level))
            .Where(log => IsInRange(log.OccurredAt, query))
            .OrderByDescending(log => log.OccurredAt)
            .Select(log => new Dictionary<string, object?>
            {
                ["logId"] = log.LogId,
                ["deviceCode"] = log.DeviceCode,
                ["lineName"] = log.LineName,
                ["level"] = log.Level,
                ["message"] = log.Message,
                ["source"] = log.Source,
                ["occurredAt"] = log.OccurredAt
            })
            .ToArray();
    }

    private IReadOnlyList<Dictionary<string, object?>> QueryCapacityTrend(CloudReadonlySimulationQuery query)
    {
        return dataSet.CapacityRecords
            .Where(record => MatchesLine(record.LineName, query.LineName))
            .Where(record => MatchesValue(record.DeviceCode, query.DeviceCode))
            .Where(record => MatchesValue(record.Shift, query.Shift))
            .Where(record => MatchesValue(record.ProductCode, query.ProductCode))
            .Where(record => IsInRange(record.OccurredAt, query))
            .GroupBy(record => new { record.ShiftDate, record.LineName })
            .OrderBy(group => group.Key.ShiftDate)
            .ThenBy(group => group.Key.LineName)
            .Select(group =>
            {
                var output = group.Sum(item => item.OutputQty);
                var qualified = group.Sum(item => item.QualifiedQty);
                return new Dictionary<string, object?>
                {
                    ["shiftDate"] = group.Key.ShiftDate,
                    ["lineName"] = group.Key.LineName,
                    ["outputQty"] = output,
                    ["qualifiedQty"] = qualified,
                    ["qualityRate"] = output == 0 ? 0 : Math.Round((decimal)qualified / output, 4),
                    ["recordCount"] = group.Count()
                };
            })
            .ToArray();
    }

    private IReadOnlyList<Dictionary<string, object?>> QueryQualityDefects(CloudReadonlySimulationQuery query)
    {
        return dataSet.QualityRecords
            .Where(record => MatchesLine(record.LineName, query.LineName))
            .Where(record => MatchesValue(record.DeviceCode, query.DeviceCode))
            .Where(record => MatchesValue(record.ProductCode, query.ProductCode))
            .Where(record => MatchesValue(record.DefectType, query.DefectType))
            .Where(record => IsInRange(record.OccurredAt, query))
            .OrderByDescending(record => record.DefectQty)
            .ThenByDescending(record => record.OccurredAt)
            .Select(record => new Dictionary<string, object?>
            {
                ["recordId"] = record.RecordId,
                ["lineName"] = record.LineName,
                ["deviceCode"] = record.DeviceCode,
                ["productCode"] = record.ProductCode,
                ["defectType"] = record.DefectType,
                ["defectQty"] = record.DefectQty,
                ["sampleQty"] = record.SampleQty,
                ["defectRate"] = Math.Round((decimal)record.DefectQty / record.SampleQty, 4),
                ["occurredAt"] = record.OccurredAt
            })
            .ToArray();
    }

    private IReadOnlyList<Dictionary<string, object?>> QueryMaintenanceWorkOrders(CloudReadonlySimulationQuery query)
    {
        return dataSet.WorkOrders
            .Where(order => MatchesLine(order.LineName, query.LineName))
            .Where(order => MatchesValue(order.DeviceCode, query.DeviceCode))
            .Where(order => MatchesValue(order.Status, query.Status))
            .Where(order => IsInRange(order.CreatedAt, query))
            .OrderByDescending(order => order.CreatedAt)
            .Select(order => new Dictionary<string, object?>
            {
                ["workOrderNo"] = order.WorkOrderNo,
                ["lineName"] = order.LineName,
                ["deviceCode"] = order.DeviceCode,
                ["category"] = order.Category,
                ["status"] = order.Status,
                ["priority"] = order.Priority,
                ["summary"] = order.Summary,
                ["createdAt"] = order.CreatedAt,
                ["closedAt"] = order.ClosedAt
            })
            .ToArray();
    }

    private IReadOnlyList<Dictionary<string, object?>> QueryWeeklyReport(CloudReadonlySimulationQuery query)
    {
        var capacity = dataSet.CapacityRecords
            .Where(record => MatchesLine(record.LineName, query.LineName))
            .Where(record => IsInRange(record.OccurredAt, query))
            .GroupBy(record => new { record.ShiftDate, record.LineName })
            .ToDictionary(group => (group.Key.ShiftDate, group.Key.LineName), group => new
            {
                Output = group.Sum(item => item.OutputQty),
                Qualified = group.Sum(item => item.QualifiedQty)
            });
        var defects = dataSet.QualityRecords
            .Where(record => MatchesLine(record.LineName, query.LineName))
            .Where(record => IsInRange(record.OccurredAt, query))
            .GroupBy(record => new { ShiftDate = record.OccurredAt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), record.LineName })
            .ToDictionary(group => (group.Key.ShiftDate, group.Key.LineName), group => group.Sum(item => item.DefectQty));
        var orders = dataSet.WorkOrders
            .Where(order => MatchesLine(order.LineName, query.LineName))
            .Where(order => IsInRange(order.CreatedAt, query))
            .GroupBy(order => new { ShiftDate = order.CreatedAt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), order.LineName })
            .ToDictionary(group => (group.Key.ShiftDate, group.Key.LineName), group => group.Count(item => item.Status != "Closed"));

        return capacity
            .OrderBy(item => item.Key.ShiftDate)
            .ThenBy(item => item.Key.LineName)
            .Select(item =>
            {
                defects.TryGetValue(item.Key, out var defectQty);
                orders.TryGetValue(item.Key, out var openWorkOrders);
                return new Dictionary<string, object?>
                {
                    ["shiftDate"] = item.Key.ShiftDate,
                    ["lineName"] = item.Key.LineName,
                    ["outputQty"] = item.Value.Output,
                    ["qualifiedQty"] = item.Value.Qualified,
                    ["qualityRate"] = item.Value.Output == 0 ? 0 : Math.Round((decimal)item.Value.Qualified / item.Value.Output, 4),
                    ["defectQty"] = defectQty,
                    ["openWorkOrders"] = openWorkOrders
                };
            })
            .ToArray();
    }

    private static string NormalizeIntent(string intent)
    {
        return intent switch
        {
            "Analysis.DeviceLog.Latest" or "Analysis.DeviceLog.Range" or "Analysis.DeviceLog.ByLevel" => "Analysis.DeviceLog.Recent",
            "Analysis.Capacity.Range" or "Analysis.Capacity.ByDevice" or "Analysis.Capacity.ByProcess" => "Analysis.Capacity.Trend",
            _ => intent
        };
    }

    private static (string Target, string Kind) ResolveTargetKind(string intent)
    {
        return intent switch
        {
            "Analysis.Device.Status" => ("Device", "Status"),
            "Analysis.DeviceLog.Recent" => ("DeviceLog", "Recent"),
            "Analysis.Capacity.Trend" => ("Capacity", "Trend"),
            "Analysis.Quality.Defect" => ("Quality", "Defect"),
            "Analysis.WorkOrder.Maintenance" => ("WorkOrder", "Maintenance"),
            "Analysis.Line.WeeklyReport" => ("Line", "WeeklyReport"),
            _ => ("Unknown", "Unknown")
        };
    }

    private static bool MatchesLine(string value, string? expected)
    {
        return string.IsNullOrWhiteSpace(expected) ||
               string.Equals(value, expected, StringComparison.OrdinalIgnoreCase);
    }

    private static bool MatchesValue(string value, string? expected)
    {
        return string.IsNullOrWhiteSpace(expected) ||
               string.Equals(value, expected, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsInRange(DateTimeOffset value, CloudReadonlySimulationQuery query)
    {
        if (query.StartAt.HasValue && value < query.StartAt.Value)
        {
            return false;
        }

        if (query.EndAt.HasValue && value > query.EndAt.Value)
        {
            return false;
        }

        if (!query.StartAt.HasValue && !query.EndAt.HasValue && query.Days is > 0)
        {
            return value >= DateTimeOffset.UtcNow.AddDays(-query.Days.Value);
        }

        return true;
    }
}

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

internal sealed class CloudReadonlySimulationDataSet
{
    public IReadOnlyList<SimulationDevice> Devices { get; } = BuildDevices();

    public IReadOnlyList<SimulationDeviceLog> DeviceLogs { get; } = BuildDeviceLogs();

    public IReadOnlyList<SimulationCapacityRecord> CapacityRecords { get; } = BuildCapacityRecords();

    public IReadOnlyList<SimulationQualityRecord> QualityRecords { get; } = BuildQualityRecords();

    public IReadOnlyList<SimulationWorkOrder> WorkOrders { get; } = BuildWorkOrders();

    private static DateTimeOffset Anchor => DateTimeOffset.UtcNow.Date.AddHours(8);

    private static IReadOnlyList<SimulationDevice> BuildDevices()
    {
        var statuses = new[] { "Running", "Running", "Idle", "Maintenance", "Running", "Offline" };
        var processes = new[] { "Welding", "Assembly", "Inspection", "Packaging", "Aging" };
        return Enumerable.Range(1, 12)
            .Select(index =>
            {
                var line = index <= 6 ? "LINE-A" : "LINE-B";
                return new SimulationDevice(
                    $"DEV-{index:000}",
                    $"{line}-Device-{index:00}",
                    line,
                    processes[(index - 1) % processes.Length],
                    statuses[(index - 1) % statuses.Length],
                    Anchor.AddMinutes(-index * 13));
            })
            .ToArray();
    }

    private static IReadOnlyList<SimulationDeviceLog> BuildDeviceLogs()
    {
        var devices = BuildDevices();
        var levels = new[] { "Info", "Warning", "Info", "Error", "Info", "Warning" };
        var messages = new[] { "cycle completed", "temperature warning", "quality checkpoint passed", "station blocked", "heartbeat ok", "pressure drift" };
        return Enumerable.Range(0, 84)
            .Select(index =>
            {
                var device = devices[index % devices.Count];
                return new SimulationDeviceLog(
                    $"LOG-{index + 1:0000}",
                    device.DeviceCode,
                    device.LineName,
                    levels[index % levels.Length],
                    messages[index % messages.Length],
                    index % 3 == 0 ? "PLC" : "MES",
                    Anchor.AddHours(-index * 2));
            })
            .ToArray();
    }

    private static IReadOnlyList<SimulationCapacityRecord> BuildCapacityRecords()
    {
        var devices = BuildDevices();
        return Enumerable.Range(0, 72)
            .Select(index =>
            {
                var device = devices[index % devices.Count];
                var output = 90 + (index % 11) * 7;
                var reject = index % 6;
                var occurredAt = Anchor.AddHours(-index * 4);
                return new SimulationCapacityRecord(
                    $"CAP-{index + 1:0000}",
                    device.DeviceCode,
                    device.LineName,
                    device.ProcessName,
                    index % 2 == 0 ? "Day" : "Night",
                    occurredAt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    index % 2 == 0 ? "P100-A" : "P200-B",
                    output,
                    output - reject,
                    occurredAt);
            })
            .ToArray();
    }

    private static IReadOnlyList<SimulationQualityRecord> BuildQualityRecords()
    {
        var devices = BuildDevices();
        var defectTypes = new[] { "Scratch", "Dimension", "Weld", "Label", "Torque" };
        return Enumerable.Range(0, 56)
            .Select(index =>
            {
                var device = devices[index % devices.Count];
                var occurredAt = Anchor.AddHours(-index * 5);
                return new SimulationQualityRecord(
                    $"QLT-{index + 1:0000}",
                    device.DeviceCode,
                    device.LineName,
                    index % 2 == 0 ? "P100-A" : "P200-B",
                    defectTypes[index % defectTypes.Length],
                    1 + index % 9,
                    120 + index % 40,
                    occurredAt);
            })
            .ToArray();
    }

    private static IReadOnlyList<SimulationWorkOrder> BuildWorkOrders()
    {
        var devices = BuildDevices();
        var statuses = new[] { "Open", "InProgress", "Closed", "Closed" };
        var categories = new[] { "Preventive", "Corrective", "Inspection" };
        var priorities = new[] { "P1", "P2", "P3" };
        return Enumerable.Range(0, 36)
            .Select(index =>
            {
                var device = devices[index % devices.Count];
                var createdAt = Anchor.AddHours(-index * 7);
                var status = statuses[index % statuses.Length];
                return new SimulationWorkOrder(
                    $"WO-{DateTimeOffset.UtcNow:yyyyMMdd}-{index + 1:000}",
                    device.DeviceCode,
                    device.LineName,
                    categories[index % categories.Length],
                    status,
                    priorities[index % priorities.Length],
                    $"{device.DeviceCode} {categories[index % categories.Length]} maintenance task",
                    createdAt,
                    status == "Closed" ? createdAt.AddHours(3 + index % 5) : null);
            })
            .ToArray();
    }
}

internal sealed record SimulationDevice(
    string DeviceCode,
    string DeviceName,
    string LineName,
    string ProcessName,
    string Status,
    DateTimeOffset UpdatedAt);

internal sealed record SimulationDeviceLog(
    string LogId,
    string DeviceCode,
    string LineName,
    string Level,
    string Message,
    string Source,
    DateTimeOffset OccurredAt);

internal sealed record SimulationCapacityRecord(
    string RecordId,
    string DeviceCode,
    string LineName,
    string ProcessName,
    string Shift,
    string ShiftDate,
    string ProductCode,
    int OutputQty,
    int QualifiedQty,
    DateTimeOffset OccurredAt);

internal sealed record SimulationQualityRecord(
    string RecordId,
    string DeviceCode,
    string LineName,
    string ProductCode,
    string DefectType,
    int DefectQty,
    int SampleQty,
    DateTimeOffset OccurredAt);

internal sealed record SimulationWorkOrder(
    string WorkOrderNo,
    string DeviceCode,
    string LineName,
    string Category,
    string Status,
    string Priority,
    string Summary,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ClosedAt);
