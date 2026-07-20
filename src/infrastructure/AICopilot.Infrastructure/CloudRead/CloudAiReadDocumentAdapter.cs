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
        var records = CloudAiReadJsonValueReader.ExtractRecords(root, limit, CloudAiReadOperation.Device);
        var items = records.Select(record => new CloudAiReadDeviceDto(
            CloudAiReadJsonValueReader.GetRequiredGuid(record, "id"),
            CloudAiReadJsonValueReader.GetRequiredString(record, "deviceCode"),
            CloudAiReadJsonValueReader.GetRequiredString(record, "deviceName"),
            CloudAiReadJsonValueReader.GetRequiredGuid(record, "processId"),
            CloudAiReadJsonValueReader.ExtractAdditionalFields(record))).ToArray();

        var rows = items.Select(item => new Dictionary<string, object?>
        {
            ["deviceId"] = item.DeviceId,
            ["deviceCode"] = item.DeviceCode,
            ["deviceName"] = item.DeviceName,
            ["processId"] = item.ProcessId
        }).ToArray();

        return BuildResult(sourcePath, "Cloud AiRead API（设备正式只读数据）", limit, root, items, rows);
    }

    public static CloudAiReadResult<CloudAiReadProcessDto> MapProcesses(
        JsonElement root,
        string sourcePath,
        int limit)
    {
        var records = CloudAiReadJsonValueReader.ExtractRecords(root, limit, CloudAiReadOperation.Process);
        var items = records.Select(record => new CloudAiReadProcessDto(
            CloudAiReadJsonValueReader.GetRequiredGuid(record, "id"),
            CloudAiReadJsonValueReader.GetRequiredString(record, "processCode"),
            CloudAiReadJsonValueReader.GetRequiredString(record, "processName"),
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
        var records = CloudAiReadJsonValueReader.ExtractRecords(root, limit, CloudAiReadOperation.ClientRelease);
        var items = records.Select(record => new CloudAiReadClientReleaseVersionDto(
            CloudAiReadJsonValueReader.GetRequiredGuid(record, "id"),
            CloudAiReadJsonValueReader.GetRequiredString(record, "componentKind"),
            CloudAiReadJsonValueReader.GetRequiredString(record, "componentKey"),
            CloudAiReadJsonValueReader.GetRequiredString(record, "displayName"),
            CloudAiReadJsonValueReader.GetRequiredString(record, "channel"),
            CloudAiReadJsonValueReader.GetRequiredString(record, "targetRuntime"),
            CloudAiReadJsonValueReader.GetRequiredString(record, "version"),
            CloudAiReadJsonValueReader.GetRequiredString(record, "status"),
            CloudAiReadJsonValueReader.GetString(record, "releaseNotes"),
            CloudAiReadJsonValueReader.GetRequiredDate(record, "createdAtUtc"),
            CloudAiReadJsonValueReader.GetDate(record, "publishedAtUtc"),
            CloudAiReadJsonValueReader.GetDate(record, "deletedAtUtc"),
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
        var records = CloudAiReadJsonValueReader.ExtractRecords(
            root,
            limit,
            CloudAiReadOperation.DeviceClientState);
        var items = records.Select(record => new CloudAiReadDeviceClientStateDto(
            CloudAiReadJsonValueReader.GetRequiredGuid(record, "deviceId"),
            CloudAiReadJsonValueReader.GetRequiredString(record, "deviceName"),
            CloudAiReadJsonValueReader.GetRequiredString(record, "clientCode"),
            CloudAiReadJsonValueReader.GetString(record, "primaryIp"),
            CloudAiReadJsonValueReader.GetString(record, "channel"),
            CloudAiReadJsonValueReader.GetString(record, "hostVersion"),
            CloudAiReadJsonValueReader.GetString(record, "hostApiVersion"),
            CloudAiReadJsonValueReader.GetDate(record, "versionReportedAtUtc"),
            CloudAiReadJsonValueReader.GetDate(record, "versionReceivedAtUtc"),
            CloudAiReadJsonValueReader.GetRequiredString(record, "softwareStatus"),
            CloudAiReadJsonValueReader.GetString(record, "runtimeStatus"),
            CloudAiReadJsonValueReader.GetDate(record, "runtimeStartedAtUtc"),
            CloudAiReadJsonValueReader.GetDate(record, "lastRuntimeHeartbeatAtUtc"),
            CloudAiReadJsonValueReader.GetDate(record, "updatedAtUtc"),
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
            ["softwareStatus"] = item.SoftwareStatus,
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
        var records = CloudAiReadJsonValueReader.ExtractRecords(
            root,
            limit,
            CloudAiReadOperation.CapacitySummary);
        var items = records.Select(record => new CloudAiReadCapacitySummaryDto(
            CloudAiReadJsonValueReader.GetRequiredDateOnly(record, "date"),
            CloudAiReadJsonValueReader.GetRequiredInt(record, "totalCount"),
            CloudAiReadJsonValueReader.GetRequiredInt(record, "okCount"),
            CloudAiReadJsonValueReader.GetRequiredInt(record, "ngCount"),
            CloudAiReadJsonValueReader.GetRequiredInt(record, "dayShiftTotal"),
            CloudAiReadJsonValueReader.GetRequiredInt(record, "nightShiftTotal"),
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
        var records = CloudAiReadJsonValueReader.ExtractRecords(
            root,
            limit,
            CloudAiReadOperation.CapacityHourly);
        var items = records.Select(record => new CloudAiReadCapacityHourlyDto(
            CloudAiReadJsonValueReader.GetRequiredDate(record, "time"),
            CloudAiReadJsonValueReader.GetRequiredDateOnly(record, "date"),
            CloudAiReadJsonValueReader.GetRequiredInt(record, "hour"),
            CloudAiReadJsonValueReader.GetRequiredInt(record, "minute"),
            CloudAiReadJsonValueReader.GetRequiredString(record, "timeLabel"),
            CloudAiReadJsonValueReader.GetRequiredString(record, "shiftCode"),
            CloudAiReadJsonValueReader.GetRequiredInt(record, "totalCount"),
            CloudAiReadJsonValueReader.GetRequiredInt(record, "okCount"),
            CloudAiReadJsonValueReader.GetRequiredInt(record, "ngCount"),
            CloudAiReadJsonValueReader.GetRequiredDecimal(record, "okRate"),
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
        var records = CloudAiReadJsonValueReader.ExtractRecords(root, limit, CloudAiReadOperation.DeviceLog);
        var items = records.Select(record => new CloudAiReadDeviceLogDto(
            CloudAiReadJsonValueReader.GetRequiredGuid(record, "id"),
            CloudAiReadJsonValueReader.GetRequiredGuid(record, "deviceId"),
            CloudAiReadJsonValueReader.GetRequiredString(record, "deviceName"),
            CloudAiReadJsonValueReader.GetRequiredString(record, "level"),
            CloudAiReadJsonValueReader.GetRequiredString(record, "message"),
            CloudAiReadJsonValueReader.GetRequiredDate(record, "logTime"),
            CloudAiReadJsonValueReader.GetRequiredDate(record, "receivedAt"),
            CloudAiReadJsonValueReader.ExtractAdditionalFields(record))).ToArray();

        var rows = items.Select(item => new Dictionary<string, object?>
        {
            ["logId"] = item.LogId,
            ["deviceId"] = item.DeviceId,
            ["deviceName"] = item.DeviceName,
            ["level"] = item.Level,
            ["message"] = item.Message,
            ["logTime"] = item.LogTime,
            ["receivedAt"] = item.ReceivedAt,
            ["occurredAt"] = item.LogTime
        }).ToArray();

        return BuildResult(sourcePath, "Cloud AiRead API（设备日志正式只读数据）", limit, root, items, rows);
    }

    public static CloudAiReadResult<CloudAiReadProductionRecordDto> MapProductionRecords(
        JsonElement root,
        string sourcePath,
        int limit)
    {
        var records = CloudAiReadJsonValueReader.ExtractRecords(
            root,
            limit,
            CloudAiReadOperation.ProductionRecord);
        var items = records.Select(record => new CloudAiReadProductionRecordDto(
            CloudAiReadJsonValueReader.GetRequiredGuid(record, "recordId"),
            CloudAiReadJsonValueReader.GetRequiredString(record, "typeKey"),
            CloudAiReadJsonValueReader.GetRequiredString(record, "typeName"),
            CloudAiReadJsonValueReader.GetRequiredGuid(record, "deviceId"),
            CloudAiReadJsonValueReader.GetRequiredString(record, "deviceName"),
            CloudAiReadJsonValueReader.GetString(record, "barcode"),
            CloudAiReadJsonValueReader.GetString(record, "result"),
            CloudAiReadJsonValueReader.GetDate(record, "completedAt"),
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
            ["deviceName"] = item.DeviceName,
            ["barcode"] = item.Barcode,
            ["result"] = item.Result,
            ["completedAt"] = item.CompletedAt,
            ["receivedAt"] = item.ReceivedAt,
            ["fields"] = item.Fields,
            ["fieldSchema"] = item.FieldSchema
        }).ToArray();

        return BuildResult(sourcePath, "Cloud AiRead API（生产记录正式只读数据）", limit, root, items, rows);
    }

    private static CloudAiReadProductionFieldSchemaDto MapProductionFieldSchema(JsonElement record)
    {
        return new CloudAiReadProductionFieldSchemaDto(
            CloudAiReadJsonValueReader.GetRequiredString(record, "key"),
            CloudAiReadJsonValueReader.GetRequiredString(record, "label"),
            CloudAiReadJsonValueReader.GetRequiredString(record, "type"),
            CloudAiReadJsonValueReader.GetString(record, "unit"),
            CloudAiReadJsonValueReader.GetInt(record, "precision"),
            CloudAiReadJsonValueReader.GetRequiredBoolean(record, "required"),
            CloudAiReadJsonValueReader.ExtractAdditionalFields(record));
    }

    private static CloudAiReadResult<T> BuildResult<T>(
        string sourcePath,
        string sourceLabel,
        int limit,
        JsonElement root,
        IReadOnlyList<T> items,
        IReadOnlyList<Dictionary<string, object?>> rows)
    {
        var metadata = CloudAiReadJsonValueReader.ReadEnvelopeMetadata(root);
        return new CloudAiReadResult<T>(
            sourcePath,
            sourceLabel,
            metadata.AsOfUtc,
            CloudAiReadRowLimitPolicy.Normalize(limit),
            metadata.Truncated,
            items,
            rows,
            metadata.Source,
            metadata.QueryScope,
            metadata.RowCount,
            metadata.NextCursor);
    }
}
