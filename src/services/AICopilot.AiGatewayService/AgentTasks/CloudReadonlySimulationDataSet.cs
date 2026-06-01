using System.Globalization;

namespace AICopilot.AiGatewayService.AgentTasks;

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
