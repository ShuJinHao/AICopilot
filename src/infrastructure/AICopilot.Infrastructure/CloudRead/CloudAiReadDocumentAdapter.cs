using System.Text.Json;
using AICopilot.Services.Contracts;

namespace AICopilot.Infrastructure.CloudRead;

internal static class CloudAiReadDocumentAdapter
{
    public static CloudAiReadResult<CloudAiReadDeviceDto> MapDevices(
        JsonElement root,
        string sourcePath,
        int limit)
    {
        var records = CloudAiReadJsonValueReader.ExtractRecords(root, limit);
        var items = records.Select(record => new CloudAiReadDeviceDto(
            CloudAiReadJsonValueReader.GetString(record, "deviceId", "id"),
            CloudAiReadJsonValueReader.GetString(record, "deviceCode", "clientCode", "code"),
            CloudAiReadJsonValueReader.GetString(record, "deviceName", "name"),
            CloudAiReadJsonValueReader.GetString(record, "status", "state"),
            CloudAiReadJsonValueReader.GetString(record, "lineName", "line"),
            CloudAiReadJsonValueReader.GetDate(record, "updatedAt", "updatedAtUtc", "lastSeenAt"),
            CloudAiReadJsonValueReader.ExtractAdditionalFields(record))).ToArray();

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

    public static CloudAiReadResult<CloudAiReadProcessDto> MapProcesses(
        JsonElement root,
        string sourcePath,
        int limit)
    {
        var records = CloudAiReadJsonValueReader.ExtractRecords(root, limit);
        var items = records.Select(record => new CloudAiReadProcessDto(
            CloudAiReadJsonValueReader.GetString(record, "processId", "id"),
            CloudAiReadJsonValueReader.GetString(record, "processCode", "code"),
            CloudAiReadJsonValueReader.GetString(record, "processName", "name"),
            CloudAiReadJsonValueReader.ExtractAdditionalFields(record))).ToArray();

        var rows = items.Select(item => new Dictionary<string, object?>
        {
            ["processId"] = item.ProcessId,
            ["processCode"] = item.ProcessCode,
            ["processName"] = item.ProcessName
        }).ToArray();

        return BuildResult(sourcePath, "Cloud AiRead API（工序正式只读数据）", limit, root, items, rows);
    }

    public static CloudAiReadResult<CloudAiReadClientReleaseVersionDto> MapClientReleases(
        JsonElement root,
        string sourcePath,
        int limit)
    {
        var records = CloudAiReadJsonValueReader.ExtractRecords(root, limit);
        var items = records.Select(record => new CloudAiReadClientReleaseVersionDto(
            CloudAiReadJsonValueReader.GetString(record, "releaseId", "id"),
            CloudAiReadJsonValueReader.GetString(record, "componentKind"),
            CloudAiReadJsonValueReader.GetString(record, "componentKey"),
            CloudAiReadJsonValueReader.GetString(record, "displayName"),
            CloudAiReadJsonValueReader.GetString(record, "channel"),
            CloudAiReadJsonValueReader.GetString(record, "targetRuntime"),
            CloudAiReadJsonValueReader.GetString(record, "version"),
            CloudAiReadJsonValueReader.GetString(record, "status"),
            CloudAiReadJsonValueReader.GetString(record, "releaseNotes"),
            CloudAiReadJsonValueReader.GetDate(record, "createdAtUtc", "createdAt"),
            CloudAiReadJsonValueReader.GetDate(record, "publishedAtUtc", "publishedAt"),
            CloudAiReadJsonValueReader.GetDate(record, "deletedAtUtc", "deletedAt"),
            CloudAiReadJsonValueReader.ExtractAdditionalFields(record))).ToArray();

        var rows = items.Select(item => new Dictionary<string, object?>
        {
            ["releaseId"] = item.ReleaseId,
            ["componentKind"] = item.ComponentKind,
            ["componentKey"] = item.ComponentKey,
            ["displayName"] = item.DisplayName,
            ["channel"] = item.Channel,
            ["targetRuntime"] = item.TargetRuntime,
            ["version"] = item.Version,
            ["status"] = item.Status,
            ["releaseNotes"] = item.ReleaseNotes,
            ["createdAtUtc"] = item.CreatedAtUtc,
            ["publishedAtUtc"] = item.PublishedAtUtc,
            ["deletedAtUtc"] = item.DeletedAtUtc
        }).ToArray();

        return BuildResult(sourcePath, "Cloud AiRead API（客户端发布版本正式只读数据）", limit, root, items, rows);
    }

    public static CloudAiReadResult<CloudAiReadDeviceClientStateDto> MapDeviceClientStates(
        JsonElement root,
        string sourcePath,
        int limit)
    {
        var records = CloudAiReadJsonValueReader.ExtractRecords(root, limit);
        var items = records.Select(record => new CloudAiReadDeviceClientStateDto(
            CloudAiReadJsonValueReader.GetString(record, "deviceId", "id"),
            CloudAiReadJsonValueReader.GetString(record, "deviceName"),
            CloudAiReadJsonValueReader.GetString(record, "clientCode", "deviceCode"),
            CloudAiReadJsonValueReader.GetString(record, "primaryIp"),
            CloudAiReadJsonValueReader.GetString(record, "channel"),
            CloudAiReadJsonValueReader.GetString(record, "hostVersion"),
            CloudAiReadJsonValueReader.GetString(record, "hostApiVersion"),
            CloudAiReadJsonValueReader.GetDate(record, "versionReportedAtUtc", "versionReportedAt"),
            CloudAiReadJsonValueReader.GetDate(record, "versionReceivedAtUtc", "versionReceivedAt"),
            CloudAiReadJsonValueReader.GetString(record, "runtimeStatus"),
            CloudAiReadJsonValueReader.GetDate(record, "runtimeStartedAtUtc", "runtimeStartedAt"),
            CloudAiReadJsonValueReader.GetDate(record, "lastRuntimeHeartbeatAtUtc", "lastRuntimeHeartbeatAt"),
            CloudAiReadJsonValueReader.GetDate(record, "updatedAtUtc", "updatedAt"),
            CloudAiReadJsonValueReader.ExtractAdditionalFields(record))).ToArray();

        var rows = items.Select(item => new Dictionary<string, object?>
        {
            ["deviceId"] = item.DeviceId,
            ["deviceName"] = item.DeviceName,
            ["clientCode"] = item.ClientCode,
            ["primaryIp"] = item.PrimaryIp,
            ["channel"] = item.Channel,
            ["hostVersion"] = item.HostVersion,
            ["hostApiVersion"] = item.HostApiVersion,
            ["versionReportedAtUtc"] = item.VersionReportedAtUtc,
            ["versionReceivedAtUtc"] = item.VersionReceivedAtUtc,
            ["runtimeStatus"] = item.RuntimeStatus,
            ["runtimeStartedAtUtc"] = item.RuntimeStartedAtUtc,
            ["lastRuntimeHeartbeatAtUtc"] = item.LastRuntimeHeartbeatAtUtc,
            ["updatedAtUtc"] = item.UpdatedAtUtc
        }).ToArray();

        return BuildResult(sourcePath, "Cloud AiRead API（设备客户端状态正式只读数据）", limit, root, items, rows);
    }

    public static CloudAiReadResult<CloudAiReadCapacitySummaryDto> MapCapacitySummary(
        JsonElement root,
        string sourcePath,
        int limit)
    {
        var records = CloudAiReadJsonValueReader.ExtractRecords(root, limit);
        var items = records.Select(record => new CloudAiReadCapacitySummaryDto(
            CloudAiReadJsonValueReader.GetString(record, "date", "shiftDate"),
            CloudAiReadJsonValueReader.GetDecimal(record, "totalCount", "outputQty", "output", "totalOutput"),
            CloudAiReadJsonValueReader.GetDecimal(record, "okCount", "qualifiedQty", "qualified", "goodQty"),
            CloudAiReadJsonValueReader.GetDecimal(record, "ngCount"),
            CloudAiReadJsonValueReader.GetDecimal(record, "dayShiftTotal"),
            CloudAiReadJsonValueReader.GetDecimal(record, "nightShiftTotal"),
            CloudAiReadJsonValueReader.ExtractAdditionalFields(record))).ToArray();

        var rows = items.Select(item => new Dictionary<string, object?>
        {
            ["shiftDate"] = item.Date,
            ["date"] = item.Date,
            ["totalCount"] = item.TotalCount,
            ["okCount"] = item.OkCount,
            ["ngCount"] = item.NgCount,
            ["dayShiftTotal"] = item.DayShiftTotal,
            ["nightShiftTotal"] = item.NightShiftTotal,
            ["outputQty"] = item.TotalCount,
            ["qualifiedQty"] = item.OkCount,
            ["occurredAt"] = item.Date
        }).ToArray();

        return BuildResult(sourcePath, "Cloud AiRead API（产能正式只读数据）", limit, root, items, rows);
    }

    public static CloudAiReadResult<CloudAiReadCapacityHourlyDto> MapCapacityHourly(
        JsonElement root,
        string sourcePath,
        int limit)
    {
        var records = CloudAiReadJsonValueReader.ExtractRecords(root, limit);
        var items = records.Select(record => new CloudAiReadCapacityHourlyDto(
            CloudAiReadJsonValueReader.GetDate(record, "time", "occurredAt"),
            CloudAiReadJsonValueReader.GetString(record, "date", "shiftDate"),
            CloudAiReadJsonValueReader.GetInt(record, "hour"),
            CloudAiReadJsonValueReader.GetInt(record, "minute"),
            CloudAiReadJsonValueReader.GetString(record, "timeLabel"),
            CloudAiReadJsonValueReader.GetString(record, "shiftCode"),
            CloudAiReadJsonValueReader.GetDecimal(record, "totalCount", "outputQty"),
            CloudAiReadJsonValueReader.GetDecimal(record, "okCount", "qualifiedQty"),
            CloudAiReadJsonValueReader.GetDecimal(record, "ngCount"),
            CloudAiReadJsonValueReader.GetDecimal(record, "okRate"),
            CloudAiReadJsonValueReader.ExtractAdditionalFields(record))).ToArray();

        var rows = items.Select(item => new Dictionary<string, object?>
        {
            ["occurredAt"] = item.Time,
            ["shiftDate"] = item.Date,
            ["date"] = item.Date,
            ["hour"] = item.Hour,
            ["minute"] = item.Minute,
            ["timeLabel"] = item.TimeLabel,
            ["shiftCode"] = item.ShiftCode,
            ["totalCount"] = item.TotalCount,
            ["okCount"] = item.OkCount,
            ["ngCount"] = item.NgCount,
            ["okRate"] = item.OkRate,
            ["outputQty"] = item.TotalCount,
            ["qualifiedQty"] = item.OkCount
        }).ToArray();

        return BuildResult(sourcePath, "Cloud AiRead API（小时产能正式只读数据）", limit, root, items, rows);
    }

    public static CloudAiReadResult<CloudAiReadDeviceLogDto> MapDeviceLogs(
        JsonElement root,
        string sourcePath,
        int limit)
    {
        var records = CloudAiReadJsonValueReader.ExtractRecords(root, limit);
        var items = records.Select(record => new CloudAiReadDeviceLogDto(
            CloudAiReadJsonValueReader.GetString(record, "logId", "id"),
            CloudAiReadJsonValueReader.GetString(record, "deviceId"),
            CloudAiReadJsonValueReader.GetString(record, "deviceCode", "clientCode"),
            CloudAiReadJsonValueReader.GetString(record, "deviceName"),
            CloudAiReadJsonValueReader.GetString(record, "level", "severity"),
            CloudAiReadJsonValueReader.GetString(record, "message", "content"),
            CloudAiReadJsonValueReader.GetString(record, "source", "logger"),
            CloudAiReadJsonValueReader.GetDate(record, "occurredAt", "logTime", "createdAt", "timestamp"),
            CloudAiReadJsonValueReader.ExtractAdditionalFields(record))).ToArray();

        var rows = items.Select(item => new Dictionary<string, object?>
        {
            ["logId"] = item.LogId,
            ["deviceId"] = item.DeviceId,
            ["deviceCode"] = item.DeviceCode,
            ["deviceName"] = item.DeviceName,
            ["level"] = item.Level,
            ["message"] = item.Message,
            ["source"] = item.Source,
            ["occurredAt"] = item.OccurredAt
        }).ToArray();

        return BuildResult(sourcePath, "Cloud AiRead API（设备日志正式只读数据）", limit, root, items, rows);
    }

    public static CloudAiReadResult<CloudAiReadProductionRecordDto> MapProductionRecords(
        JsonElement root,
        string sourcePath,
        int limit)
    {
        var records = CloudAiReadJsonValueReader.ExtractRecords(root, limit);
        var items = records.Select(record => new CloudAiReadProductionRecordDto(
            CloudAiReadJsonValueReader.GetString(record, "recordId", "id"),
            CloudAiReadJsonValueReader.GetString(record, "typeKey"),
            CloudAiReadJsonValueReader.GetString(record, "typeName", "displayName"),
            CloudAiReadJsonValueReader.GetString(record, "deviceId"),
            CloudAiReadJsonValueReader.GetString(record, "deviceCode", "clientCode"),
            CloudAiReadJsonValueReader.GetString(record, "deviceName"),
            CloudAiReadJsonValueReader.GetString(record, "processName", "process", "typeName"),
            CloudAiReadJsonValueReader.GetString(record, "barcode", "cellCode", "sn"),
            CloudAiReadJsonValueReader.GetString(record, "stationName", "station", "typeName", "typeKey"),
            CloudAiReadJsonValueReader.GetString(record, "result", "status"),
            CloudAiReadJsonValueReader.GetDate(record, "occurredAt", "completedAt", "passedAt", "createdAt"),
            CloudAiReadJsonValueReader.GetDate(record, "receivedAt"),
            CloudAiReadJsonValueReader.GetObject(record, "fields"),
            CloudAiReadJsonValueReader.GetObjectArray(record, "fieldSchema")
                .Select(MapProductionFieldSchema)
                .ToArray(),
            CloudAiReadJsonValueReader.ExtractAdditionalFields(record))).ToArray();

        var rows = items.Select(item => new Dictionary<string, object?>
        {
            ["recordId"] = item.RecordId,
            ["typeKey"] = item.TypeKey,
            ["typeName"] = item.TypeName,
            ["deviceId"] = item.DeviceId,
            ["deviceCode"] = item.DeviceCode,
            ["deviceName"] = item.DeviceName,
            ["processName"] = item.ProcessName,
            ["barcode"] = item.Barcode,
            ["stationName"] = item.StationName,
            ["result"] = item.Result,
            ["occurredAt"] = item.OccurredAt,
            ["completedAt"] = item.OccurredAt,
            ["receivedAt"] = item.ReceivedAt,
            ["fields"] = item.Fields,
            ["fieldSchema"] = item.FieldSchema
        }).ToArray();

        return BuildResult(sourcePath, "Cloud AiRead API（生产记录正式只读数据）", limit, root, items, rows);
    }

    private static CloudAiReadProductionFieldSchemaDto MapProductionFieldSchema(JsonElement record)
    {
        return new CloudAiReadProductionFieldSchemaDto(
            CloudAiReadJsonValueReader.GetString(record, "key"),
            CloudAiReadJsonValueReader.GetString(record, "label"),
            CloudAiReadJsonValueReader.GetString(record, "type"),
            CloudAiReadJsonValueReader.GetString(record, "unit"),
            CloudAiReadJsonValueReader.GetInt(record, "precision"),
            TryGetBool(record, "required"),
            CloudAiReadJsonValueReader.ExtractAdditionalFields(record));
    }

    private static bool? TryGetBool(JsonElement record, string name)
    {
        return record.TryGetProperty(name, out var property) && property.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? property.GetBoolean()
            : null;
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
            CloudAiReadJsonValueReader.IsTruncated(root, items.Count, limit),
            items,
            rows);
    }
}
