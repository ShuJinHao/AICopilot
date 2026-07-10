using AICopilot.DataAnalysisService.Semantics;
using AICopilot.Services.Contracts;

namespace AICopilot.BackendTests;

public sealed class SemanticQueryPlannerTests
{
    private readonly ISemanticQueryPlanner _planner;

    public SemanticQueryPlannerTests()
    {
        var definitions = new SemanticDefinitionCatalog();
        var intents = new SemanticIntentCatalog(definitions);
        _planner = new SemanticQueryPlanner(intents, definitions);
    }

    public static IEnumerable<object[]> SupportedIntentCases()
    {
        yield return ["Analysis.Device.List", "{\"filters\":[],\"limit\":20}", SemanticQueryTarget.Device, SemanticQueryKind.List];
        yield return ["Analysis.Device.Detail", "{\"filters\":[{\"field\":\"deviceCode\",\"operator\":\"eq\",\"value\":\"DEV-01\"}]}", SemanticQueryTarget.Device, SemanticQueryKind.Detail];
        yield return ["Analysis.Device.Status", "{\"filters\":[{\"field\":\"deviceCode\",\"operator\":\"eq\",\"value\":\"DEV-01\"}]}", SemanticQueryTarget.Device, SemanticQueryKind.Status];
        yield return ["Analysis.DeviceLog.Latest", "{\"filters\":[{\"field\":\"deviceCode\",\"operator\":\"eq\",\"value\":\"DEV-01\"}],\"limit\":10}", SemanticQueryTarget.DeviceLog, SemanticQueryKind.Latest];
        yield return ["Analysis.DeviceLog.Range", "{\"filters\":[{\"field\":\"deviceCode\",\"operator\":\"eq\",\"value\":\"DEV-01\"}],\"timeRange\":{\"field\":\"occurredAt\",\"start\":\"2026-04-20T00:00:00Z\",\"end\":\"2026-04-21T00:00:00Z\"}}", SemanticQueryTarget.DeviceLog, SemanticQueryKind.Range];
        yield return ["Analysis.DeviceLog.ByLevel", "{\"filters\":[{\"field\":\"level\",\"operator\":\"eq\",\"value\":\"ERROR\"}]}", SemanticQueryTarget.DeviceLog, SemanticQueryKind.ByLevel];
        yield return ["Analysis.Recipe.List", "{\"filters\":[{\"field\":\"deviceCode\",\"operator\":\"eq\",\"value\":\"DEV-01\"}]}", SemanticQueryTarget.Recipe, SemanticQueryKind.List];
        yield return ["Analysis.Recipe.Detail", "{\"filters\":[{\"field\":\"recipeName\",\"operator\":\"eq\",\"value\":\"Recipe-Cut-01\"}]}", SemanticQueryTarget.Recipe, SemanticQueryKind.Detail];
        yield return ["Analysis.Recipe.VersionHistory", "{\"filters\":[{\"field\":\"recipeName\",\"operator\":\"eq\",\"value\":\"Recipe-Cut-01\"}],\"sort\":{\"field\":\"version\",\"direction\":\"desc\"}}", SemanticQueryTarget.Recipe, SemanticQueryKind.VersionHistory];
        yield return ["Analysis.Capacity.Range", "{\"filters\":[{\"field\":\"deviceCode\",\"operator\":\"eq\",\"value\":\"DEV-01\"}],\"timeRange\":{\"field\":\"occurredAt\",\"start\":\"2026-04-20T00:00:00Z\",\"end\":\"2026-04-21T00:00:00Z\"}}", SemanticQueryTarget.Capacity, SemanticQueryKind.Range];
        yield return ["Analysis.Capacity.ByDevice", "{\"filters\":[{\"field\":\"deviceCode\",\"operator\":\"eq\",\"value\":\"DEV-01\"}]}", SemanticQueryTarget.Capacity, SemanticQueryKind.ByDevice];
        yield return ["Analysis.ProductionData.Latest", "{\"filters\":[{\"field\":\"deviceCode\",\"operator\":\"eq\",\"value\":\"DEV-01\"}]}", SemanticQueryTarget.ProductionData, SemanticQueryKind.Latest];
        yield return ["Analysis.ProductionData.Range", "{\"filters\":[{\"field\":\"deviceCode\",\"operator\":\"eq\",\"value\":\"DEV-01\"}],\"timeRange\":{\"field\":\"completedAt\",\"start\":\"2026-04-20T00:00:00Z\",\"end\":\"2026-04-21T00:00:00Z\"}}", SemanticQueryTarget.ProductionData, SemanticQueryKind.Range];
        yield return ["Analysis.ProductionData.ByDevice", "{\"filters\":[{\"field\":\"deviceCode\",\"operator\":\"eq\",\"value\":\"DEV-01\"}]}", SemanticQueryTarget.ProductionData, SemanticQueryKind.ByDevice];
        yield return ["Analysis.Process.List", "{\"filters\":[],\"limit\":20}", SemanticQueryTarget.Process, SemanticQueryKind.List];
        yield return ["Analysis.Process.Detail", "{\"filters\":[{\"field\":\"processCode\",\"operator\":\"eq\",\"value\":\"CUT\"}]}", SemanticQueryTarget.Process, SemanticQueryKind.Detail];
        yield return ["Analysis.ClientRelease.List", "{\"filters\":[{\"field\":\"channel\",\"operator\":\"eq\",\"value\":\"stable\"},{\"field\":\"targetRuntime\",\"operator\":\"eq\",\"value\":\"win-x64\"}]}", SemanticQueryTarget.ClientRelease, SemanticQueryKind.List];
    }

    [Theory]
    [MemberData(nameof(SupportedIntentCases))]
    public void Planner_ShouldBuildSemanticPlan_ForAllSupportedBusinessIntents(
        string intent,
        string query,
        SemanticQueryTarget target,
        SemanticQueryKind kind)
    {
        var result = _planner.Plan(intent, query);

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        result.Plan.Should().NotBeNull();
        result.Plan!.Target.Should().Be(target);
        result.Plan.Kind.Should().Be(kind);
        result.Plan.Limit.Should().BeGreaterThan(0);
        result.Plan.Projection.Fields.Should().NotBeEmpty();
    }

    [Theory]
    [InlineData("Analysis.Device.List", "{\"fields\":[\"password\"]}", "projection whitelist")]
    [InlineData("Analysis.DeviceLog.Range", "{\"filters\":[{\"field\":\"deviceCode\",\"operator\":\"eq\",\"value\":\"DEV-01\"}]}", "timeRange")]
    [InlineData("Analysis.Recipe.Detail", "{\"filters\":[{\"field\":\"processName\",\"operator\":\"eq\",\"value\":\"Cutting\"}]}", "recipeId")]
    [InlineData("Analysis.Capacity.ByProcess", "{\"filters\":[{\"field\":\"processName\",\"operator\":\"eq\",\"value\":\"Cutting\"}]}", "Unsupported semantic intent")]
    [InlineData("Analysis.Capacity.Range", "{\"filters\":[{\"field\":\"deviceCode\",\"operator\":\"eq\",\"value\":\"DEV-01\"}]}", "timeRange")]
    [InlineData("Analysis.ProductionData.ByDevice", "{\"filters\":[{\"field\":\"deviceCode\",\"operator\":\"eq\",\"value\":\"DEV-01\"}],\"sort\":{\"field\":\"password\",\"direction\":\"asc\"}}", "Sort field")]
    [InlineData("Analysis.ProductionData.Range", "{\"filters\":[{\"field\":\"deviceCode\",\"operator\":\"eq\",\"value\":\"DEV-01\"}]}", "timeRange")]
    public void Planner_ShouldRejectInvalidSemanticRequests(
        string intent,
        string query,
        string expectedErrorPart)
    {
        var result = _planner.Plan(intent, query);

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain(expectedErrorPart);
    }

    public static IEnumerable<object[]> DeviceLogCompletionCases()
    {
        yield return [
            "Analysis.DeviceLog.ByLevel",
            "查看错误日志",
            SemanticQueryKind.ByLevel,
            "level",
            "ERROR"
        ];
        yield return [
            "Analysis.DeviceLog.ByLevel",
            "{\"queryText\":\"查看所有错误日志\"}",
            SemanticQueryKind.ByLevel,
            "level",
            "ERROR"
        ];
        yield return [
            "Analysis.DeviceLog.ByLevel",
            "{\"queryText\":\"查看设备 DEV-001 错误日志\",\"filters\":[{\"field\":\"deviceCode\",\"operator\":\"eq\",\"value\":\"DEV-001\"}]}",
            SemanticQueryKind.ByLevel,
            "level",
            "ERROR"
        ];
        yield return [
            "Analysis.DeviceLog.Latest",
            "查看警告日志",
            SemanticQueryKind.ByLevel,
            "level",
            "WARN"
        ];
        yield return [
            "Analysis.DeviceLog.Latest",
            "查看信息日志",
            SemanticQueryKind.ByLevel,
            "level",
            "INFO"
        ];
        yield return [
            "Analysis.DeviceLog.ByLevel",
            "{\"queryText\":\"查看错误日志\",\"filters\":[{\"field\":\"level\",\"operator\":\"eq\",\"value\":\"Error\"}]}",
            SemanticQueryKind.ByLevel,
            "level",
            "ERROR"
        ];
    }

    [Theory]
    [MemberData(nameof(DeviceLogCompletionCases))]
    public void Planner_ShouldCompleteDeviceLogLevelSemantics(
        string intent,
        string query,
        SemanticQueryKind expectedKind,
        string expectedFilter,
        string expectedValue)
    {
        var result = _planner.Plan(intent, query);

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        result.Plan.Should().NotBeNull();
        result.Plan!.Target.Should().Be(SemanticQueryTarget.DeviceLog);
        result.Plan.Kind.Should().Be(expectedKind);
        result.Plan.Filters.Should().ContainSingle(filter =>
            filter.Field == expectedFilter &&
            filter.Value == expectedValue &&
            filter.Operator == SemanticFilterOperator.Equal);
    }

    [Theory]
    [InlineData("Analysis.DeviceLog.Latest", "查看错误警告日志")]
    [InlineData("Analysis.DeviceLog.Latest", "帮我分析错误告警日志")]
    [InlineData("Analysis.DeviceLog.ByLevel", "{\"queryText\":\"查看异常日志\",\"filters\":[{\"field\":\"level\",\"operator\":\"eq\",\"value\":\"Error,Warn\"}]}")]
    public void Planner_ShouldCompleteDeviceLogMultiLevelSemantics(
        string intent,
        string query)
    {
        var result = _planner.Plan(intent, query);

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        result.Plan.Should().NotBeNull();
        result.Plan!.Target.Should().Be(SemanticQueryTarget.DeviceLog);
        result.Plan.Kind.Should().Be(SemanticQueryKind.ByLevel);
        result.Plan.Filters.Should().ContainSingle(filter =>
            filter.Field == "level" &&
            filter.Value == "ERROR,WARN" &&
            filter.Operator == SemanticFilterOperator.In);
    }

    [Theory]
    [InlineData("替我查询最近1天的日志并帮我分析错误信息")]
    [InlineData("查看过去24小时错误警告日志")]
    [InlineData("查询近七天告警日志")]
    public void Planner_ShouldCompleteDeviceLogRelativeTimeRange(string query)
    {
        var result = _planner.Plan("Analysis.DeviceLog.Latest", query);

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        result.Plan.Should().NotBeNull();
        result.Plan!.Target.Should().Be(SemanticQueryTarget.DeviceLog);
        result.Plan.TimeRange.Should().NotBeNull();
        result.Plan.TimeRange!.Field.Should().Be("occurredAt");
        result.Plan.TimeRange.Start.Should().NotBeNull();
        result.Plan.TimeRange.End.Should().NotBeNull();
        result.Plan.TimeRange.Start.Should().BeBefore(result.Plan.TimeRange.End!.Value);
    }

    [Fact]
    public void Planner_ShouldNotInventUnsupportedProcessNameFilter_FromNaturalLanguageScope()
    {
        var result = _planner.Plan(
            "Analysis.DeviceLog.Latest",
            "替我查询下模切设备最近1天的日志并帮我分析错误信息");

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        result.Plan.Should().NotBeNull();
        result.Plan!.Filters.Should().NotContain(filter => filter.Field == "processName");
        result.Plan.Filters.Should().Contain(filter =>
            filter.Field == "level" &&
            filter.Operator == SemanticFilterOperator.In &&
            filter.Value == "ERROR,WARN");
        result.Plan.TimeRange.Should().NotBeNull();
    }

    [Fact]
    public void Planner_ShouldNotTreatBroadDeviceInformationAsInfoLevelLog()
    {
        var result = _planner.Plan(
            "Analysis.DeviceLog.Latest",
            "替我查询下模切设备最近的一些信息并帮我整理分类成表格图表");

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        result.Plan.Should().NotBeNull();
        result.Plan!.Target.Should().Be(SemanticQueryTarget.DeviceLog);
        result.Plan.Kind.Should().Be(SemanticQueryKind.Latest);
        result.Plan.Filters.Should().NotContain(filter => filter.Field == "processName");
        result.Plan.Filters.Should().NotContain(filter =>
            filter.Field.Equals("level", StringComparison.OrdinalIgnoreCase));
        result.Plan.Sort.Should().Be(new SemanticSort("occurredAt", SemanticSortDirection.Desc));
    }

    [Fact]
    public void Planner_ShouldNotTreatEnglishInformationAsInfoLevelLog()
    {
        var result = _planner.Plan(
            "Analysis.DeviceLog.Latest",
            "show recent device information and summarize it as a table");

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        result.Plan.Should().NotBeNull();
        result.Plan!.Target.Should().Be(SemanticQueryTarget.DeviceLog);
        result.Plan.Kind.Should().Be(SemanticQueryKind.Latest);
        result.Plan.Filters.Should().NotContain(filter =>
            filter.Field.Equals("level", StringComparison.OrdinalIgnoreCase));
        result.Plan.Sort.Should().Be(new SemanticSort("occurredAt", SemanticSortDirection.Desc));
    }

    [Theory]
    [InlineData("Analysis.DeviceLog.Latest", "查看设备日志")]
    [InlineData("Analysis.DeviceLog.Latest", "查看最近设备日志")]
    [InlineData("Analysis.DeviceLog.ByLevel", "查看设备日志")]
    public void Planner_ShouldUseLatestDeviceLog_WhenLevelIsMissingOrLatestIsRequested(
        string intent,
        string query)
    {
        var result = _planner.Plan(intent, query);

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        result.Plan.Should().NotBeNull();
        result.Plan!.Target.Should().Be(SemanticQueryTarget.DeviceLog);
        result.Plan.Kind.Should().Be(SemanticQueryKind.Latest);
        result.Plan.Filters.Should().NotContain(filter =>
            filter.Field.Equals("level", StringComparison.OrdinalIgnoreCase));
        result.Plan.Sort.Should().Be(new SemanticSort("occurredAt", SemanticSortDirection.Desc));
    }

    [Fact]
    public void Planner_ShouldPreserveInvalidJsonFailures_WhenCompletionCannotSafelyParsePayload()
    {
        var result = _planner.Plan("Analysis.DeviceLog.ByLevel", "{\"queryText\":\"查看错误日志\"");

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("not valid JSON");
    }
}
