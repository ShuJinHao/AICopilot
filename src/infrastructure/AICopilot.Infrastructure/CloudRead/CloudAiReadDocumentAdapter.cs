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

    public static CloudAiReadResult<CloudAiReadCapacitySummaryDto> MapCapacitySummary(
        JsonElement root,
        string sourcePath,
        int limit)
    {
        var records = CloudAiReadJsonValueReader.ExtractRecords(root, limit);
        var items = records.Select(record => new CloudAiReadCapacitySummaryDto(
            CloudAiReadJsonValueReader.GetString(record, "recordId", "id"),
            CloudAiReadJsonValueReader.GetString(record, "deviceId"),
            CloudAiReadJsonValueReader.GetString(record, "deviceCode", "clientCode"),
            CloudAiReadJsonValueReader.GetString(record, "processName", "process"),
            CloudAiReadJsonValueReader.GetString(record, "shiftDate", "date"),
            CloudAiReadJsonValueReader.GetDecimal(record, "outputQty", "output", "totalOutput"),
            CloudAiReadJsonValueReader.GetDecimal(record, "qualifiedQty", "qualified", "goodQty"),
            CloudAiReadJsonValueReader.GetDate(record, "occurredAt", "recordedAt", "updatedAt"),
            CloudAiReadJsonValueReader.ExtractAdditionalFields(record))).ToArray();

        var rows = items.Select(item => new Dictionary<string, object?>
        {
            ["recordId"] = item.RecordId,
            ["deviceId"] = item.DeviceId,
            ["deviceCode"] = item.DeviceCode,
            ["processName"] = item.ProcessName,
            ["shiftDate"] = item.ShiftDate,
            ["outputQty"] = item.OutputQty,
            ["qualifiedQty"] = item.QualifiedQty,
            ["occurredAt"] = item.OccurredAt
        }).ToArray();

        return BuildResult(sourcePath, "Cloud AiRead API（产能正式只读数据）", limit, root, items, rows);
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
            CloudAiReadJsonValueReader.GetString(record, "level", "severity"),
            CloudAiReadJsonValueReader.GetString(record, "message", "content"),
            CloudAiReadJsonValueReader.GetString(record, "source", "logger"),
            CloudAiReadJsonValueReader.GetDate(record, "occurredAt", "createdAt", "timestamp"),
            CloudAiReadJsonValueReader.ExtractAdditionalFields(record))).ToArray();

        var rows = items.Select(item => new Dictionary<string, object?>
        {
            ["logId"] = item.LogId,
            ["deviceId"] = item.DeviceId,
            ["deviceCode"] = item.DeviceCode,
            ["level"] = item.Level,
            ["message"] = item.Message,
            ["source"] = item.Source,
            ["occurredAt"] = item.OccurredAt
        }).ToArray();

        return BuildResult(sourcePath, "Cloud AiRead API（设备日志正式只读数据）", limit, root, items, rows);
    }

    public static CloudAiReadResult<CloudAiReadPassStationRecordDto> MapPassStationRecords(
        JsonElement root,
        string sourcePath,
        int limit)
    {
        var records = CloudAiReadJsonValueReader.ExtractRecords(root, limit);
        var items = records.Select(record => new CloudAiReadPassStationRecordDto(
            CloudAiReadJsonValueReader.GetString(record, "recordId", "id"),
            CloudAiReadJsonValueReader.GetString(record, "deviceId"),
            CloudAiReadJsonValueReader.GetString(record, "deviceCode", "clientCode"),
            CloudAiReadJsonValueReader.GetString(record, "processName", "process"),
            CloudAiReadJsonValueReader.GetString(record, "barcode", "cellCode", "sn"),
            CloudAiReadJsonValueReader.GetString(record, "stationName", "station"),
            CloudAiReadJsonValueReader.GetString(record, "result", "status"),
            CloudAiReadJsonValueReader.GetDate(record, "occurredAt", "passedAt", "createdAt"),
            CloudAiReadJsonValueReader.ExtractAdditionalFields(record))).ToArray();

        var rows = items.Select(item => new Dictionary<string, object?>
        {
            ["recordId"] = item.RecordId,
            ["deviceId"] = item.DeviceId,
            ["deviceCode"] = item.DeviceCode,
            ["processName"] = item.ProcessName,
            ["barcode"] = item.Barcode,
            ["stationName"] = item.StationName,
            ["result"] = item.Result,
            ["occurredAt"] = item.OccurredAt
        }).ToArray();

        return BuildResult(sourcePath, "Cloud AiRead API（过站/生产正式只读数据）", limit, root, items, rows);
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
