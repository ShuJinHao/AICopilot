using AICopilot.DataAnalysisService.Semantics;
using AICopilot.Services.Contracts;

namespace AICopilot.BackendTests;

internal sealed class SampleSemanticPhysicalMappingProvider : ISemanticPhysicalMappingProvider
{
    private readonly IReadOnlyDictionary<SemanticQueryTarget, SemanticPhysicalMapping> _mappings =
        new Dictionary<SemanticQueryTarget, SemanticPhysicalMapping>
        {
            [SemanticQueryTarget.Device] = new SemanticPhysicalMapping(
                SemanticQueryTarget.Device,
                DatabaseProviderType.PostgreSql,
                "device_master_view",
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["deviceId"] = "device_id",
                    ["deviceCode"] = "device_code",
                    ["deviceName"] = "device_name",
                    ["processId"] = "process_id"
                },
                allowedProjectionFields: ["deviceId", "deviceCode", "deviceName", "processId"],
                allowedFilterFields: ["deviceId", "deviceCode", "deviceName", "processId"],
                allowedSortFields: ["deviceCode", "deviceName"],
                databaseName: "SemanticDb",
                defaultSort: new SemanticSort("deviceCode", SemanticSortDirection.Asc)),
            [SemanticQueryTarget.DeviceLog] = new SemanticPhysicalMapping(
                SemanticQueryTarget.DeviceLog,
                DatabaseProviderType.PostgreSql,
                "device_log_view",
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["logId"] = "log_id",
                    ["deviceId"] = "device_id",
                    ["deviceCode"] = "device_code",
                    ["level"] = "log_level",
                    ["message"] = "log_message",
                    ["source"] = "log_source",
                    ["occurredAt"] = "occurred_at"
                },
                allowedProjectionFields: ["logId", "deviceId", "deviceCode", "level", "message", "source", "occurredAt"],
                allowedFilterFields: ["deviceId", "deviceCode", "level", "source"],
                allowedSortFields: ["occurredAt", "level"],
                databaseName: "SemanticDb",
                defaultSort: new SemanticSort("occurredAt", SemanticSortDirection.Desc)),
            [SemanticQueryTarget.Capacity] = new SemanticPhysicalMapping(
                SemanticQueryTarget.Capacity,
                DatabaseProviderType.PostgreSql,
                "capacity_view",
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["recordId"] = "record_id",
                    ["deviceId"] = "device_id",
                    ["deviceCode"] = "device_code",
                    ["processName"] = "process_name",
                    ["shiftDate"] = "shift_date",
                    ["outputQty"] = "output_qty",
                    ["qualifiedQty"] = "qualified_qty",
                    ["occurredAt"] = "occurred_at"
                },
                allowedProjectionFields: ["recordId", "deviceId", "deviceCode", "processName", "shiftDate", "outputQty", "qualifiedQty", "occurredAt"],
                allowedFilterFields: ["recordId", "deviceId", "deviceCode", "processName", "shiftDate"],
                allowedSortFields: ["shiftDate", "occurredAt", "outputQty", "qualifiedQty", "deviceCode", "processName"],
                databaseName: "SemanticDb",
                defaultSort: new SemanticSort("occurredAt", SemanticSortDirection.Desc)),
            [SemanticQueryTarget.ProductionData] = new SemanticPhysicalMapping(
                SemanticQueryTarget.ProductionData,
                DatabaseProviderType.PostgreSql,
                "production_data_view",
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["recordId"] = "record_id",
                    ["deviceId"] = "device_id",
                    ["deviceCode"] = "device_code",
                    ["processName"] = "process_name",
                    ["barcode"] = "barcode",
                    ["stationName"] = "station_name",
                    ["result"] = "result",
                    ["occurredAt"] = "occurred_at"
                },
                allowedProjectionFields: ["recordId", "deviceId", "deviceCode", "processName", "barcode", "stationName", "result", "occurredAt"],
                allowedFilterFields: ["recordId", "deviceId", "deviceCode", "processName", "barcode", "stationName", "result"],
                allowedSortFields: ["occurredAt", "deviceCode", "processName", "stationName", "result"],
                databaseName: "SemanticDb",
                defaultSort: new SemanticSort("occurredAt", SemanticSortDirection.Desc))
        };

    public bool TryGetMapping(SemanticQueryTarget target, out SemanticPhysicalMapping mapping)
    {
        return _mappings.TryGetValue(target, out mapping!);
    }
}
