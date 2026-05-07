using AICopilot.Services.Contracts;
using AICopilot.Services.Contracts.AiGateway.Dtos;

namespace AICopilot.AiGatewayService.Workflows.Executors;

internal static class SemanticAnalysisPresentation
{
    public static AnalysisDto BuildAnalysis(
        SemanticQueryPlan plan,
        string sourceLabel,
        SemanticSummaryDto semanticSummary,
        bool isTruncated)
    {
        return new AnalysisDto
        {
            SourceLabel = sourceLabel,
            Description = BuildDescription(plan, semanticSummary, isTruncated),
            Metadata = plan.Projection.Fields
                .Select(field => new MetadataItemDto
                {
                    Name = field,
                    Description = GetFieldDescription(field)
                })
                .ToList()
        };
    }

    public static string BuildBusinessSourceLabel(
        string targetLabel,
        DataSourceExternalSystemType externalSystemType)
    {
        return externalSystemType == DataSourceExternalSystemType.CloudReadOnly
            ? $"Cloud {targetLabel}只读视图"
            : $"{targetLabel}只读数据源";
    }

    public static string GetTargetLabel(SemanticQueryTarget target)
    {
        return target switch
        {
            SemanticQueryTarget.Device => "设备",
            SemanticQueryTarget.DeviceLog => "设备日志",
            SemanticQueryTarget.Recipe => "配方",
            SemanticQueryTarget.Capacity => "产能",
            SemanticQueryTarget.ProductionData => "生产数据",
            _ => "业务"
        };
    }

    public static string TryGetTargetLabel(string intent)
    {
        if (intent.StartsWith("Analysis.DeviceLog.", StringComparison.OrdinalIgnoreCase))
        {
            return "设备日志";
        }

        if (intent.StartsWith("Analysis.Device.", StringComparison.OrdinalIgnoreCase))
        {
            return "设备";
        }

        if (intent.StartsWith("Analysis.Recipe.", StringComparison.OrdinalIgnoreCase))
        {
            return "配方";
        }

        if (intent.StartsWith("Analysis.Capacity.", StringComparison.OrdinalIgnoreCase))
        {
            return "产能";
        }

        if (intent.StartsWith("Analysis.ProductionData.", StringComparison.OrdinalIgnoreCase))
        {
            return "生产数据";
        }

        return "业务";
    }

    private static string BuildDescription(
        SemanticQueryPlan plan,
        SemanticSummaryDto semanticSummary,
        bool isTruncated)
    {
        var targetDescription = plan.Target switch
        {
            SemanticQueryTarget.Device => plan.Kind switch
            {
                SemanticQueryKind.List => "设备列表查询",
                SemanticQueryKind.Detail => "设备详情查询",
                SemanticQueryKind.Status => "设备状态查询",
                _ => "设备查询"
            },
            SemanticQueryTarget.DeviceLog => plan.Kind switch
            {
                SemanticQueryKind.Latest => "最新设备日志查询",
                SemanticQueryKind.Range => "设备日志时间范围查询",
                SemanticQueryKind.ByLevel => "设备日志级别查询",
                _ => "设备日志查询"
            },
            SemanticQueryTarget.Recipe => plan.Kind switch
            {
                SemanticQueryKind.List => "配方列表查询",
                SemanticQueryKind.Detail => "配方详情查询",
                SemanticQueryKind.VersionHistory => "配方版本历史查询",
                _ => "配方查询"
            },
            SemanticQueryTarget.Capacity => plan.Kind switch
            {
                SemanticQueryKind.Range => "产能时间范围查询",
                SemanticQueryKind.ByDevice => "设备产能查询",
                SemanticQueryKind.ByProcess => "工序产能查询",
                _ => "产能查询"
            },
            SemanticQueryTarget.ProductionData => plan.Kind switch
            {
                SemanticQueryKind.Latest => "最新生产数据查询",
                SemanticQueryKind.Range => "生产数据时间范围查询",
                SemanticQueryKind.ByDevice => "设备生产数据查询",
                _ => "生产数据查询"
            },
            _ => "业务查询"
        };

        var scope = string.IsNullOrWhiteSpace(semanticSummary.Scope)
            ? "结果上限以内的匹配记录"
            : semanticSummary.Scope;

        var truncationNote = isTruncated ? " 结果已截断。" : string.Empty;
        return $"{targetDescription}，{semanticSummary.Conclusion} 查询范围：{scope}。{truncationNote}";
    }

    private static string GetFieldDescription(string field)
    {
        return field switch
        {
            "deviceId" => "设备标识",
            "deviceCode" => "设备编码",
            "deviceName" => "设备名称",
            "status" => "设备状态",
            "lineName" => "产线",
            "updatedAt" => "时间",
            "logId" => "日志标识",
            "level" => "日志级别",
            "message" => "日志内容",
            "source" => "日志来源",
            "occurredAt" => "时间",
            "recipeId" => "配方标识",
            "recipeName" => "配方名称",
            "processName" => "工序名称",
            "version" => "版本号",
            "isActive" => "当前生效版本",
            "recordId" => "记录标识",
            "shiftDate" => "时间",
            "outputQty" => "总产出",
            "qualifiedQty" => "合格数",
            "barcode" => "条码",
            "stationName" => "工位名称",
            "result" => "生产结果",
            _ => field
        };
    }
}
