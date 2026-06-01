using System.Globalization;
using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Result;

namespace AICopilot.AiGatewayService.AgentTasks;

internal sealed class CloudReadonlySimulationQueryExecutor(CloudReadonlySimulationDataSet dataSet)
{
    public CloudReadonlyAgentToolResult Execute(string intent, CloudReadonlySimulationQuery query)
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
