using System.Text.Json;
using System.Text.RegularExpressions;
using AICopilot.AiGatewayService.Agents;
using AICopilot.AiGatewayService.Models;
using AICopilot.AiGatewayService.Workflows;
using AICopilot.AiGatewayService.Workflows.Executors;
using AICopilot.DataAnalysisService.BusinessDatabases;
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
            CreateConfirmedIntent(intent, "查询正式业务数据"),
            sink: null,
            CreateSession(),
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
    public async Task RunAsync_LowConfidenceWithoutConfirmedTaskContext_ShouldAskBeforeProvider()
    {
        const string intent = "Analysis.Device.List";
        var planner = new RecordingSemanticQueryPlanner(
            SemanticPlanningResult.Success(CreatePlan(intent, SemanticQueryTarget.Device)));
        var cloudClient = new RecordingCloudAiReadClient(
            isEnabled: true,
            resultFactory: plan => CreateCloudResult(plan.Target, []));
        var runner = CreateRunner(cloudClient, planner);

        var result = await runner.RunAsync(
            new IntentResult
            {
                Intent = intent,
                Query = "设备条件不明确",
                Confidence = 0.42
            },
            sink: null,
            CreateSession(),
            CancellationToken.None);

        result.Status.Should().Be(BranchExecutionStatus.Failed);
        GetSafeOutput(result).Should().Contain("置信度不足");
        cloudClient.RequestedPlans.Should().BeEmpty();
    }

    [Fact]
    public async Task RunAsync_HighConfidenceSessionWithoutExplicitScopeConfirmation_ShouldAskBeforeProvider()
    {
        const string intent = "Analysis.Process.List";
        var planner = new RecordingSemanticQueryPlanner(
            SemanticPlanningResult.Success(CreatePlan(intent, SemanticQueryTarget.Process)));
        var cloudClient = new RecordingCloudAiReadClient(
            isEnabled: true,
            resultFactory: plan => CreateCloudResult(plan.Target, []));
        var runner = CreateRunner(cloudClient, planner);

        var result = await runner.RunAsync(
            new IntentResult
            {
                Intent = intent,
                Query = "查看工序",
                Confidence = 0.99
            },
            sink: null,
            CreateSession(),
            CancellationToken.None);

        result.Status.Should().Be(BranchExecutionStatus.Failed);
        GetSafeOutput(result).Should().Contain("请确认");
        cloudClient.RequestedPlans.Should().BeEmpty();
    }

    [Fact]
    public async Task RunAsync_ConfirmedUnavailablePlugin_ShouldExecuteSameSourceFallback()
    {
        const string intent = "Analysis.Device.List";
        var planner = new RecordingSemanticQueryPlanner(
            SemanticPlanningResult.Success(CreatePlan(intent, SemanticQueryTarget.Device)));
        var cloudClient = new RecordingCloudAiReadClient(
            isEnabled: false,
            resultFactory: plan => CreateCloudResult(plan.Target, []));
        var source = new FixedBusinessDatabaseReadService();
        var fallback = new RecordingTextToSqlFallbackRunner();
        var runner = CreateRunner(
            cloudClient,
            planner,
            businessDatabaseReadService: source,
            fallbackRunner: fallback);

        var result = await runner.RunAsync(
            CreateConfirmedIntent(intent, "查看设备"),
            sink: null,
            CreateSession(),
            CancellationToken.None);

        result.Status.Should().Be(BranchExecutionStatus.Succeeded);
        fallback.Contexts.Should().ContainSingle()
            .Which.DataSourceId.Should().Be(source.Descriptor.Id);
        GetSafeOutput(result).Should().Contain("business_data_preview");
    }

    [Fact]
    public void BusinessDataSourceBindingResolver_ShouldBindFutureNonCloudSourceByKeyAndOptionalId()
    {
        var mes = CreateNonCloudDescriptor(Guid.NewGuid(), "mes-readonly");
        var erp = CreateNonCloudDescriptor(Guid.NewGuid(), "erp-readonly");
        var context = new BusinessQueryContext(
            Guid.NewGuid(),
            "mes-readonly",
            mes.Id,
            DataSourceExternalSystemType.NonCloud,
            BusinessDataCapability.ProductionRecord,
            "查询 MES 生产记录",
            SourceExplicitlySelected: true,
            BusinessQueryConfirmation.Complete,
            ConfirmedAtUtc: DateTimeOffset.UtcNow);

        BusinessDataSourceBindingResolver.Resolve(context, [mes, erp])
            .Should().ContainSingle().Which.Should().Be(mes);
        BusinessDataSourceBindingResolver.Resolve(
                context with { DataSourceId = null },
                [mes, erp])
            .Should().ContainSingle().Which.Should().Be(mes);
        BusinessDataSourceBindingResolver.Resolve(
                context with { DataSourceId = erp.Id },
                [mes, erp])
            .Should().BeEmpty("the confirmed source key and id must both match");
        BusinessDataSourceBindingResolver.Resolve(
                context with { SourceKey = "erp-readonly", DataSourceId = null },
                [mes, erp])
            .Should().ContainSingle().Which.Should().Be(erp);
    }

    [Fact]
    public async Task RunAsync_ExactChallengeReply_ShouldExecuteStoredScopeOnce()
    {
        const string intent = "Analysis.Device.List";
        var plan = CreatePlan(intent, SemanticQueryTarget.Device);
        var planner = new RecordingSemanticQueryPlanner(
            SemanticPlanningResult.Success(plan));
        var cloudClient = new RecordingCloudAiReadClient(
            isEnabled: true,
            resultFactory: targetPlan => CreateCloudResult(targetPlan.Target, []));
        var contextStore = new BusinessQueryContextStore();
        var runner = CreateRunner(cloudClient, planner, contextStore);
        var session = CreateSession();

        var challengeResult = await runner.RunAsync(
            new IntentResult
            {
                Intent = intent,
                Query = "查看全部设备",
                Confidence = 0.99
            },
            sink: null,
            session,
            CancellationToken.None);
        var challengeMessage = GetSafeOutput(challengeResult);
        var token = Regex.Match(
            challengeMessage,
            @"确认查询 (?<token>[0-9a-f]{32})",
            RegexOptions.CultureInvariant).Groups["token"].Value;
        token.Should().HaveLength(32);
        contextStore.TryConfirmPending(
                session.Id,
                $"确认查询 {token}",
                out var confirmed)
            .Should().BeTrue();

        var confirmedResult = await runner.RunAsync(
            new IntentResult
            {
                Intent = intent,
                Query = confirmed.Question,
                Confidence = 1,
                RoutingNote = "server-confirmed-business-query",
                BusinessDataSourceExplicitlySelected = true,
                ConfirmedBusinessQueryContext = BusinessQueryConfirmation.Complete,
                ConfirmedBusinessQuery = confirmed
            },
            sink: null,
            session,
            CancellationToken.None);

        confirmedResult.Status.Should().Be(BranchExecutionStatus.Empty);
        cloudClient.RequestedPlans.Should().ContainSingle();
        contextStore.TryConfirmPending(
                session.Id,
                $"确认查询 {token}",
                out _)
            .Should().BeFalse();
    }

    [Fact]
    public async Task RunAsync_LowConfidenceFollowUp_ShouldReuseConfirmedSourceWithinSameTask()
    {
        const string intent = "Analysis.Device.List";
        var planner = new RecordingSemanticQueryPlanner(
            SemanticPlanningResult.Success(CreatePlan(intent, SemanticQueryTarget.Device)));
        var cloudClient = new RecordingCloudAiReadClient(
            isEnabled: true,
            resultFactory: plan => CreateCloudResult(plan.Target, []));
        var contextStore = new BusinessQueryContextStore();
        var runner = CreateRunner(cloudClient, planner, contextStore);
        var session = CreateSession();

        await runner.RunAsync(
            CreateConfirmedIntent(intent, "确认查询设备", confidence: 0.95),
            sink: null,
            session,
            CancellationToken.None);
        var followUp = await runner.RunAsync(
            new IntentResult
            {
                Intent = intent,
                Query = "继续看这些设备",
                Confidence = 0.42
            },
            sink: null,
            session,
            CancellationToken.None);

        followUp.Status.Should().Be(BranchExecutionStatus.Empty);
        cloudClient.RequestedPlans.Should().HaveCount(2);
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
            new IntentResult { Intent = "Analysis.Recipe.Detail", Query = "查询配方详情", Confidence = 0.9 },
            sink: null,
            CreateSession(),
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
        _ = target;
        var planner = new RecordingSemanticQueryPlanner(
            SemanticPlanningResult.Failure("invalid semantic payload"));
        var cloudClient = new RecordingCloudAiReadClient(
            isEnabled: true,
            resultFactory: plan => CreateCloudResult(plan.Target, []));
        var runner = CreateRunner(cloudClient, planner);

        var result = await runner.RunAsync(
            CreateConfirmedIntent(intent, "查询正式业务数据"),
            sink: null,
            CreateSession(),
            CancellationToken.None);
        var safeOutput = GetSafeOutput(result);

        AssertNoCloudRead(planner, cloudClient, expectedPlannerCalls: 1);
        safeOutput.Should().Contain("尚未形成可执行的结构化");
        safeOutput.Should().NotContain("Text-to-SQL 补充分析");
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
            CreateConfirmedIntent(intent, "查询正式业务数据"),
            sink: null,
            CreateSession(),
            CancellationToken.None);
        var safeOutput = GetSafeOutput(result);

        AssertNoCloudRead(planner, cloudClient, expectedPlannerCalls: 1);
        if (target == SemanticQueryTarget.ClientRelease)
        {
            safeOutput.Should().Contain("Unavailable");
            safeOutput.Should().Contain("capability_fallback_disabled");
        }
        else
        {
            safeOutput.Should().Contain("Text-to-SQL 当前未配置");
        }
        safeOutput.Should().NotContain("Simulation");
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
            CreateConfirmedIntent(intent, "查询正式业务数据"),
            sink: null,
            CreateSession(),
            CancellationToken.None);
        var safeOutput = GetSafeOutput(result);

        AssertCloudReadRequested(planner, cloudClient, target);
        if (target == SemanticQueryTarget.ClientRelease)
        {
            safeOutput.Should().Contain("Unavailable");
            safeOutput.Should().Contain("capability_fallback_disabled");
        }
        else
        {
            safeOutput.Should().Contain("Text-to-SQL 当前未配置");
        }
        safeOutput.Should().NotContain("provider detail");
        safeOutput.Should().NotContain("DataAnalysis/Text-to-SQL 补充分析");
        safeOutput.Should().NotContain("Simulation");
    }

    [Theory]
    [InlineData(CloudAiReadProblemCodes.InvalidRequest, "缺少必要条件")]
    [InlineData(CloudAiReadProblemCodes.Unauthorized, "明确终止")]
    [InlineData(CloudAiReadProblemCodes.Forbidden, "明确终止")]
    [InlineData(CloudAiReadProblemCodes.RateLimited, "Text-to-SQL 当前未配置")]
    [InlineData(CloudAiReadProblemCodes.RequestBlocked, "明确终止")]
    [InlineData("future_cloud_auth_boundary", "明确终止")]
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
            CreateConfirmedIntent(intent, "查询设备状态"),
            sink: null,
            CreateSession(),
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
            new SemanticTimeRange(
                "occurredAt",
                DateTimeOffset.Parse("2026-04-20T00:00:00Z"),
                DateTimeOffset.Parse("2026-04-21T00:00:00Z")),
            new SemanticSort("occurredAt", SemanticSortDirection.Desc),
            10);
        var planner = new RecordingSemanticQueryPlanner(SemanticPlanningResult.Success(plan));
        var cloudClient = new RecordingCloudAiReadClient(
            isEnabled: true,
            resultFactory: targetPlan => CreateCloudResult(targetPlan.Target, rows));
        var runner = CreateRunner(cloudClient, planner);
        var sink = new AgentWorkflowSink();

        var result = await runner.RunAsync(
            CreateConfirmedIntent(intent, plan.QueryText),
            sink,
            CreateSession(),
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
        ISemanticQueryPlanner planner,
        IBusinessQueryContextStore? contextStore = null,
        IBusinessDatabaseReadService? businessDatabaseReadService = null,
        IBusinessTextToSqlFallbackRunner? fallbackRunner = null)
    {
        var profileRegistry = new BusinessDataSourceProfileRegistry(
            [new CloudReadOnlyBusinessDataSourceProfileProvider()]);
        return new SemanticAnalysisRunner(
            planner,
            NullLogger<SemanticAnalysisRunner>.Instance,
            new BusinessQueryProviderRegistry(
                [new CloudAiReadBusinessQueryProvider(cloudAiReadClient)],
                profileRegistry),
            profileRegistry,
            contextStore ?? new BusinessQueryContextStore(),
            businessDatabaseReadService,
            fallbackRunner);
    }

    private static SessionRuntimeSnapshot CreateSession() => new()
    {
        Id = Guid.NewGuid(),
        UserId = Guid.NewGuid(),
        Title = "semantic-runner-test"
    };

    private static BusinessDatabaseDescriptor CreateNonCloudDescriptor(
        Guid id,
        string sourceKey) =>
        new(
            id,
            sourceKey,
            "future external readonly source",
            DatabaseProviderType.PostgreSql,
            IsEnabled: true,
            IsReadOnly: true,
            DataSourceExternalSystemType.NonCloud,
            ReadOnlyCredentialVerified: true);

    private static IntentResult CreateConfirmedIntent(
        string intent,
        string? query,
        double confidence = 0.9)
    {
        return new IntentResult
        {
            Intent = intent,
            Query = query,
            Confidence = confidence,
            BusinessDataSourceExplicitlySelected = true,
            ConfirmedBusinessQueryContext = BusinessQueryConfirmation.Complete
        };
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

        var filters = target switch
        {
            SemanticQueryTarget.DeviceLog or SemanticQueryTarget.Capacity =>
                new[]
                {
                    new SemanticFilter(
                        "deviceId",
                        SemanticFilterOperator.Equal,
                        "11111111-1111-1111-1111-111111111111")
                },
            SemanticQueryTarget.ProductionData =>
                [new SemanticFilter("barcode", SemanticFilterOperator.Equal, "BC-001")],
            _ => []
        };
        var timeRange = target switch
        {
            SemanticQueryTarget.DeviceLog or SemanticQueryTarget.ProductionData =>
                new SemanticTimeRange(
                    "occurredAt",
                    DateTimeOffset.Parse("2026-04-20T00:00:00Z"),
                    DateTimeOffset.Parse("2026-04-21T00:00:00Z")),
            SemanticQueryTarget.Capacity =>
                new SemanticTimeRange(
                    "shiftDate",
                    DateTimeOffset.Parse("2026-04-20T00:00:00Z"),
                    DateTimeOffset.Parse("2026-04-21T00:00:00Z")),
            _ => null
        };

        return new SemanticQueryPlan(
            intent,
            target,
            kind,
            "查询正式业务数据",
            new SemanticProjection(fields),
            filters,
            timeRange,
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

    private sealed class FixedBusinessDatabaseReadService : IBusinessDatabaseReadService
    {
        public BusinessDatabaseDescriptor Descriptor { get; } = new(
            Guid.NewGuid(),
            "CloudPlatformReadonly",
            "Cloud readonly",
            DatabaseProviderType.PostgreSql,
            IsEnabled: true,
            IsReadOnly: true,
            DataSourceExternalSystemType.CloudReadOnly,
            ReadOnlyCredentialVerified: true);

        public Task<IReadOnlyList<BusinessDatabaseDescriptor>> ListEnabledAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<BusinessDatabaseDescriptor>>([Descriptor]);

        public Task<IReadOnlyList<BusinessDatabaseDescriptor>> ListSelectableAsync(
            DataSourceSelectionMode selectionMode,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<BusinessDatabaseDescriptor>>([Descriptor]);

        public Task<BusinessDatabaseConnectionInfo?> GetByNameAsync(
            string name,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<BusinessDatabaseConnectionInfo?>(new BusinessDatabaseConnectionInfo(
                Descriptor.Id,
                Descriptor.Name,
                Descriptor.Description,
                "Host=localhost;Database=cloud;Username=readonly;Password=fake-test-only",
                Descriptor.Provider,
                Descriptor.IsEnabled,
                Descriptor.IsReadOnly,
                Descriptor.ExternalSystemType,
                Descriptor.ReadOnlyCredentialVerified));
    }

    private sealed class RecordingTextToSqlFallbackRunner : IBusinessTextToSqlFallbackRunner
    {
        public List<BusinessQueryContext> Contexts { get; } = [];

        public Task<BusinessTextToSqlFallbackResult> RunAsync(
            BusinessQueryContext context,
            BusinessDatabaseConnectionInfo database,
            string? question,
            int? requestedLimit,
            CancellationToken cancellationToken)
        {
            Contexts.Add(context);
            return Task.FromResult(new BusinessTextToSqlFallbackResult(
                Succeeded: true,
                Context: """{"business_data_preview":[{"client_code":"DEV-001"}]}""",
                Rows: [new Dictionary<string, object?> { ["client_code"] = "DEV-001" }],
                RowCount: 1,
                IsTruncated: false,
                QueryHash: "test-query-hash",
                RepairAttempts: [],
                SafeMessage: "ok"));
        }
    }
}
