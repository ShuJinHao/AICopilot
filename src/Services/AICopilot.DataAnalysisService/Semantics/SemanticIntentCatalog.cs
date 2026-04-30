using AICopilot.Services.Contracts;

namespace AICopilot.DataAnalysisService.Semantics;

public sealed class SemanticIntentCatalog(
    ISemanticDefinitionCatalog definitionCatalog) : ISemanticIntentCatalog
{
    private readonly IReadOnlyDictionary<string, SemanticIntentDescriptor> _descriptors =
        BuildDescriptors(definitionCatalog)
            .ToDictionary(item => item.Intent, StringComparer.OrdinalIgnoreCase);

    public IReadOnlyCollection<SemanticIntentDescriptor> GetAll()
    {
        return _descriptors.Values.OrderBy(item => item.Intent).ToArray();
    }

    public bool TryGet(string intent, out SemanticIntentDescriptor descriptor)
    {
        return _descriptors.TryGetValue(intent, out descriptor!);
    }

    private static IReadOnlyList<SemanticIntentDescriptor> BuildDescriptors(ISemanticDefinitionCatalog definitionCatalog)
    {
        var deviceDefinition = definitionCatalog.Get(SemanticQueryTarget.Device);
        var deviceLogDefinition = definitionCatalog.Get(SemanticQueryTarget.DeviceLog);
        var recipeDefinition = definitionCatalog.Get(SemanticQueryTarget.Recipe);
        var capacityDefinition = definitionCatalog.Get(SemanticQueryTarget.Capacity);
        var productionDefinition = definitionCatalog.Get(SemanticQueryTarget.ProductionData);

        return
        [
            new(
                "Analysis.Device.List",
                SemanticQueryTarget.Device,
                SemanticQueryKind.List,
                "设备列表只读语义查询，可按设备编码、设备名称、设备状态或产线筛选，适用于“列出 LINE-A 产线设备”。",
                deviceDefinition.GetDefaultProjection(SemanticQueryKind.List).Fields,
                DefaultSortField: "deviceCode",
                DefaultSortDirection: SemanticSortDirection.Asc,
                DefaultLimit: deviceDefinition.DefaultLimit),
            new(
                "Analysis.Device.Detail",
                SemanticQueryTarget.Device,
                SemanticQueryKind.Detail,
                "单台设备详情只读语义查询，要求至少按设备编码或设备标识定位一台设备。",
                deviceDefinition.GetDefaultProjection(SemanticQueryKind.Detail).Fields,
                DefaultSortField: null,
                DefaultSortDirection: SemanticSortDirection.Asc,
                DefaultLimit: 1,
                RequiredAnyFilterFields: ["deviceId", "deviceCode"]),
            new(
                "Analysis.Device.Status",
                SemanticQueryTarget.Device,
                SemanticQueryKind.Status,
                "设备状态只读语义查询，用于查看单台或多台设备当前状态。",
                deviceDefinition.GetDefaultProjection(SemanticQueryKind.Status).Fields,
                DefaultSortField: "updatedAt",
                DefaultSortDirection: SemanticSortDirection.Desc,
                DefaultLimit: deviceDefinition.DefaultLimit),
            new(
                "Analysis.DeviceLog.Latest",
                SemanticQueryTarget.DeviceLog,
                SemanticQueryKind.Latest,
                "最新设备日志只读语义查询，默认按发生时间倒序返回。",
                deviceLogDefinition.GetDefaultProjection(SemanticQueryKind.Latest).Fields,
                DefaultSortField: "occurredAt",
                DefaultSortDirection: SemanticSortDirection.Desc,
                DefaultLimit: deviceLogDefinition.DefaultLimit),
            new(
                "Analysis.DeviceLog.Range",
                SemanticQueryTarget.DeviceLog,
                SemanticQueryKind.Range,
                "设备日志时间范围只读语义查询，必须带发生时间范围。",
                deviceLogDefinition.GetDefaultProjection(SemanticQueryKind.Range).Fields,
                DefaultSortField: "occurredAt",
                DefaultSortDirection: SemanticSortDirection.Desc,
                DefaultLimit: deviceLogDefinition.DefaultLimit,
                RequiresTimeRange: true),
            new(
                "Analysis.DeviceLog.ByLevel",
                SemanticQueryTarget.DeviceLog,
                SemanticQueryKind.ByLevel,
                "按日志级别筛选的设备日志只读语义查询。",
                deviceLogDefinition.GetDefaultProjection(SemanticQueryKind.ByLevel).Fields,
                DefaultSortField: "occurredAt",
                DefaultSortDirection: SemanticSortDirection.Desc,
                DefaultLimit: deviceLogDefinition.DefaultLimit,
                RequiredAllFilterFields: ["level"]),
            new(
                "Analysis.Recipe.List",
                SemanticQueryTarget.Recipe,
                SemanticQueryKind.List,
                "配方列表只读语义查询，可按设备、工序、配方名称或是否生效筛选。",
                recipeDefinition.GetDefaultProjection(SemanticQueryKind.List).Fields,
                DefaultSortField: "updatedAt",
                DefaultSortDirection: SemanticSortDirection.Desc,
                DefaultLimit: recipeDefinition.DefaultLimit),
            new(
                "Analysis.Recipe.Detail",
                SemanticQueryTarget.Recipe,
                SemanticQueryKind.Detail,
                "单个配方详情只读语义查询，至少要能定位配方名称、配方标识或目标设备。",
                recipeDefinition.GetDefaultProjection(SemanticQueryKind.Detail).Fields,
                DefaultSortField: "updatedAt",
                DefaultSortDirection: SemanticSortDirection.Desc,
                DefaultLimit: 1,
                RequiredAnyFilterFields: ["recipeId", "recipeName", "deviceCode"]),
            new(
                "Analysis.Recipe.VersionHistory",
                SemanticQueryTarget.Recipe,
                SemanticQueryKind.VersionHistory,
                "配方版本历史只读语义查询，用于查看同一配方的版本演进和当前生效版本。",
                recipeDefinition.GetDefaultProjection(SemanticQueryKind.VersionHistory).Fields,
                DefaultSortField: "version",
                DefaultSortDirection: SemanticSortDirection.Desc,
                DefaultLimit: recipeDefinition.DefaultLimit,
                RequiredAnyFilterFields: ["recipeId", "recipeName", "deviceCode"]),
            new(
                "Analysis.Capacity.Range",
                SemanticQueryTarget.Capacity,
                SemanticQueryKind.Range,
                "产能时间范围只读语义查询，必须带班次日期或发生时间范围。",
                capacityDefinition.GetDefaultProjection(SemanticQueryKind.Range).Fields,
                DefaultSortField: "occurredAt",
                DefaultSortDirection: SemanticSortDirection.Desc,
                DefaultLimit: capacityDefinition.DefaultLimit,
                RequiresTimeRange: true),
            new(
                "Analysis.Capacity.ByDevice",
                SemanticQueryTarget.Capacity,
                SemanticQueryKind.ByDevice,
                "按设备筛选的产能只读语义查询。",
                capacityDefinition.GetDefaultProjection(SemanticQueryKind.ByDevice).Fields,
                DefaultSortField: "occurredAt",
                DefaultSortDirection: SemanticSortDirection.Desc,
                DefaultLimit: capacityDefinition.DefaultLimit,
                RequiredAnyFilterFields: ["deviceId", "deviceCode"]),
            new(
                "Analysis.Capacity.ByProcess",
                SemanticQueryTarget.Capacity,
                SemanticQueryKind.ByProcess,
                "按工序筛选的产能只读语义查询。",
                capacityDefinition.GetDefaultProjection(SemanticQueryKind.ByProcess).Fields,
                DefaultSortField: "occurredAt",
                DefaultSortDirection: SemanticSortDirection.Desc,
                DefaultLimit: capacityDefinition.DefaultLimit,
                RequiredAllFilterFields: ["processName"]),
            new(
                "Analysis.ProductionData.Latest",
                SemanticQueryTarget.ProductionData,
                SemanticQueryKind.Latest,
                "最新生产数据只读语义查询，默认按发生时间倒序返回。",
                productionDefinition.GetDefaultProjection(SemanticQueryKind.Latest).Fields,
                DefaultSortField: "occurredAt",
                DefaultSortDirection: SemanticSortDirection.Desc,
                DefaultLimit: productionDefinition.DefaultLimit),
            new(
                "Analysis.ProductionData.Range",
                SemanticQueryTarget.ProductionData,
                SemanticQueryKind.Range,
                "生产数据时间范围只读语义查询，必须带发生时间范围。",
                productionDefinition.GetDefaultProjection(SemanticQueryKind.Range).Fields,
                DefaultSortField: "occurredAt",
                DefaultSortDirection: SemanticSortDirection.Desc,
                DefaultLimit: productionDefinition.DefaultLimit,
                RequiresTimeRange: true),
            new(
                "Analysis.ProductionData.ByDevice",
                SemanticQueryTarget.ProductionData,
                SemanticQueryKind.ByDevice,
                "按设备筛选的生产数据只读语义查询。",
                productionDefinition.GetDefaultProjection(SemanticQueryKind.ByDevice).Fields,
                DefaultSortField: "occurredAt",
                DefaultSortDirection: SemanticSortDirection.Desc,
                DefaultLimit: productionDefinition.DefaultLimit,
                RequiredAnyFilterFields: ["deviceId", "deviceCode"])
        ];
    }
}
