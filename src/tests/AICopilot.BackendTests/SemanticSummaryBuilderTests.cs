using AICopilot.AiGatewayService.Workflows;
using AICopilot.DataAnalysisService.Semantics;
using AICopilot.Services.Contracts;

namespace AICopilot.BackendTests;

public sealed class SemanticSummaryBuilderTests
{
    [Fact]
    public void Builder_ShouldSummarizeDeviceRecords()
    {
        var plan = CreatePlan(SemanticQueryTarget.Device, SemanticQueryKind.List);
        var rows = new List<Dictionary<string, object?>>
        {
            CreateRow(("deviceCode", "DEV-001"), ("deviceName", "Cutter A"), ("processId", "process-1")),
            CreateRow(("deviceCode", "DEV-002"), ("deviceName", "Welder B"), ("processId", "process-2")),
            CreateRow(("deviceCode", "DEV-003"), ("deviceName", "Cutter C"), ("processId", "process-1"))
        };

        var summary = SemanticSummaryBuilder.Build(plan, rows);

        summary.Conclusion.Should().Contain("3 台设备");
        summary.Conclusion.Should().Contain("主数据");
        summary.Metrics.Should().NotContain(item => item.Name.Contains("status", StringComparison.OrdinalIgnoreCase));
        summary.Metrics.Should().NotContain(item => item.Name.Contains("line", StringComparison.OrdinalIgnoreCase));
        summary.Highlights.Should().Contain(item => item.Contains("Cutter A"));
        summary.Scope.Should().Contain("结果上限 20 条");
    }

    [Fact]
    public void Builder_ShouldDescribeLastReportedDeviceStatusWithoutOnlineInference()
    {
        var plan = CreatePlan(SemanticQueryTarget.Device, SemanticQueryKind.Status, ("deviceCode", "DEV-001"));
        var rows = new List<Dictionary<string, object?>>
        {
            CreateRow(
                ("deviceName", "Cutter A"),
                ("clientCode", "DEV-001"),
                ("softwareStatus", "Running"),
                ("runtimeStatus", "Running"),
                ("lastRuntimeHeartbeatAtUtc", "2026-07-10T01:02:00Z"))
        };

        var summary = SemanticSummaryBuilder.Build(plan, rows);

        summary.Conclusion.Should().Contain("1 台具有有效运行心跳");
        summary.Conclusion.Should().Contain("陈旧不等于离线或停止");
        summary.Metrics.Should().Contain(item =>
            item.Name == "runtimeStatusBreakdown" && item.Value.Contains("Running 1台"));
        summary.Highlights.Should().ContainSingle().Which.Should().Contain("Cloud 软件状态 Running");
    }

    [Fact]
    public void Builder_ShouldTreatMissingDeviceStatusAsNoReportInsteadOfOffline()
    {
        var plan = CreatePlan(SemanticQueryTarget.Device, SemanticQueryKind.Status, ("deviceCode", "DEV-001"));

        var summary = SemanticSummaryBuilder.Build(plan, []);

        summary.Conclusion.Should().Be("当前授权范围内没有匹配的设备。");
    }

    [Fact]
    public void Builder_ShouldNotUseRecipeSpecificSummaryProfile()
    {
        var plan = CreatePlan(SemanticQueryTarget.Recipe, SemanticQueryKind.VersionHistory, ("recipeName", "Recipe-Cut-01"));
        var rows = new List<Dictionary<string, object?>>
        {
            CreateRow(("recipeName", "Recipe-Cut-01"), ("deviceCode", "DEV-001"), ("processName", "Cutting"), ("version", "V1.0"), ("isActive", false), ("updatedAt", "2026-04-18T08:00:00Z")),
            CreateRow(("recipeName", "Recipe-Cut-01"), ("deviceCode", "DEV-001"), ("processName", "Cutting"), ("version", "V2.0"), ("isActive", true), ("updatedAt", "2026-04-21T08:00:00Z"))
        };

        var summary = SemanticSummaryBuilder.Build(plan, rows);

        summary.Metrics.Should().BeEmpty();
        summary.Highlights.Should().BeEmpty();
        summary.Conclusion.Should().NotContain("Recipe-Cut-01");
        summary.Conclusion.Should().NotContain("V2.0");
    }

    [Fact]
    public void Builder_ShouldSummarizeCapacityRates()
    {
        var plan = CreatePlan(SemanticQueryTarget.Capacity, SemanticQueryKind.ByDevice, ("deviceId", "11111111-1111-1111-1111-111111111111"));
        var rows = new List<Dictionary<string, object?>>
        {
            CreateRow(("outputQty", 118), ("qualifiedQty", 116), ("occurredAt", "2026-04-20T08:00:00Z")),
            CreateRow(("outputQty", 126), ("qualifiedQty", 123), ("occurredAt", "2026-04-21T08:00:00Z"))
        };

        var summary = SemanticSummaryBuilder.Build(plan, rows);

        summary.Metrics.Should().Contain(item => item.Name == "totalOutputQty" && item.Value == "244");
        summary.Metrics.Should().Contain(item => item.Name == "totalQualifiedQty" && item.Value == "239");
        summary.Metrics.Should().Contain(item => item.Name == "qualifiedRate" && item.Value == "97.95%");
    }

    [Fact]
    public void Builder_ShouldSummarizeDeviceLogs()
    {
        var plan = CreatePlan(SemanticQueryTarget.DeviceLog, SemanticQueryKind.ByLevel, ("deviceId", "11111111-1111-1111-1111-111111111111"), ("level", "ERROR"));
        var rows = new List<Dictionary<string, object?>>
        {
            CreateRow(("deviceId", "11111111-1111-1111-1111-111111111111"), ("deviceName", "Cutter A"), ("level", "WARN"), ("message", "Temperature high"), ("occurredAt", "2026-04-20T10:00:00Z")),
            CreateRow(("deviceId", "11111111-1111-1111-1111-111111111111"), ("deviceName", "Cutter A"), ("level", "ERROR"), ("message", "Motor overload"), ("occurredAt", "2026-04-20T11:00:00Z"))
        };

        var summary = SemanticSummaryBuilder.Build(plan, rows);

        summary.Metrics.Should().Contain(item => item.Name == "levelBreakdown" && item.Value.Contains("ERROR 1条"));
        summary.Metrics.Should().Contain(item => item.Name == "latestOccurredAt" && item.Value.Contains("2026-04-20 11:00:00 UTC"));
    }

    [Fact]
    public void Builder_ShouldSummarizeProductionPassFail()
    {
        var plan = CreatePlan(SemanticQueryTarget.ProductionData, SemanticQueryKind.ByDevice, ("deviceId", "11111111-1111-1111-1111-111111111111"));
        var rows = new List<Dictionary<string, object?>>
        {
            CreateRow(("deviceId", "11111111-1111-1111-1111-111111111111"), ("deviceName", "Cutter A"), ("typeKey", "Type-A"), ("typeName", "类型 A"), ("barcode", "CELL-0001"), ("result", "Pass"), ("completedAt", "2026-04-21T09:00:00Z")),
            CreateRow(("deviceId", "11111111-1111-1111-1111-111111111111"), ("deviceName", "Cutter A"), ("typeKey", "Type-B"), ("typeName", "类型 B"), ("barcode", "CELL-0002"), ("result", "Fail"), ("completedAt", "2026-04-21T09:30:00Z"))
        };

        var summary = SemanticSummaryBuilder.Build(plan, rows);

        summary.Metrics.Should().Contain(item => item.Name == "passCount" && item.Value == "1 条");
        summary.Metrics.Should().Contain(item => item.Name == "failCount" && item.Value == "1 条");
        summary.Metrics.Should().Contain(item => item.Name == "passRate" && item.Value == "50.00%");
        summary.Metrics.Should().Contain(item => item.Name == "groupBreakdown" && item.Value.Contains("类型 A 1条"));
    }

    [Fact]
    public void Builder_ShouldReturnMissSummary_ForEmptyResult()
    {
        var plan = CreatePlan(SemanticQueryTarget.Capacity, SemanticQueryKind.Range, ("deviceCode", "DEV-001"));

        var summary = SemanticSummaryBuilder.Build(plan, []);

        summary.Conclusion.Should().Be("当前范围内未命中记录。");
        summary.Metrics.Should().BeEmpty();
        summary.Highlights.Should().BeEmpty();
    }

    [Fact]
    public void Builder_ShouldSummarizeOfficialProcessMasterData()
    {
        var plan = CreatePlan(SemanticQueryTarget.Process, SemanticQueryKind.List);
        var rows = new List<Dictionary<string, object?>>
        {
            CreateRow(("processId", "process-1"), ("processCode", "CUT"), ("processName", "模切")),
            CreateRow(("processId", "process-2"), ("processCode", "STACK"), ("processName", "叠片"))
        };

        var summary = SemanticSummaryBuilder.Build(plan, rows);

        summary.Conclusion.Should().Contain("2 条 Cloud 工序主数据");
        summary.Metrics.Should().ContainSingle(item => item.Name == "totalCount" && item.Value == "2 个");
        summary.Highlights.Should().Contain(item => item.Contains("CUT") && item.Contains("模切"));
    }

    [Fact]
    public void Builder_ShouldSummarizeClientReleasesWithoutInventingDistributionArtifacts()
    {
        var plan = CreatePlan(SemanticQueryTarget.ClientRelease, SemanticQueryKind.List, ("channel", "stable"));
        var rows = new List<Dictionary<string, object?>>
        {
            CreateRow(("componentKey", "edge-host"), ("version", "1.2.3"), ("channel", "stable"), ("targetRuntime", "win-x64"), ("status", "Published"))
        };

        var summary = SemanticSummaryBuilder.Build(plan, rows);

        summary.Conclusion.Should().Contain("正式 Cloud");
        summary.Conclusion.Should().Contain("未生成哈希或下载地址");
        summary.Highlights.Should().ContainSingle().Which.Should().Contain("1.2.3");
        summary.Highlights.Should().NotContain(item => item.Contains("hash", StringComparison.OrdinalIgnoreCase));
        summary.Highlights.Should().NotContain(item => item.Contains("http", StringComparison.OrdinalIgnoreCase));
    }

    private static SemanticQueryPlan CreatePlan(
        SemanticQueryTarget target,
        SemanticQueryKind kind,
        params (string Field, string Value)[] filters)
    {
        return new SemanticQueryPlan(
            $"Analysis.{target}.{kind}",
            target,
            kind,
            "test",
            new SemanticProjection(["deviceCode"]),
            filters.Select(item => new SemanticFilter(item.Field, SemanticFilterOperator.Equal, item.Value)).ToList(),
            null,
            new SemanticSort(target == SemanticQueryTarget.Recipe ? "updatedAt" : "occurredAt", SemanticSortDirection.Desc),
            20);
    }

    private static Dictionary<string, object?> CreateRow(params (string Key, object? Value)[] pairs)
    {
        return pairs.ToDictionary(item => item.Key, item => item.Value, StringComparer.OrdinalIgnoreCase);
    }
}
