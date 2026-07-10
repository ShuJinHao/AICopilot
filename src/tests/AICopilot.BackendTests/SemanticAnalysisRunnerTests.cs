using System.Data;
using System.Text.Json;
using AICopilot.AiGatewayService.Models;
using AICopilot.AiGatewayService.Workflows;
using AICopilot.AiGatewayService.Workflows.Executors;
using Microsoft.Extensions.Logging.Abstractions;

namespace AICopilot.BackendTests;

public sealed class SemanticAnalysisRunnerTests
{
    [Fact]
    public async Task RunAsync_ShouldBlockRecipeDataReadBeforeBusinessDatabaseAccess()
    {
        var databaseReadService = new RecordingBusinessDatabaseReadService();
        var databaseConnector = new RecordingDatabaseConnector();
        var runner = new SemanticAnalysisRunner(
            new ThrowingCloudAiReadClient(),
            databaseReadService,
            databaseConnector,
            new StubSemanticQueryPlanner(CreateRecipePlan()),
            new ThrowingSemanticPhysicalMappingProvider(),
            new ThrowingSemanticSqlGenerator(),
            new DataAnalysisAuditRecorder(new NoopAuditLogWriter()),
            NullLogger<SemanticAnalysisRunner>.Instance);

        var result = await runner.RunAsync(
            new IntentResult
            {
                Intent = "Analysis.Recipe.Detail",
                Query = """{"filters":[{"field":"recipeName","operator":"eq","value":"Recipe-Cut-01"}]}"""
            },
            CancellationToken.None);

        result.Should().Contain("当前 AI 不读取云端配方主数据或配方版本数据");
        result.Should().Contain("不能查询具体配方");
        databaseReadService.WasCalled.Should().BeFalse();
        databaseConnector.WasCalled.Should().BeFalse();
    }

    [Fact]
    public async Task RunAsync_ShouldNotFallbackDeviceMasterData_WhenCloudAiReadIsDisabled()
    {
        var databaseReadService = new RecordingBusinessDatabaseReadService();
        var databaseConnector = new RecordingDatabaseConnector();
        var fallbackGenerator = new FixedCloudReadOnlyTextToSqlGenerator(
            "SELECT d.client_code FROM devices d LIMIT 10");
        var fallbackRunner = new CloudReadOnlyTextToSqlFallbackRunner(
            fallbackGenerator,
            databaseConnector,
            new DataAnalysisAuditRecorder(new NoopAuditLogWriter()));
        var plan = new SemanticQueryPlan(
            "Analysis.Device.List",
            SemanticQueryTarget.Device,
            SemanticQueryKind.List,
            "查询设备列表",
            new SemanticProjection(["deviceCode", "deviceName"]),
            [],
            null,
            new SemanticSort("deviceCode", SemanticSortDirection.Asc),
            10);
        var runner = new SemanticAnalysisRunner(
            new ThrowingCloudAiReadClient(isEnabled: false),
            databaseReadService,
            databaseConnector,
            new StubSemanticQueryPlanner(plan),
            new ThrowingSemanticPhysicalMappingProvider(),
            new ThrowingSemanticSqlGenerator(),
            new DataAnalysisAuditRecorder(new NoopAuditLogWriter()),
            NullLogger<SemanticAnalysisRunner>.Instance,
            fallbackRunner);

        var result = await runner.RunAsync(
            new IntentResult
            {
                Intent = "Analysis.Device.List",
                Query = "查询设备列表"
            },
            CancellationToken.None);

        result.Should().Contain(SemanticAnalysisRunner.CloudOnlySemanticSourceUnavailableMarker);
        result.Should().Contain("不会回退 Direct DB、Text-to-SQL 或 Simulation");
        databaseReadService.WasCalled.Should().BeFalse();
        databaseConnector.WasCalled.Should().BeFalse();
        fallbackGenerator.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task RunAsync_ShouldPreferCloudAiReadForDeviceMasterData_WhenEnabled()
    {
        var database = new BusinessDatabaseConnectionInfo(
            Guid.NewGuid(),
            "CloudPlatformReadonly",
            "Cloud Platform readonly business data",
            "Host=localhost;Database=cloud;Username=readonly;Password=fake-test-only",
            DatabaseProviderType.PostgreSql,
            IsEnabled: true,
            IsReadOnly: true,
            DataSourceExternalSystemType.CloudReadOnly,
            ReadOnlyCredentialVerified: true);
        var databaseReadService = new RecordingBusinessDatabaseReadService(database);
        var databaseConnector = new RecordingDatabaseConnector(new DatabaseQueryResult(
            [
                new Dictionary<string, object?>
                {
                    ["deviceCode"] = "DEV-001"
                }
            ],
            ReturnedRowCount: 1,
            IsTruncated: false,
            ElapsedMilliseconds: 4));
        var plan = new SemanticQueryPlan(
            "Analysis.Device.List",
            SemanticQueryTarget.Device,
            SemanticQueryKind.List,
            "查询设备列表",
            new SemanticProjection(["deviceCode"]),
            [],
            null,
            new SemanticSort("deviceCode", SemanticSortDirection.Asc),
            10);
        var mapping = new SemanticPhysicalMapping(
            SemanticQueryTarget.Device,
            DatabaseProviderType.PostgreSql,
            "devices",
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["deviceCode"] = "client_code"
            },
            ["deviceCode"],
            ["deviceCode"],
            ["deviceCode"],
            databaseName: "CloudPlatformReadonly",
            defaultSort: new SemanticSort("deviceCode", SemanticSortDirection.Asc));
        var cloudAiReadClient = new RecordingCloudAiReadClient(new CloudAiReadResult<object>(
            "/api/v1/ai/read/devices",
            "Cloud AiRead API",
            DateTimeOffset.UtcNow,
            10,
            IsTruncated: false,
            [],
            [new Dictionary<string, object?> { ["deviceCode"] = "DEV-001" }]));
        var runner = new SemanticAnalysisRunner(
            cloudAiReadClient,
            databaseReadService,
            databaseConnector,
            new StubSemanticQueryPlanner(plan),
            new StubSemanticPhysicalMappingProvider(mapping),
            new StubSemanticSqlGenerator(new GeneratedSemanticSql(
                "SELECT t.client_code AS deviceCode FROM devices t LIMIT 10",
                new Dictionary<string, object?>())),
            new DataAnalysisAuditRecorder(new NoopAuditLogWriter()),
            NullLogger<SemanticAnalysisRunner>.Instance);

        var result = await runner.RunAsync(
            new IntentResult
            {
                Intent = "Analysis.Device.List",
                Query = "查询设备列表"
            },
            CancellationToken.None);

        cloudAiReadClient.RequestedPlans.Should().ContainSingle().Which.Target.Should().Be(SemanticQueryTarget.Device);
        databaseReadService.WasCalled.Should().BeFalse();
        databaseConnector.WasCalled.Should().BeFalse();
        result.Should().Contain("Cloud AiRead");
        result.Should().NotContain("Simulation");
    }

    [Fact]
    public async Task RunAsync_ShouldNotFallbackDeviceStatus_WhenCloudAiReadIsDisabled()
    {
        var databaseReadService = new RecordingBusinessDatabaseReadService();
        var databaseConnector = new RecordingDatabaseConnector();
        var plan = new SemanticQueryPlan(
            "Analysis.Device.Status",
            SemanticQueryTarget.Device,
            SemanticQueryKind.Status,
            "设备 DEV-001 最后上报的运行状态",
            new SemanticProjection(["runtimeStatus", "lastRuntimeHeartbeatAtUtc"]),
            [new SemanticFilter("deviceCode", SemanticFilterOperator.Equal, "DEV-001")],
            null,
            null,
            10);
        var runner = new SemanticAnalysisRunner(
            new ThrowingCloudAiReadClient(isEnabled: false),
            databaseReadService,
            databaseConnector,
            new StubSemanticQueryPlanner(plan),
            new ThrowingSemanticPhysicalMappingProvider(),
            new ThrowingSemanticSqlGenerator(),
            new DataAnalysisAuditRecorder(new NoopAuditLogWriter()),
            NullLogger<SemanticAnalysisRunner>.Instance);

        var result = await runner.RunAsync(
            new IntentResult
            {
                Intent = "Analysis.Device.Status",
                Query = "设备 DEV-001 最后上报的运行状态"
            },
            CancellationToken.None);

        result.Should().Contain(SemanticAnalysisRunner.DeviceStatusSourceUnavailableMarker);
        result.Should().Contain("不会回退 Direct DB、Text-to-SQL 或 Simulation");
        databaseReadService.WasCalled.Should().BeFalse();
        databaseConnector.WasCalled.Should().BeFalse();
    }

    [Theory]
    [InlineData("Analysis.Process.List", SemanticQueryTarget.Process, "工序主数据")]
    [InlineData("Analysis.ClientRelease.List", SemanticQueryTarget.ClientRelease, "客户端发布版本")]
    public async Task RunAsync_ShouldNotFallbackCloudOnlyTargets_WhenCloudAiReadIsDisabled(
        string intent,
        SemanticQueryTarget target,
        string targetLabel)
    {
        var databaseReadService = new RecordingBusinessDatabaseReadService();
        var databaseConnector = new RecordingDatabaseConnector();
        var plan = new SemanticQueryPlan(
            intent,
            target,
            SemanticQueryKind.List,
            targetLabel,
            new SemanticProjection(target == SemanticQueryTarget.Process
                ? ["processId", "processCode", "processName"]
                : ["componentKey", "version", "status"]),
            [],
            null,
            null,
            20);
        var runner = new SemanticAnalysisRunner(
            new ThrowingCloudAiReadClient(isEnabled: false),
            databaseReadService,
            databaseConnector,
            new StubSemanticQueryPlanner(plan),
            new ThrowingSemanticPhysicalMappingProvider(),
            new ThrowingSemanticSqlGenerator(),
            new DataAnalysisAuditRecorder(new NoopAuditLogWriter()),
            NullLogger<SemanticAnalysisRunner>.Instance);

        var result = await runner.RunAsync(
            new IntentResult { Intent = intent, Query = targetLabel },
            CancellationToken.None);

        result.Should().Contain(targetLabel);
        result.Should().Contain(SemanticAnalysisRunner.CloudOnlySemanticSourceUnavailableMarker);
        result.Should().Contain("不会回退 Direct DB、Text-to-SQL 或 Simulation");
        databaseReadService.WasCalled.Should().BeFalse();
        databaseConnector.WasCalled.Should().BeFalse();
    }

    [Theory]
    [InlineData("Analysis.Process.List", SemanticQueryTarget.Process)]
    [InlineData("Analysis.ClientRelease.List", SemanticQueryTarget.ClientRelease)]
    public async Task RunAsync_ShouldReturnCloudEmptySetWithoutFallback_WhenCloudOnlyTargetIsEnabled(
        string intent,
        SemanticQueryTarget target)
    {
        var cloudClient = new RecordingCloudAiReadClient(new CloudAiReadResult<object>(
            target == SemanticQueryTarget.Process
                ? "/api/v1/ai/read/processes"
                : "/api/v1/ai/read/client-releases",
            "Cloud AiRead API",
            DateTimeOffset.UtcNow,
            20,
            IsTruncated: false,
            [],
            []));
        var plan = new SemanticQueryPlan(
            intent,
            target,
            SemanticQueryKind.List,
            intent,
            new SemanticProjection(target == SemanticQueryTarget.Process
                ? ["processId", "processCode", "processName"]
                : ["componentKey", "version", "status"]),
            [],
            null,
            null,
            20);
        var databaseReadService = new RecordingBusinessDatabaseReadService();
        var databaseConnector = new RecordingDatabaseConnector();
        var runner = new SemanticAnalysisRunner(
            cloudClient,
            databaseReadService,
            databaseConnector,
            new StubSemanticQueryPlanner(plan),
            new ThrowingSemanticPhysicalMappingProvider(),
            new ThrowingSemanticSqlGenerator(),
            new DataAnalysisAuditRecorder(new NoopAuditLogWriter()),
            NullLogger<SemanticAnalysisRunner>.Instance);

        var result = await runner.RunAsync(
            new IntentResult { Intent = intent, Query = intent },
            CancellationToken.None);

        cloudClient.RequestedPlans.Should().ContainSingle().Which.Target.Should().Be(target);
        result.Should().Contain("Cloud AiRead");
        result.Should().NotContain("Simulation");
        databaseReadService.WasCalled.Should().BeFalse();
        databaseConnector.WasCalled.Should().BeFalse();
    }

    [Theory]
    [InlineData(CloudAiReadProblemCodes.InvalidRequest)]
    [InlineData(CloudAiReadProblemCodes.Unauthorized)]
    [InlineData(CloudAiReadProblemCodes.Forbidden)]
    [InlineData(CloudAiReadProblemCodes.NotFound)]
    [InlineData(CloudAiReadProblemCodes.RateLimited)]
    [InlineData(CloudAiReadProblemCodes.Unavailable)]
    public async Task RunAsync_ShouldReturnStableCloudErrorWithoutFallback(string errorCode)
    {
        var databaseReadService = new RecordingBusinessDatabaseReadService();
        var databaseConnector = new RecordingDatabaseConnector();
        var fallbackGenerator = new FixedCloudReadOnlyTextToSqlGenerator(
            "SELECT d.client_code FROM devices d LIMIT 10");
        var fallbackRunner = new CloudReadOnlyTextToSqlFallbackRunner(
            fallbackGenerator,
            databaseConnector,
            new DataAnalysisAuditRecorder(new NoopAuditLogWriter()));
        var cloudClient = new RecordingCloudAiReadClient(
            new CloudAiReadResult<object>(
                "/api/v1/ai/read/devices",
                "Cloud AiRead API",
                DateTimeOffset.UtcNow,
                20,
                IsTruncated: false,
                [],
                []),
            new CloudAiReadException(errorCode, "provider detail must stay hidden"));
        var plan = new SemanticQueryPlan(
            "Analysis.Device.List",
            SemanticQueryTarget.Device,
            SemanticQueryKind.List,
            "查询设备列表",
            new SemanticProjection(["deviceId", "deviceCode", "deviceName", "processId"]),
            [],
            null,
            new SemanticSort("deviceCode", SemanticSortDirection.Asc),
            20);
        var runner = new SemanticAnalysisRunner(
            cloudClient,
            databaseReadService,
            databaseConnector,
            new StubSemanticQueryPlanner(plan),
            new ThrowingSemanticPhysicalMappingProvider(),
            new ThrowingSemanticSqlGenerator(),
            new DataAnalysisAuditRecorder(new NoopAuditLogWriter()),
            NullLogger<SemanticAnalysisRunner>.Instance,
            fallbackRunner);

        var result = await runner.RunAsync(
            new IntentResult { Intent = plan.Intent, Query = plan.QueryText },
            CancellationToken.None);

        result.Should().Contain("Cloud AiRead");
        result.Should().NotContain("provider detail");
        databaseReadService.WasCalled.Should().BeFalse();
        databaseConnector.WasCalled.Should().BeFalse();
        fallbackGenerator.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task RunAsync_ShouldPreferCloudAiReadForHighFrequencyTargets_WhenMappingExists()
    {
        var database = new BusinessDatabaseConnectionInfo(
            Guid.NewGuid(),
            "CloudPlatformReadonly",
            "Cloud Platform readonly business data",
            "Host=localhost;Database=cloud;Username=readonly;Password=fake-test-only",
            DatabaseProviderType.PostgreSql,
            IsEnabled: true,
            IsReadOnly: true,
            DataSourceExternalSystemType.CloudReadOnly,
            ReadOnlyCredentialVerified: true);
        var databaseReadService = new RecordingBusinessDatabaseReadService(database);
        var databaseConnector = new RecordingDatabaseConnector(new DatabaseQueryResult(
            [
                new Dictionary<string, object?>
                {
                    ["deviceId"] = "11111111-1111-1111-1111-111111111111"
                }
            ],
            ReturnedRowCount: 1,
            IsTruncated: false,
            ElapsedMilliseconds: 4));
        var plan = new SemanticQueryPlan(
            "Analysis.DeviceLog.Latest",
            SemanticQueryTarget.DeviceLog,
            SemanticQueryKind.Latest,
            "查看设备最近日志",
            new SemanticProjection(["deviceId", "deviceName", "level", "message", "occurredAt"]),
            [new SemanticFilter("deviceId", SemanticFilterOperator.Equal, "11111111-1111-1111-1111-111111111111")],
            null,
            new SemanticSort("occurredAt", SemanticSortDirection.Desc),
            10);
        var mapping = new SemanticPhysicalMapping(
            SemanticQueryTarget.DeviceLog,
            DatabaseProviderType.PostgreSql,
            "device_logs",
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["deviceId"] = "device_id",
                ["level"] = "level"
            },
            ["deviceId", "level"],
            ["deviceId", "level"],
            ["occurredAt"],
            databaseName: "CloudPlatformReadonly",
            defaultSort: new SemanticSort("occurredAt", SemanticSortDirection.Desc));
        var cloudAiReadClient = new RecordingCloudAiReadClient(new CloudAiReadResult<object>(
            "/api/v1/ai/read/device-logs",
            "Cloud AiRead API",
            DateTimeOffset.UtcNow,
            10,
            IsTruncated: false,
            [],
            [
                new Dictionary<string, object?>
                {
                    ["deviceId"] = "11111111-1111-1111-1111-111111111111",
                    ["deviceName"] = "叠片一号",
                    ["level"] = "WARN",
                    ["message"] = "Temperature high",
                    ["occurredAt"] = "2026-04-20T10:00:00Z"
                }
            ]));
        var runner = new SemanticAnalysisRunner(
            cloudAiReadClient,
            databaseReadService,
            databaseConnector,
            new StubSemanticQueryPlanner(plan),
            new StubSemanticPhysicalMappingProvider(mapping),
            new StubSemanticSqlGenerator(new GeneratedSemanticSql(
                "SELECT device_id AS deviceId, level FROM device_logs LIMIT 10",
                new Dictionary<string, object?>())),
            new DataAnalysisAuditRecorder(new NoopAuditLogWriter()),
            NullLogger<SemanticAnalysisRunner>.Instance);

        var result = await runner.RunAsync(
            new IntentResult
            {
                Intent = "Analysis.DeviceLog.Latest",
                Query = "查看设备最近日志"
            },
            CancellationToken.None);

        cloudAiReadClient.RequestedPlans.Should().ContainSingle().Which.Target.Should().Be(SemanticQueryTarget.DeviceLog);
        databaseReadService.WasCalled.Should().BeFalse();
        databaseConnector.WasCalled.Should().BeFalse();
        result.Should().Contain("Cloud AiRead API");
        result.Should().NotContain("DataAnalysis/Text-to-SQL 补充分析");
    }

    [Fact]
    public async Task RunAsync_ShouldEmitDeviceLogWidgets_WhenSemanticQuerySucceeds()
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
        var cloudAiReadClient = new RecordingCloudAiReadClient(new CloudAiReadResult<object>(
            "/api/v1/ai/read/device-logs",
            "Cloud AiRead API",
            DateTimeOffset.Parse("2026-04-20T11:01:00Z"),
            10,
            IsTruncated: false,
            [],
            rows,
            ProviderSource: "Cloud",
            QueryScope: "deviceId=present",
            RowCount: rows.Count));
        var databaseReadService = new RecordingBusinessDatabaseReadService();
        var databaseConnector = new RecordingDatabaseConnector();
        var plan = new SemanticQueryPlan(
            "Analysis.DeviceLog.ByLevel",
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
        var runner = new SemanticAnalysisRunner(
            cloudAiReadClient,
            databaseReadService,
            databaseConnector,
            new StubSemanticQueryPlanner(plan),
            new ThrowingSemanticPhysicalMappingProvider(),
            new ThrowingSemanticSqlGenerator(),
            new DataAnalysisAuditRecorder(new NoopAuditLogWriter()),
            NullLogger<SemanticAnalysisRunner>.Instance);
        var sink = new AgentWorkflowSink();

        var result = await runner.RunAsync(
            new IntentResult
            {
                Intent = "Analysis.DeviceLog.ByLevel",
                Query = "查看设备 DEV-001 错误和告警日志"
            },
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
        using var resultDocument = JsonDocument.Parse(result);
        resultDocument.RootElement.GetProperty("display_blocks").GetArrayLength().Should().Be(5);
        cloudAiReadClient.RequestedPlans.Should().ContainSingle();
        databaseReadService.WasCalled.Should().BeFalse();
        databaseConnector.WasCalled.Should().BeFalse();
    }

    [Fact]
    public async Task RunAsync_ShouldNotReachDirectSqlGuard_WhenCloudAiReadIsDisabled()
    {
        var databaseConnector = new RecordingDatabaseConnector();
        var runner = CreateCloudReadOnlySemanticRunner(
            "SELECT t.recipe_name AS deviceName FROM recipes t LIMIT 10",
            databaseConnector);

        var result = await runner.RunAsync(
            new IntentResult
            {
                Intent = "Analysis.Device.List",
                Query = "查询设备列表"
            },
            CancellationToken.None);

        result.Should().Contain(SemanticAnalysisRunner.CloudOnlySemanticSourceUnavailableMarker);
        result.Should().Contain("不会回退 Direct DB、Text-to-SQL 或 Simulation");
        databaseConnector.WasCalled.Should().BeFalse();
    }

    [Fact]
    public async Task RunAsync_ShouldNotInvokeTextToSqlFallback_WhenCloudAiReadIsDisabled()
    {
        var databaseConnector = new RecordingDatabaseConnector(new DatabaseQueryResult(
            [
                new Dictionary<string, object?>
                {
                    ["client_code"] = "DEV-001"
                }
            ],
            ReturnedRowCount: 1,
            IsTruncated: false,
            ElapsedMilliseconds: 3));
        var fallbackGenerator = new FixedCloudReadOnlyTextToSqlGenerator(
            "SELECT d.client_code FROM devices d LIMIT 10");
        var fallbackRunner = new CloudReadOnlyTextToSqlFallbackRunner(
            fallbackGenerator,
            databaseConnector,
            new DataAnalysisAuditRecorder(new NoopAuditLogWriter()));
        var runner = CreateCloudReadOnlySemanticRunner(
            "SELECT t.recipe_name AS deviceName FROM recipes t LIMIT 10",
            databaseConnector,
            fallbackRunner);

        var result = await runner.RunAsync(
            new IntentResult
            {
                Intent = "Analysis.Device.List",
                Query = "查询设备列表"
            },
            CancellationToken.None);

        result.Should().Contain(SemanticAnalysisRunner.CloudOnlySemanticSourceUnavailableMarker);
        databaseConnector.WasCalled.Should().BeFalse();
        fallbackGenerator.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task RunAsync_ShouldNotReachSensitiveDirectSql_WhenCloudAiReadIsDisabled()
    {
        var databaseConnector = new RecordingDatabaseConnector();
        var runner = CreateCloudReadOnlySemanticRunner(
            "SELECT t.bootstrap_secret_hash AS deviceName FROM devices t LIMIT 10",
            databaseConnector);

        var result = await runner.RunAsync(
            new IntentResult
            {
                Intent = "Analysis.Device.List",
                Query = "查询设备列表"
            },
            CancellationToken.None);

        result.Should().Contain(SemanticAnalysisRunner.CloudOnlySemanticSourceUnavailableMarker);
        databaseConnector.WasCalled.Should().BeFalse();
    }

    [Fact]
    public async Task RunAsync_ShouldNotExecuteOtherwiseValidDirectSql_WhenCloudAiReadIsDisabled()
    {
        var databaseConnector = new RecordingDatabaseConnector(new DatabaseQueryResult(
            [
                new Dictionary<string, object?>
                {
                    ["deviceName"] = "切叠一号线"
                }
            ],
            ReturnedRowCount: 1,
            IsTruncated: false,
            ElapsedMilliseconds: 3));
        var runner = CreateCloudReadOnlySemanticRunner(
            """
            SELECT d.device_name AS deviceName
            FROM devices d
            LEFT JOIN LATERAL (
                SELECT l.level, l.log_time
                FROM device_logs l
                WHERE l.device_id = d.id
                ORDER BY l.log_time DESC
                LIMIT 1
            ) latest_log ON true
            ORDER BY d.device_name ASC
            LIMIT 10
            """,
            databaseConnector);

        var result = await runner.RunAsync(
            new IntentResult
            {
                Intent = "Analysis.Device.List",
                Query = "查询设备列表"
            },
            CancellationToken.None);

        result.Should().Contain(SemanticAnalysisRunner.CloudOnlySemanticSourceUnavailableMarker);
        databaseConnector.WasCalled.Should().BeFalse();
    }

    [Fact]
    public async Task RunAsync_ShouldNotTouchReadonlyRole_WhenCloudAiReadIsDisabled()
    {
        var databaseConnector = new RecordingDatabaseConnector(
            exception: new InvalidOperationException("42501: permission denied for table mfg_processes"));
        var runner = CreateCloudReadOnlySemanticRunner(
            """
            SELECT d.device_name AS deviceName
            FROM devices d
            LEFT JOIN mfg_processes mp ON d.process_id = mp.id
            LIMIT 10
            """,
            databaseConnector);

        var result = await runner.RunAsync(
            new IntentResult
            {
                Intent = "Analysis.Device.List",
                Query = "查询模切设备"
            },
            CancellationToken.None);

        result.Should().Contain(SemanticAnalysisRunner.CloudOnlySemanticSourceUnavailableMarker);
        databaseConnector.WasCalled.Should().BeFalse();
    }

    private static SemanticQueryPlan CreateRecipePlan()
    {
        return new SemanticQueryPlan(
            "Analysis.Recipe.Detail",
            SemanticQueryTarget.Recipe,
            SemanticQueryKind.Detail,
            "查看配方 Recipe-Cut-01 详情",
            new SemanticProjection(["recipeName", "version"]),
            [new SemanticFilter("recipeName", SemanticFilterOperator.Equal, "Recipe-Cut-01")],
            null,
            new SemanticSort("updatedAt", SemanticSortDirection.Desc),
            1);
    }

    private static SemanticAnalysisRunner CreateCloudReadOnlySemanticRunner(
        string generatedSql,
        RecordingDatabaseConnector databaseConnector,
        CloudReadOnlyTextToSqlFallbackRunner? fallbackRunner = null)
    {
        var database = new BusinessDatabaseConnectionInfo(
            Guid.NewGuid(),
            "CloudPlatformReadonly",
            "Cloud Platform readonly business data",
            "Host=localhost;Database=cloud;Username=readonly;Password=fake-test-only",
            DatabaseProviderType.PostgreSql,
            IsEnabled: true,
            IsReadOnly: true,
            DataSourceExternalSystemType.CloudReadOnly,
            ReadOnlyCredentialVerified: true);
        var plan = new SemanticQueryPlan(
            "Analysis.Device.List",
            SemanticQueryTarget.Device,
            SemanticQueryKind.List,
            "查询设备列表",
            new SemanticProjection(["deviceName"]),
            [],
            null,
            new SemanticSort("deviceName", SemanticSortDirection.Asc),
            10);
        var mapping = new SemanticPhysicalMapping(
            SemanticQueryTarget.Device,
            DatabaseProviderType.PostgreSql,
            "devices",
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["deviceName"] = "device_name"
            },
            ["deviceName"],
            ["deviceName"],
            ["deviceName"],
            databaseName: "CloudPlatformReadonly",
            defaultSort: new SemanticSort("deviceName", SemanticSortDirection.Asc));

        return new SemanticAnalysisRunner(
            new ThrowingCloudAiReadClient(isEnabled: false),
            new RecordingBusinessDatabaseReadService(database),
            databaseConnector,
            new StubSemanticQueryPlanner(plan),
            new StubSemanticPhysicalMappingProvider(mapping),
            new StubSemanticSqlGenerator(new GeneratedSemanticSql(
                generatedSql,
                new Dictionary<string, object?>())),
            new DataAnalysisAuditRecorder(new NoopAuditLogWriter()),
            NullLogger<SemanticAnalysisRunner>.Instance,
            fallbackRunner);
    }

    private sealed class StubSemanticQueryPlanner(SemanticQueryPlan plan) : ISemanticQueryPlanner
    {
        public SemanticPlanningResult Plan(string intent, string? query)
        {
            return SemanticPlanningResult.Success(plan);
        }
    }

    private sealed class StubSemanticPhysicalMappingProvider(SemanticPhysicalMapping mapping)
        : ISemanticPhysicalMappingProvider
    {
        public bool TryGetMapping(SemanticQueryTarget target, out SemanticPhysicalMapping result)
        {
            result = mapping;
            return target == mapping.Target;
        }
    }

    private sealed class StubSemanticSqlGenerator(GeneratedSemanticSql generatedSql) : ISemanticSqlGenerator
    {
        public GeneratedSemanticSql Generate(SemanticQueryPlan plan, SemanticPhysicalMapping mapping)
        {
            return generatedSql;
        }
    }

    private sealed class ThrowingCloudAiReadClient(bool isEnabled = true) : ICloudAiReadClient
    {
        public bool IsEnabled => isEnabled;

        public Task<CloudAiReadResult<CloudAiReadDeviceDto>> GetDevicesAsync(
            CloudAiReadQuery query,
            CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("Cloud AiRead must not be called for recipe data.");
        }

        public Task<CloudAiReadResult<CloudAiReadProcessDto>> GetProcessesAsync(
            CloudAiReadQuery query,
            CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("Cloud AiRead must not be called for recipe data.");
        }

        public Task<CloudAiReadResult<CloudAiReadClientReleaseVersionDto>> GetClientReleasesAsync(
            CloudAiReadQuery query,
            CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("Cloud AiRead must not be called for recipe data.");
        }

        public Task<CloudAiReadResult<CloudAiReadDeviceClientStateDto>> GetDeviceClientStatesAsync(
            CloudAiReadQuery query,
            CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("Cloud AiRead must not be called for recipe data.");
        }

        public Task<CloudAiReadResult<CloudAiReadCapacitySummaryDto>> GetCapacitySummaryAsync(
            CloudAiReadQuery query,
            CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("Cloud AiRead must not be called for recipe data.");
        }

        public Task<CloudAiReadResult<CloudAiReadCapacityHourlyDto>> GetCapacityHourlyAsync(
            CloudAiReadQuery query,
            CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("Cloud AiRead must not be called for recipe data.");
        }

        public Task<CloudAiReadResult<CloudAiReadDeviceLogDto>> GetDeviceLogsAsync(
            CloudAiReadQuery query,
            CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("Cloud AiRead must not be called for recipe data.");
        }

        public Task<CloudAiReadResult<CloudAiReadProductionRecordDto>> GetProductionRecordsAsync(
            CloudAiReadQuery query,
            CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("Cloud AiRead must not be called for recipe data.");
        }

        public Task<CloudAiReadResult<object>> QuerySemanticAsync(
            SemanticQueryPlan plan,
            CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("Cloud AiRead must not be called for recipe data.");
        }
    }

    private sealed class RecordingCloudAiReadClient(
        CloudAiReadResult<object> result,
        CloudAiReadException? exception = null) : ICloudAiReadClient
    {
        public bool IsEnabled => true;

        public List<SemanticQueryPlan> RequestedPlans { get; } = [];

        public Task<CloudAiReadResult<CloudAiReadDeviceDto>> GetDevicesAsync(
            CloudAiReadQuery query,
            CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("Semantic runner test should use QuerySemanticAsync.");
        }

        public Task<CloudAiReadResult<CloudAiReadProcessDto>> GetProcessesAsync(
            CloudAiReadQuery query,
            CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("Semantic runner test should use QuerySemanticAsync.");
        }

        public Task<CloudAiReadResult<CloudAiReadClientReleaseVersionDto>> GetClientReleasesAsync(
            CloudAiReadQuery query,
            CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("Semantic runner test should use QuerySemanticAsync.");
        }

        public Task<CloudAiReadResult<CloudAiReadDeviceClientStateDto>> GetDeviceClientStatesAsync(
            CloudAiReadQuery query,
            CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("Semantic runner test should use QuerySemanticAsync.");
        }

        public Task<CloudAiReadResult<CloudAiReadCapacitySummaryDto>> GetCapacitySummaryAsync(
            CloudAiReadQuery query,
            CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("Semantic runner test should use QuerySemanticAsync.");
        }

        public Task<CloudAiReadResult<CloudAiReadCapacityHourlyDto>> GetCapacityHourlyAsync(
            CloudAiReadQuery query,
            CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("Semantic runner test should use QuerySemanticAsync.");
        }

        public Task<CloudAiReadResult<CloudAiReadDeviceLogDto>> GetDeviceLogsAsync(
            CloudAiReadQuery query,
            CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("Semantic runner test should use QuerySemanticAsync.");
        }

        public Task<CloudAiReadResult<CloudAiReadProductionRecordDto>> GetProductionRecordsAsync(
            CloudAiReadQuery query,
            CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("Semantic runner test should use QuerySemanticAsync.");
        }

        public Task<CloudAiReadResult<object>> QuerySemanticAsync(
            SemanticQueryPlan plan,
            CancellationToken cancellationToken = default)
        {
            RequestedPlans.Add(plan);
            if (exception is not null)
            {
                throw exception;
            }

            return Task.FromResult(result);
        }
    }

    private sealed class RecordingBusinessDatabaseReadService(BusinessDatabaseConnectionInfo? database = null)
        : IBusinessDatabaseReadService
    {
        public bool WasCalled { get; private set; }

        public Task<IReadOnlyList<BusinessDatabaseDescriptor>> ListEnabledAsync(
            CancellationToken cancellationToken = default)
        {
            WasCalled = true;
            return Task.FromResult<IReadOnlyList<BusinessDatabaseDescriptor>>([]);
        }

        public Task<IReadOnlyList<BusinessDatabaseDescriptor>> ListSelectableAsync(
            DataSourceSelectionMode selectionMode,
            CancellationToken cancellationToken = default)
        {
            WasCalled = true;
            return Task.FromResult<IReadOnlyList<BusinessDatabaseDescriptor>>([]);
        }

        public Task<BusinessDatabaseConnectionInfo?> GetByNameAsync(
            string name,
            CancellationToken cancellationToken = default)
        {
            WasCalled = true;
            return Task.FromResult(
                string.Equals(database?.Name, name, StringComparison.Ordinal)
                    ? database
                    : null);
        }
    }

    private sealed class RecordingDatabaseConnector(DatabaseQueryResult? result = null, Exception? exception = null) : IDatabaseConnector
    {
        public bool WasCalled { get; private set; }
        public BusinessDatabaseConnectionInfo? LastDatabase { get; private set; }
        public string? LastSql { get; private set; }

        public IDbConnection GetConnection(BusinessDatabaseConnectionInfo database)
        {
            WasCalled = true;
            throw new InvalidOperationException("Database connector must not be called for recipe data.");
        }

        public Task<IEnumerable<dynamic>> ExecuteQueryAsync(
            BusinessDatabaseConnectionInfo database,
            string sql,
            object? parameters = null,
            CancellationToken cancellationToken = default)
        {
            WasCalled = true;
            throw new InvalidOperationException("Database connector must not be called for recipe data.");
        }

        public Task<DatabaseQueryResult> ExecuteQueryWithMetadataAsync(
            BusinessDatabaseConnectionInfo database,
            string sql,
            object? parameters = null,
            DatabaseQueryOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            WasCalled = true;
            LastDatabase = database;
            LastSql = sql;
            if (exception is not null)
            {
                throw exception;
            }

            return result is not null
                ? Task.FromResult(result)
                : throw new InvalidOperationException("Database connector must not be called for recipe data.");
        }

        public Task<IEnumerable<dynamic>> GetSchemaInfoAsync(
            BusinessDatabaseConnectionInfo database,
            CancellationToken cancellationToken = default)
        {
            WasCalled = true;
            throw new InvalidOperationException("Database connector must not be called for recipe data.");
        }
    }

    private sealed class ThrowingSemanticPhysicalMappingProvider : ISemanticPhysicalMappingProvider
    {
        public bool TryGetMapping(SemanticQueryTarget target, out SemanticPhysicalMapping mapping)
        {
            throw new InvalidOperationException("Semantic mapping provider must not be called for recipe data.");
        }
    }

    private sealed class ThrowingSemanticSqlGenerator : ISemanticSqlGenerator
    {
        public GeneratedSemanticSql Generate(SemanticQueryPlan plan, SemanticPhysicalMapping mapping)
        {
            throw new InvalidOperationException("Semantic SQL generator must not be called for recipe data.");
        }
    }

    private sealed class FixedCloudReadOnlyTextToSqlGenerator(string sql) : ICloudReadOnlyTextToSqlGenerator
    {
        public List<CloudReadOnlyTextToSqlGenerationRequest> Requests { get; } = [];

        public Task<CloudReadOnlyTextToSqlGenerationResult> GenerateAsync(
            CloudReadOnlyTextToSqlGenerationRequest request,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return Task.FromResult(CloudReadOnlyTextToSqlGenerationResult.Success(sql, "fixed fallback sql"));
        }
    }

    private sealed class NoopAuditLogWriter : IAuditLogWriter
    {
        public Task WriteAsync(AuditLogWriteRequest request, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(0);
        }
    }
}
