using AICopilot.Services.Contracts;

namespace AICopilot.DataAnalysisService.Semantics;

public sealed class DeviceSemanticDefinition : SemanticEntityDefinition
{
    public DeviceSemanticDefinition()
        : base(
            SemanticQueryTarget.Device,
            ["设备", "机台", "设备主数据", "设备运行状态", "device", "machine"],
            ["deviceId", "deviceCode", "deviceName", "processId", "clientCode", "softwareStatus", "runtimeStatus", "runtimeStartedAtUtc", "lastRuntimeHeartbeatAtUtc"],
            ["deviceId", "deviceCode", "deviceName", "processId", "clientCode"],
            ["deviceCode", "deviceName"],
            new Dictionary<SemanticQueryKind, SemanticProjection>
            {
                [SemanticQueryKind.List] = new(["deviceId", "deviceCode", "deviceName", "processId"]),
                [SemanticQueryKind.Detail] = new(["deviceId", "deviceCode", "deviceName", "processId"]),
                [SemanticQueryKind.Status] = new(["deviceId", "deviceName", "clientCode", "softwareStatus", "runtimeStatus", "runtimeStartedAtUtc", "lastRuntimeHeartbeatAtUtc"])
            },
            defaultLimit: 50,
            maxLimit: 100)
    {
    }
}

public sealed class DeviceLogSemanticDefinition : SemanticEntityDefinition
{
    public DeviceLogSemanticDefinition()
        : base(
            SemanticQueryTarget.DeviceLog,
            ["设备日志", "报警日志", "运行日志", "故障日志", "device log", "alarm log"],
            ["logId", "deviceId", "deviceName", "level", "message", "occurredAt", "receivedAt"],
            ["deviceId", "deviceCode", "deviceName", "level", "message"],
            ["occurredAt", "level"],
            new Dictionary<SemanticQueryKind, SemanticProjection>
            {
                [SemanticQueryKind.Latest] = new(["deviceId", "deviceName", "level", "message", "occurredAt"]),
                [SemanticQueryKind.Range] = new(["deviceId", "deviceName", "level", "message", "occurredAt", "receivedAt"]),
                [SemanticQueryKind.ByLevel] = new(["deviceId", "deviceName", "level", "message", "occurredAt"])
            },
            defaultLimit: 50,
            maxLimit: 200)
    {
    }
}

public sealed class RecipeSemanticDefinition : SemanticEntityDefinition
{
    public RecipeSemanticDefinition()
        : base(
            SemanticQueryTarget.Recipe,
            ["配方", "工艺配方", "配方版本", "recipe", "recipe version"],
            ["recipeId", "recipeName", "deviceId", "deviceCode", "processName", "version", "isActive", "updatedAt"],
            ["recipeId", "recipeName", "deviceId", "deviceCode", "processName", "version", "isActive"],
            ["recipeName", "version", "updatedAt", "processName"],
            new Dictionary<SemanticQueryKind, SemanticProjection>
            {
                [SemanticQueryKind.List] = new(["recipeName", "deviceCode", "processName", "version", "isActive", "updatedAt"]),
                [SemanticQueryKind.Detail] = new(["recipeId", "recipeName", "deviceCode", "processName", "version", "isActive", "updatedAt"]),
                [SemanticQueryKind.VersionHistory] = new(["recipeName", "deviceCode", "processName", "version", "isActive", "updatedAt"])
            },
            defaultLimit: 50,
            maxLimit: 100)
    {
    }
}

public sealed class CapacitySemanticDefinition : SemanticEntityDefinition
{
    public CapacitySemanticDefinition()
        : base(
            SemanticQueryTarget.Capacity,
            ["产能", "产量", "良率", "capacity", "output"],
            ["shiftDate", "outputQty", "qualifiedQty", "totalCount", "okCount", "ngCount", "occurredAt"],
            ["deviceId", "deviceCode", "plcName", "shiftDate"],
            ["shiftDate", "occurredAt", "outputQty", "qualifiedQty"],
            new Dictionary<SemanticQueryKind, SemanticProjection>
            {
                [SemanticQueryKind.Range] = new(["shiftDate", "outputQty", "qualifiedQty", "occurredAt"]),
                [SemanticQueryKind.ByDevice] = new(["shiftDate", "outputQty", "qualifiedQty", "occurredAt"])
            },
            defaultLimit: 100,
            maxLimit: 200)
    {
    }
}

public sealed class ProductionDataSemanticDefinition : SemanticEntityDefinition
{
    public ProductionDataSemanticDefinition()
        : base(
            SemanticQueryTarget.ProductionData,
            ["生产数据", "过站数据", "工序数据", "production data", "station data"],
            ["recordId", "typeKey", "typeName", "deviceId", "deviceName", "barcode", "result", "completedAt", "receivedAt", "fields", "fieldSchema"],
            ["typeKey", "processId", "deviceId", "deviceCode", "barcode", "result"],
            ["completedAt", "typeKey", "result"],
            new Dictionary<SemanticQueryKind, SemanticProjection>
            {
                [SemanticQueryKind.Latest] = new(["recordId", "typeKey", "typeName", "deviceId", "deviceName", "barcode", "result", "completedAt"]),
                [SemanticQueryKind.Range] = new(["recordId", "typeKey", "typeName", "deviceId", "deviceName", "barcode", "result", "completedAt"]),
                [SemanticQueryKind.ByDevice] = new(["recordId", "typeKey", "typeName", "deviceId", "deviceName", "barcode", "result", "completedAt"])
            },
            defaultLimit: 100,
            maxLimit: 200)
    {
    }
}

public sealed class ProcessSemanticDefinition : SemanticEntityDefinition
{
    public ProcessSemanticDefinition()
        : base(
            SemanticQueryTarget.Process,
            ["工序", "工序主数据", "process", "process master data"],
            ["processId", "processCode", "processName"],
            ["processId", "processCode", "processName"],
            [],
            new Dictionary<SemanticQueryKind, SemanticProjection>
            {
                [SemanticQueryKind.List] = new(["processId", "processCode", "processName"]),
                [SemanticQueryKind.Detail] = new(["processId", "processCode", "processName"])
            },
            defaultLimit: 50,
            maxLimit: 100)
    {
    }
}

public sealed class ClientReleaseSemanticDefinition : SemanticEntityDefinition
{
    public ClientReleaseSemanticDefinition()
        : base(
            SemanticQueryTarget.ClientRelease,
            ["客户端发布版本", "客户端版本", "发布版本", "client release", "client version"],
            ["releaseId", "componentKind", "componentKey", "displayName", "channel", "targetRuntime", "version", "status", "releaseNotes", "createdAtUtc", "publishedAtUtc", "deletedAtUtc"],
            ["channel", "targetRuntime", "status", "includeArchived"],
            [],
            new Dictionary<SemanticQueryKind, SemanticProjection>
            {
                [SemanticQueryKind.List] = new(["releaseId", "componentKind", "componentKey", "displayName", "channel", "targetRuntime", "version", "status", "createdAtUtc", "publishedAtUtc", "deletedAtUtc"])
            },
            defaultLimit: 50,
            maxLimit: 100)
    {
    }
}

public sealed class SemanticDefinitionCatalog : ISemanticDefinitionCatalog
{
    private readonly IReadOnlyDictionary<SemanticQueryTarget, SemanticEntityDefinition> _definitions =
        new Dictionary<SemanticQueryTarget, SemanticEntityDefinition>
        {
            [SemanticQueryTarget.Device] = new DeviceSemanticDefinition(),
            [SemanticQueryTarget.DeviceLog] = new DeviceLogSemanticDefinition(),
            [SemanticQueryTarget.Recipe] = new RecipeSemanticDefinition(),
            [SemanticQueryTarget.Capacity] = new CapacitySemanticDefinition(),
            [SemanticQueryTarget.ProductionData] = new ProductionDataSemanticDefinition(),
            [SemanticQueryTarget.Process] = new ProcessSemanticDefinition(),
            [SemanticQueryTarget.ClientRelease] = new ClientReleaseSemanticDefinition()
        };

    public IReadOnlyCollection<SemanticEntityDefinition> GetAll()
    {
        return _definitions.Values.ToArray();
    }

    public SemanticEntityDefinition Get(SemanticQueryTarget target)
    {
        if (_definitions.TryGetValue(target, out var definition))
        {
            return definition;
        }

        throw new NotSupportedException($"未找到目标 {target} 的语义定义。");
    }
}
