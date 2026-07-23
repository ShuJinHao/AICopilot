using System.Text.Json;
using AICopilot.AiGatewayService.Models;
using AICopilot.AiGatewayService.Workflows;
using AICopilot.AiGatewayService.Workflows.Executors;
using Microsoft.Extensions.Logging.Abstractions;

namespace AICopilot.ApplicationTests;

public sealed class SemanticAnalysisRunnerTests
{
    public static TheoryData<string, SemanticQueryTarget> CloudOnlyTargets { get; } = new()
    {
        { "Analysis.Device.Status", SemanticQueryTarget.Device },
        { "Analysis.DeviceLog.Latest", SemanticQueryTarget.DeviceLog },
        { "Analysis.Capacity.Range", SemanticQueryTarget.Capacity },
        { "Analysis.ProductionData.Range", SemanticQueryTarget.ProductionData },
        { "Analysis.Process.List", SemanticQueryTarget.Process },
        { "Analysis.ClientRelease.List", SemanticQueryTarget.ClientRelease }
    };

    [Theory]
    [MemberData(nameof(CloudOnlyTargets))]
    public async Task RunAsync_SixCloudOnlyTargets_ShouldKeepCloudEmptyResultsWithoutFallback(
        string intent,
        SemanticQueryTarget target)
    {
        var planner = new RecordingSemanticQueryPlanner(
            SemanticPlanningResult.Success(CreatePlan(intent, target)));
        var cloudClient = new RecordingCloudAiReadClient(
            isEnabled: true,
            resultFactory: plan => CreateCloudResult(plan.Target, []));
        var runner = CreateRunner(cloudClient, planner);

        var result = await runner.RunAsync(
            new IntentResult { Intent = intent, Query = "查询正式业务数据" },
            sink: null,
            CancellationToken.None);
        var safeOutput = GetSafeOutput(result);

        AssertCloudReadRequested(planner, cloudClient, target);
        safeOutput.Should().Contain("Cloud AiRead");
        using var resultDocument = JsonDocument.Parse(safeOutput);
        resultDocument.RootElement.GetProperty("query_execution").GetProperty("returned_row_count").GetInt32()
            .Should().Be(0);
        resultDocument.RootElement.GetProperty("business_data_preview").GetArrayLength().Should().Be(0);
        safeOutput.Should().NotContain("DataAnalysis/Text-to-SQL 补充分析");
        safeOutput.Should().NotContain("Simulation");
    }

    [Fact]
    public async Task RunAsync_RecipeTarget_ShouldRejectBeforePlannerEvenWhenPlanningWouldFail()
    {
        var planner = new RecordingSemanticQueryPlanner(
            SemanticPlanningResult.Failure("planner must not run for recipe data"));
        var cloudClient = new RecordingCloudAiReadClient(
            isEnabled: true,
            resultFactory: plan => CreateCloudResult(plan.Target, []));
        var runner = CreateRunner(cloudClient, planner);

        var result = await runner.RunAsync(
            new IntentResult { Intent = "Analysis.Recipe.Detail", Query = "查询配方详情" },
            sink: null,
            CancellationToken.None);
        var safeOutput = GetSafeOutput(result);

        safeOutput.Should().Contain(SemanticAnalysisRunner.RecipeDataReadBoundaryMarker);
        safeOutput.Should().Contain("不能查询具体配方");
        AssertNoCloudRead(planner, cloudClient, expectedPlannerCalls: 0);
    }

    [Theory]
    [MemberData(nameof(CloudOnlyTargets))]
    public async Task RunAsync_SixCloudOnlyTargets_ShouldNotFallbackWhenPlanningFails(
        string intent,
        SemanticQueryTarget target)
    {
        var planner = new RecordingSemanticQueryPlanner(
            SemanticPlanningResult.Failure("invalid semantic payload"));
        var cloudClient = new RecordingCloudAiReadClient(
            isEnabled: true,
            resultFactory: plan => CreateCloudResult(plan.Target, []));
        var runner = CreateRunner(cloudClient, planner);

        var result = await runner.RunAsync(
            new IntentResult { Intent = intent, Query = "查询正式业务数据" },
            sink: null,
            CancellationToken.None);
        var safeOutput = GetSafeOutput(result);

        AssertNoCloudRead(planner, cloudClient, expectedPlannerCalls: 1);
        AssertCloudOnlyFailure(safeOutput, target);
    }

    [Theory]
    [MemberData(nameof(CloudOnlyTargets))]
    public async Task RunAsync_SixCloudOnlyTargets_ShouldNotFallbackWhenCloudAiReadIsDisabled(
        string intent,
        SemanticQueryTarget target)
    {
        var planner = new RecordingSemanticQueryPlanner(
            SemanticPlanningResult.Success(CreatePlan(intent, target)));
        var cloudClient = new RecordingCloudAiReadClient(
            isEnabled: false,
            resultFactory: plan => CreateCloudResult(plan.Target, []));
        var runner = CreateRunner(cloudClient, planner);

        var result = await runner.RunAsync(
            new IntentResult { Intent = intent, Query = "查询正式业务数据" },
            sink: null,
            CancellationToken.None);
        var safeOutput = GetSafeOutput(result);

        AssertNoCloudRead(planner, cloudClient, expectedPlannerCalls: 1);
        AssertCloudOnlyFailure(safeOutput, target);
    }

    [Theory]
    [MemberData(nameof(CloudOnlyTargets))]
    public async Task RunAsync_SixCloudOnlyTargets_ShouldReturnStableCloudErrorWithoutFallback(
        string intent,
        SemanticQueryTarget target)
    {
        var planner = new RecordingSemanticQueryPlanner(
            SemanticPlanningResult.Success(CreatePlan(intent, target)));
        var cloudClient = new RecordingCloudAiReadClient(
            isEnabled: true,
            resultFactory: plan => CreateCloudResult(plan.Target, []),
            exception: new CloudAiReadException(
                CloudAiReadProblemCodes.Unavailable,
                "provider detail must stay hidden"));
        var runner = CreateRunner(cloudClient, planner);

        var result = await runner.RunAsync(
            new IntentResult { Intent = intent, Query = "查询正式业务数据" },
            sink: null,
            CancellationToken.None);
        var safeOutput = GetSafeOutput(result);

        AssertCloudReadRequested(planner, cloudClient, target);
        safeOutput.Should().Contain("Cloud AiRead");
        safeOutput.Should().Contain("只读接口暂不可用");
        safeOutput.Should().NotContain("provider detail");
        safeOutput.Should().NotContain("DataAnalysis/Text-to-SQL 补充分析");
        safeOutput.Should().NotContain("Simulation");
    }

    [Theory]
    [InlineData(CloudAiReadProblemCodes.InvalidRequest, "参数不符合正式接口契约")]
    [InlineData(CloudAiReadProblemCodes.Unauthorized, "未通过身份凭据校验")]
    [InlineData(CloudAiReadProblemCodes.Forbidden, "权限或设备范围不足")]
    [InlineData(CloudAiReadProblemCodes.RateLimited, "当前受到限流")]
    [InlineData(CloudAiReadProblemCodes.RequestBlocked, "未通过只读白名单校验")]
    public async Task RunAsync_ShouldMapCloudProblemCodesWithoutLeakingProviderDetail(
        string problemCode,
        string expectedMessage)
    {
        const string intent = "Analysis.Device.Status";
        var planner = new RecordingSemanticQueryPlanner(
            SemanticPlanningResult.Success(CreatePlan(intent, SemanticQueryTarget.Device)));
        var cloudClient = new RecordingCloudAiReadClient(
            isEnabled: true,
            resultFactory: plan => CreateCloudResult(plan.Target, []),
            exception: new CloudAiReadException(problemCode, "provider detail must stay hidden"));
        var runner = CreateRunner(cloudClient, planner);

        var result = await runner.RunAsync(
            new IntentResult { Intent = intent, Query = "查询设备状态" },
            sink: null,
            CancellationToken.None);
        var safeOutput = GetSafeOutput(result);

        safeOutput.Should().Contain(expectedMessage);
        safeOutput.Should().NotContain("provider detail");
        cloudClient.RequestedPlans.Should().ContainSingle();
    }

    [Fact]
    public async Task RunAsync_ShouldEmitDeviceLogWidgets_WhenCloudSemanticQuerySucceeds()
    {
        var rows = new List<Dictionary<string, object?>>
        {
            new()
            {
                ["deviceId"] = "11111111-1111-1111-1111-111111111111",
                ["deviceName"] = "切叠一号线",
                ["level"] = "ERROR",
                ["message"] = "Motor overload",
                ["occurredAt"] = "2026-04-20T11:00:00Z"
            },
            new()
            {
                ["deviceId"] = "11111111-1111-1111-1111-111111111111",
                ["deviceName"] = "切叠一号线",
                ["level"] = "WARN",
                ["message"] = "Temperature high",
                ["occurredAt"] = "2026-04-20T10:00:00Z"
            },
            new()
            {
                ["deviceId"] = "11111111-1111-1111-1111-111111111111",
                ["deviceName"] = "切叠一号线",
                ["level"] = "ERROR",
                ["message"] = "Communication timeout",
                ["occurredAt"] = "2026-04-20T10:30:00Z"
            }
        };
        const string intent = "Analysis.DeviceLog.ByLevel";
        var plan = new SemanticQueryPlan(
            intent,
            SemanticQueryTarget.DeviceLog,
            SemanticQueryKind.ByLevel,
            "查看设备 DEV-001 错误和告警日志",
            new SemanticProjection(["deviceId", "deviceName", "level", "message", "occurredAt"]),
            [
                new SemanticFilter("deviceId", SemanticFilterOperator.Equal, "11111111-1111-1111-1111-111111111111"),
                new SemanticFilter("level", SemanticFilterOperator.In, "ERROR,WARN")
            ],
            null,
            new SemanticSort("occurredAt", SemanticSortDirection.Desc),
            10);
        var planner = new RecordingSemanticQueryPlanner(SemanticPlanningResult.Success(plan));
        var cloudClient = new RecordingCloudAiReadClient(
            isEnabled: true,
            resultFactory: targetPlan => CreateCloudResult(targetPlan.Target, rows));
        var runner = CreateRunner(cloudClient, planner);
        var sink = new AgentWorkflowSink();

        var result = await runner.RunAsync(
            new IntentResult { Intent = intent, Query = plan.QueryText },
            sink,
            CancellationToken.None);
        sink.Complete();
        var chunks = new List<ChatChunk>();
        await foreach (var chunk in sink.ReadAllAsync(CancellationToken.None))
        {
            chunks.Add(chunk);
        }

        chunks.Should().HaveCount(5);
        chunks.Should().OnlyContain(chunk => chunk.Type == ChunkType.Widget);
        using var statsDocument = JsonDocument.Parse(chunks[0].Content);
        statsDocument.RootElement.GetProperty("type").GetString().Should().Be("StatsCard");
        statsDocument.RootElement.GetProperty("data").GetProperty("value").GetInt32().Should().Be(3);

        var levelChartChunk = chunks.Single(chunk =>
        {
            using var document = JsonDocument.Parse(chunk.Content);
            return document.RootElement.GetProperty("title").GetString() == "日志级别分布";
        });
        using var levelChartDocument = JsonDocument.Parse(levelChartChunk.Content);
        levelChartDocument.RootElement.GetProperty("data").GetProperty("dataset").GetProperty("source").EnumerateArray()
            .Should().Contain(row => row.GetProperty("level").GetString() == "ERROR"
                                     && row.GetProperty("count").GetInt32() == 2);

        var tableChunk = chunks.Single(chunk =>
        {
            using var document = JsonDocument.Parse(chunk.Content);
            return document.RootElement.GetProperty("type").GetString() == "DataTable";
        });
        using var tableDocument = JsonDocument.Parse(tableChunk.Content);
        tableDocument.RootElement.GetProperty("data").GetProperty("rows").GetArrayLength().Should().Be(3);
        using var resultDocument = JsonDocument.Parse(GetSafeOutput(result));
        resultDocument.RootElement.GetProperty("display_blocks").GetArrayLength().Should().Be(5);
        cloudClient.RequestedPlans.Should().ContainSingle();
    }

    private static string GetSafeOutput(AgentAnalysisNodeResult result) =>
        result.Evidence?.SafeContext ?? result.SafeMessage ?? string.Empty;

    private static void AssertCloudOnlyFailure(string safeOutput, SemanticQueryTarget target)
    {
        safeOutput.Should().Contain(target == SemanticQueryTarget.Device
            ? SemanticAnalysisRunner.DeviceStatusSourceUnavailableMarker
            : SemanticAnalysisRunner.CloudOnlySemanticSourceUnavailableMarker);
        safeOutput.Should().Contain("不会回退 Direct DB、Text-to-SQL 或 Simulation");
        safeOutput.Should().NotContain("DataAnalysis/Text-to-SQL 补充分析");
    }

    private static void AssertCloudReadRequested(
        RecordingSemanticQueryPlanner planner,
        RecordingCloudAiReadClient cloudClient,
        SemanticQueryTarget target)
    {
        planner.CallCount.Should().Be(1);
        cloudClient.RequestedPlans.Should().ContainSingle().Which.Target.Should().Be(target);
    }

    private static void AssertNoCloudRead(
        RecordingSemanticQueryPlanner planner,
        RecordingCloudAiReadClient cloudClient,
        int expectedPlannerCalls)
    {
        planner.CallCount.Should().Be(expectedPlannerCalls);
        cloudClient.RequestedPlans.Should().BeEmpty();
    }

    private static SemanticAnalysisRunner CreateRunner(
        ICloudAiReadClient cloudAiReadClient,
        ISemanticQueryPlanner planner)
    {
        return new SemanticAnalysisRunner(
            cloudAiReadClient,
            planner,
            NullLogger<SemanticAnalysisRunner>.Instance);
    }

    private static SemanticQueryPlan CreatePlan(string intent, SemanticQueryTarget target)
    {
        var (kind, fields) = target switch
        {
            SemanticQueryTarget.Device =>
                (SemanticQueryKind.Status, new[] { "deviceCode", "runtimeStatus", "lastRuntimeHeartbeatAtUtc" }),
            SemanticQueryTarget.DeviceLog =>
                (SemanticQueryKind.Latest, new[] { "deviceId", "deviceName", "level", "message", "occurredAt" }),
            SemanticQueryTarget.Capacity =>
                (SemanticQueryKind.Range, new[] { "deviceId", "shiftDate", "outputQty", "qualifiedQty" }),
            SemanticQueryTarget.ProductionData =>
                (SemanticQueryKind.Range, new[] { "deviceId", "productCode", "occurredAt" }),
            SemanticQueryTarget.Process =>
                (SemanticQueryKind.List, new[] { "processId", "processCode", "processName" }),
            SemanticQueryTarget.ClientRelease =>
                (SemanticQueryKind.List, new[] { "componentKey", "version", "status" }),
            _ => throw new ArgumentOutOfRangeException(nameof(target), target, null)
        };

        return new SemanticQueryPlan(
            intent,
            target,
            kind,
            "查询正式业务数据",
            new SemanticProjection(fields),
            [],
            null,
            null,
            20);
    }

    private static CloudAiReadResult<object> CreateCloudResult(
        SemanticQueryTarget target,
        IReadOnlyList<Dictionary<string, object?>> rows)
    {
        var sourcePath = target switch
        {
            SemanticQueryTarget.Device => "/api/v1/ai/read/devices/{deviceId}/client-state",
            SemanticQueryTarget.DeviceLog => "/api/v1/ai/read/device-logs",
            SemanticQueryTarget.Capacity => "/api/v1/ai/read/capacity/summary",
            SemanticQueryTarget.ProductionData => "/api/v1/ai/read/production-records",
            SemanticQueryTarget.Process => "/api/v1/ai/read/processes",
            SemanticQueryTarget.ClientRelease => "/api/v1/ai/read/client-releases",
            _ => throw new ArgumentOutOfRangeException(nameof(target), target, null)
        };

        return new CloudAiReadResult<object>(
            sourcePath,
            "Cloud AiRead API",
            DateTimeOffset.Parse("2026-04-20T11:01:00Z"),
            20,
            IsTruncated: false,
            [],
            rows,
            ProviderSource: "Cloud",
            QueryScope: "authorized-scope",
            RowCount: rows.Count);
    }

    private sealed class RecordingSemanticQueryPlanner(SemanticPlanningResult result) : ISemanticQueryPlanner
    {
        public int CallCount { get; private set; }

        public SemanticPlanningResult Plan(string intent, string? query)
        {
            CallCount++;
            return result;
        }
    }

    private sealed class RecordingCloudAiReadClient(
        bool isEnabled,
        Func<SemanticQueryPlan, CloudAiReadResult<object>> resultFactory,
        CloudAiReadException? exception = null) : ICloudAiReadClient
    {
        public bool IsEnabled => isEnabled;

        public List<SemanticQueryPlan> RequestedPlans { get; } = [];

        public Task<CloudAiReadResult<CloudAiReadDeviceDto>> GetDevicesAsync(
            CloudAiReadQuery query,
            CancellationToken cancellationToken = default) => UnexpectedTypedCall<CloudAiReadDeviceDto>();

        public Task<CloudAiReadResult<CloudAiReadProcessDto>> GetProcessesAsync(
            CloudAiReadQuery query,
            CancellationToken cancellationToken = default) => UnexpectedTypedCall<CloudAiReadProcessDto>();

        public Task<CloudAiReadResult<CloudAiReadClientReleaseVersionDto>> GetClientReleasesAsync(
            CloudAiReadQuery query,
            CancellationToken cancellationToken = default) => UnexpectedTypedCall<CloudAiReadClientReleaseVersionDto>();

        public Task<CloudAiReadResult<CloudAiReadDeviceClientStateDto>> GetDeviceClientStatesAsync(
            CloudAiReadQuery query,
            CancellationToken cancellationToken = default) => UnexpectedTypedCall<CloudAiReadDeviceClientStateDto>();

        public Task<CloudAiReadResult<CloudAiReadCapacitySummaryDto>> GetCapacitySummaryAsync(
            CloudAiReadQuery query,
            CancellationToken cancellationToken = default) => UnexpectedTypedCall<CloudAiReadCapacitySummaryDto>();

        public Task<CloudAiReadResult<CloudAiReadCapacityHourlyDto>> GetCapacityHourlyAsync(
            CloudAiReadQuery query,
            CancellationToken cancellationToken = default) => UnexpectedTypedCall<CloudAiReadCapacityHourlyDto>();

        public Task<CloudAiReadResult<CloudAiReadDeviceLogDto>> GetDeviceLogsAsync(
            CloudAiReadQuery query,
            CancellationToken cancellationToken = default) => UnexpectedTypedCall<CloudAiReadDeviceLogDto>();

        public Task<CloudAiReadResult<CloudAiReadProductionRecordDto>> GetProductionRecordsAsync(
            CloudAiReadQuery query,
            CancellationToken cancellationToken = default) => UnexpectedTypedCall<CloudAiReadProductionRecordDto>();

        public Task<CloudAiReadResult<object>> QuerySemanticAsync(
            SemanticQueryPlan plan,
            CancellationToken cancellationToken = default)
        {
            RequestedPlans.Add(plan);
            if (exception is not null)
            {
                throw exception;
            }

            return Task.FromResult(resultFactory(plan));
        }

        private static Task<CloudAiReadResult<T>> UnexpectedTypedCall<T>()
        {
            throw new InvalidOperationException("Semantic runner tests must use QuerySemanticAsync.");
        }
    }
}
