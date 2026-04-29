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
            CreateRow(("deviceCode", "DEV-001"), ("deviceName", "Cutter A"), ("status", "Running"), ("lineName", "LINE-A"), ("updatedAt", "2026-04-21T08:00:00Z")),
            CreateRow(("deviceCode", "DEV-002"), ("deviceName", "Welder B"), ("status", "Idle"), ("lineName", "LINE-A"), ("updatedAt", "2026-04-21T09:00:00Z")),
            CreateRow(("deviceCode", "DEV-003"), ("deviceName", "Cutter C"), ("status", "Running"), ("lineName", "LINE-B"), ("updatedAt", "2026-04-21T10:00:00Z"))
        };

        var summary = SemanticSummaryBuilder.Build(plan, rows);

        summary.Conclusion.Should().Contain("3 台设备");
        summary.Metrics.Should().Contain(item => item.Name == "statusBreakdown" && item.Value.Contains("Running 2台"));
        summary.Metrics.Should().Contain(item => item.Name == "lineBreakdown" && item.Value.Contains("LINE-A 2台"));
        summary.Highlights.Should().Contain(item => item.Contains("Cutter A"));
        summary.Scope.Should().Contain("结果上限 20 条");
    }

    [Fact]
    public void Builder_ShouldSummarizeRecipeVersions()
    {
        var plan = CreatePlan(SemanticQueryTarget.Recipe, SemanticQueryKind.VersionHistory, ("recipeName", "Recipe-Cut-01"));
        var rows = new List<Dictionary<string, object?>>
        {
            CreateRow(("recipeName", "Recipe-Cut-01"), ("deviceCode", "DEV-001"), ("processName", "Cutting"), ("version", "V1.0"), ("isActive", false), ("updatedAt", "2026-04-18T08:00:00Z")),
            CreateRow(("recipeName", "Recipe-Cut-01"), ("deviceCode", "DEV-001"), ("processName", "Cutting"), ("version", "V2.0"), ("isActive", true), ("updatedAt", "2026-04-21T08:00:00Z"))
        };

        var summary = SemanticSummaryBuilder.Build(plan, rows);

        summary.Metrics.Should().Contain(item => item.Name == "activeVersion" && item.Value == "V2.0");
        summary.Metrics.Should().Contain(item => item.Name == "versionChain" && item.Value.Contains("V2.0 -> V1.0"));
        summary.Highlights.Should().Contain(item => item.Contains("当前生效版本 是"));
    }

    [Fact]
    public void Builder_ShouldSummarizeCapacityRates()
    {
        var plan = CreatePlan(SemanticQueryTarget.Capacity, SemanticQueryKind.ByDevice, ("deviceCode", "DEV-001"));
        var rows = new List<Dictionary<string, object?>>
        {
            CreateRow(("deviceCode", "DEV-001"), ("processName", "Cutting"), ("outputQty", 118), ("qualifiedQty", 116), ("occurredAt", "2026-04-20T08:00:00Z")),
            CreateRow(("deviceCode", "DEV-001"), ("processName", "Cutting"), ("outputQty", 126), ("qualifiedQty", 123), ("occurredAt", "2026-04-21T08:00:00Z"))
        };

        var summary = SemanticSummaryBuilder.Build(plan, rows);

        summary.Metrics.Should().Contain(item => item.Name == "totalOutputQty" && item.Value == "244");
        summary.Metrics.Should().Contain(item => item.Name == "totalQualifiedQty" && item.Value == "239");
        summary.Metrics.Should().Contain(item => item.Name == "qualifiedRate" && item.Value == "97.95%");
    }

    [Fact]
    public void Builder_ShouldSummarizeDeviceLogs()
    {
        var plan = CreatePlan(SemanticQueryTarget.DeviceLog, SemanticQueryKind.ByLevel, ("deviceCode", "DEV-001"), ("level", "Error"));
        var rows = new List<Dictionary<string, object?>>
        {
            CreateRow(("deviceCode", "DEV-001"), ("level", "Warn"), ("message", "Temperature high"), ("occurredAt", "2026-04-20T10:00:00Z")),
            CreateRow(("deviceCode", "DEV-001"), ("level", "Error"), ("message", "Motor overload"), ("occurredAt", "2026-04-20T11:00:00Z"))
        };

        var summary = SemanticSummaryBuilder.Build(plan, rows);

        summary.Metrics.Should().Contain(item => item.Name == "levelBreakdown" && item.Value.Contains("Error 1条"));
        summary.Metrics.Should().Contain(item => item.Name == "latestOccurredAt" && item.Value.Contains("2026-04-20 11:00:00 UTC"));
    }

    [Fact]
    public void Builder_ShouldSummarizeProductionPassFail()
    {
        var plan = CreatePlan(SemanticQueryTarget.ProductionData, SemanticQueryKind.ByDevice, ("deviceCode", "DEV-001"));
        var rows = new List<Dictionary<string, object?>>
        {
            CreateRow(("deviceCode", "DEV-001"), ("stationName", "Station-A"), ("barcode", "CELL-0001"), ("result", "Pass"), ("occurredAt", "2026-04-21T09:00:00Z")),
            CreateRow(("deviceCode", "DEV-001"), ("stationName", "Station-B"), ("barcode", "CELL-0002"), ("result", "Fail"), ("occurredAt", "2026-04-21T09:30:00Z"))
        };

        var summary = SemanticSummaryBuilder.Build(plan, rows);

        summary.Metrics.Should().Contain(item => item.Name == "passCount" && item.Value == "1 条");
        summary.Metrics.Should().Contain(item => item.Name == "failCount" && item.Value == "1 条");
        summary.Metrics.Should().Contain(item => item.Name == "passRate" && item.Value == "50.00%");
        summary.Metrics.Should().Contain(item => item.Name == "groupBreakdown" && item.Value.Contains("Station-A 1条"));
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
