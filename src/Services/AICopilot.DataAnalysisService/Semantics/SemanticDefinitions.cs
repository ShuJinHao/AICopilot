using AICopilot.Services.Contracts;

namespace AICopilot.DataAnalysisService.Semantics;

public sealed class DeviceSemanticDefinition : SemanticEntityDefinition
{
    public DeviceSemanticDefinition()
        : base(
            SemanticQueryTarget.Device,
            ["设备", "机台", "设备主数据", "产线设备", "device", "machine"],
            ["deviceId", "deviceCode", "deviceName", "status", "lineName", "updatedAt"],
            ["deviceId", "deviceCode", "deviceName", "status", "lineName"],
            ["deviceCode", "deviceName", "updatedAt"],
            new Dictionary<SemanticQueryKind, SemanticProjection>
            {
                [SemanticQueryKind.List] = new(["deviceCode", "deviceName", "status", "lineName", "updatedAt"]),
                [SemanticQueryKind.Detail] = new(["deviceId", "deviceCode", "deviceName", "status", "lineName", "updatedAt"]),
                [SemanticQueryKind.Status] = new(["deviceCode", "deviceName", "status", "updatedAt"])
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
            ["logId", "deviceId", "deviceCode", "level", "message", "source", "occurredAt"],
            ["deviceId", "deviceCode", "level", "source"],
            ["occurredAt", "level"],
            new Dictionary<SemanticQueryKind, SemanticProjection>
            {
                [SemanticQueryKind.Latest] = new(["deviceCode", "level", "message", "occurredAt"]),
                [SemanticQueryKind.Range] = new(["deviceCode", "level", "message", "source", "occurredAt"]),
                [SemanticQueryKind.ByLevel] = new(["deviceCode", "level", "message", "occurredAt"])
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
            ["recordId", "deviceId", "deviceCode", "processName", "shiftDate", "outputQty", "qualifiedQty", "occurredAt"],
            ["recordId", "deviceId", "deviceCode", "processName", "shiftDate"],
            ["shiftDate", "occurredAt", "outputQty", "qualifiedQty", "deviceCode", "processName"],
            new Dictionary<SemanticQueryKind, SemanticProjection>
            {
                [SemanticQueryKind.Range] = new(["deviceCode", "processName", "shiftDate", "outputQty", "qualifiedQty", "occurredAt"]),
                [SemanticQueryKind.ByDevice] = new(["deviceCode", "processName", "shiftDate", "outputQty", "qualifiedQty", "occurredAt"]),
                [SemanticQueryKind.ByProcess] = new(["deviceCode", "processName", "shiftDate", "outputQty", "qualifiedQty", "occurredAt"])
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
            ["recordId", "deviceId", "deviceCode", "processName", "barcode", "stationName", "result", "occurredAt"],
            ["recordId", "deviceId", "deviceCode", "processName", "barcode", "stationName", "result"],
            ["occurredAt", "deviceCode", "processName", "stationName", "result"],
            new Dictionary<SemanticQueryKind, SemanticProjection>
            {
                [SemanticQueryKind.Latest] = new(["deviceCode", "processName", "barcode", "stationName", "result", "occurredAt"]),
                [SemanticQueryKind.Range] = new(["deviceCode", "processName", "barcode", "stationName", "result", "occurredAt"]),
                [SemanticQueryKind.ByDevice] = new(["deviceCode", "processName", "barcode", "stationName", "result", "occurredAt"])
            },
            defaultLimit: 100,
            maxLimit: 200)
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
            [SemanticQueryTarget.ProductionData] = new ProductionDataSemanticDefinition()
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
