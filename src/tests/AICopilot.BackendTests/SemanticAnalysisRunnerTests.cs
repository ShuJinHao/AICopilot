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
    public async Task RunAsync_ShouldQueryCloudReadOnlyBusinessDatabase_WhenCloudAiReadIsDisabled()
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
                    ["deviceCode"] = "DEV-001",
                    ["deviceName"] = "切叠一号线"
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
            new SemanticProjection(["deviceCode", "deviceName"]),
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
                ["deviceCode"] = "client_code",
                ["deviceName"] = "device_name"
            },
            ["deviceCode", "deviceName"],
            ["deviceCode", "deviceName"],
            ["deviceCode"],
            databaseName: "CloudPlatformReadonly",
            defaultSort: new SemanticSort("deviceCode", SemanticSortDirection.Asc));
        var runner = new SemanticAnalysisRunner(
            new ThrowingCloudAiReadClient(isEnabled: false),
            databaseReadService,
            databaseConnector,
            new StubSemanticQueryPlanner(plan),
            new StubSemanticPhysicalMappingProvider(mapping),
            new StubSemanticSqlGenerator(new GeneratedSemanticSql(
                "SELECT t.client_code AS deviceCode, t.device_name AS deviceName FROM devices t LIMIT 10",
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

        databaseReadService.WasCalled.Should().BeTrue();
        databaseConnector.WasCalled.Should().BeTrue();
        databaseConnector.LastDatabase.Should().NotBeNull();
        databaseConnector.LastDatabase!.ExternalSystemType.Should().Be(DataSourceExternalSystemType.CloudReadOnly);
        databaseConnector.LastSql.Should().Contain("FROM devices");
        using var resultJson = JsonDocument.Parse(result);
        resultJson.RootElement.GetProperty("source_mode").GetString()
            .Should().Be("DataAnalysis/Text-to-SQL 补充分析");
        resultJson.RootElement.GetProperty("analysis").GetProperty("source_label").GetString()
            .Should().Contain("Cloud");
        result.Should().NotContain("Simulation");
        result.Should().NotContain("simulation");
    }

    [Fact]
    public async Task RunAsync_ShouldPreferCloudReadOnlyBusinessDatabaseForNonHighFrequencyTargets_WhenCloudAiReadIsEnabled()
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
        var runner = new SemanticAnalysisRunner(
            new ThrowingCloudAiReadClient(isEnabled: true),
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

        databaseReadService.WasCalled.Should().BeTrue();
        databaseConnector.WasCalled.Should().BeTrue();
        databaseConnector.LastSql.Should().Contain("FROM devices");
        result.Should().NotContain("Cloud AiRead");
        result.Should().NotContain("Simulation");
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
                    ["deviceId"] = "device-1"
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
            [new SemanticFilter("deviceId", SemanticFilterOperator.Equal, "device-1")],
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
                    ["deviceId"] = "device-1",
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
        var database = new BusinessDatabaseConnectionInfo(
            Guid.NewGuid(),
            "DeviceSemanticReadonly",
            "Device log readonly business data",
            "Host=localhost;Database=semantic;Username=readonly;Password=fake-test-only",
            DatabaseProviderType.PostgreSql,
            IsEnabled: true,
            IsReadOnly: true,
            DataSourceExternalSystemType.NonCloud,
            ReadOnlyCredentialVerified: true);
        var rows = new List<Dictionary<string, object?>>
        {
            new()
            {
                ["deviceCode"] = "DEV-001",
                ["deviceName"] = "切叠一号线",
                ["processName"] = "叠片",
                ["level"] = "ERROR",
                ["message"] = "Motor overload",
                ["source"] = "Cloud",
                ["occurredAt"] = "2026-04-20T11:00:00Z"
            },
            new()
            {
                ["deviceCode"] = "DEV-001",
                ["deviceName"] = "切叠一号线",
                ["processName"] = "叠片",
                ["level"] = "WARN",
                ["message"] = "Temperature high",
                ["source"] = "Cloud",
                ["occurredAt"] = "2026-04-20T10:00:00Z"
            },
            new()
            {
                ["deviceCode"] = "DEV-001",
                ["deviceName"] = "切叠一号线",
                ["processName"] = "叠片",
                ["level"] = "ERROR",
                ["message"] = "Communication timeout",
                ["source"] = "Cloud",
                ["occurredAt"] = "2026-04-20T10:30:00Z"
            }
        };
        var databaseConnector = new RecordingDatabaseConnector(new DatabaseQueryResult(
            rows,
            ReturnedRowCount: rows.Count,
            IsTruncated: false,
            ElapsedMilliseconds: 4));
        var plan = new SemanticQueryPlan(
            "Analysis.DeviceLog.ByLevel",
            SemanticQueryTarget.DeviceLog,
            SemanticQueryKind.ByLevel,
            "查看设备 DEV-001 错误和告警日志",
            new SemanticProjection(["deviceCode", "deviceName", "processName", "level", "message", "source", "occurredAt"]),
            [
                new SemanticFilter("deviceCode", SemanticFilterOperator.Equal, "DEV-001"),
                new SemanticFilter("level", SemanticFilterOperator.In, "ERROR,WARN")
            ],
            null,
            new SemanticSort("occurredAt", SemanticSortDirection.Desc),
            10);
        var mapping = new SemanticPhysicalMapping(
            SemanticQueryTarget.DeviceLog,
            DatabaseProviderType.PostgreSql,
            "device_log_cloud_sim_view",
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["deviceCode"] = "device_code",
                ["deviceName"] = "device_name",
                ["processName"] = "process_name",
                ["level"] = "log_level",
                ["message"] = "log_message",
                ["source"] = "log_source",
                ["occurredAt"] = "occurred_at"
            },
            ["deviceCode", "deviceName", "processName", "level", "message", "source", "occurredAt"],
            ["deviceCode", "deviceName", "processName", "level", "source"],
            ["occurredAt", "level"],
            databaseName: "DeviceSemanticReadonly",
            defaultSort: new SemanticSort("occurredAt", SemanticSortDirection.Desc));
        var runner = new SemanticAnalysisRunner(
            new ThrowingCloudAiReadClient(isEnabled: false),
            new RecordingBusinessDatabaseReadService(database),
            databaseConnector,
            new StubSemanticQueryPlanner(plan),
            new StubSemanticPhysicalMappingProvider(mapping),
            new StubSemanticSqlGenerator(new GeneratedSemanticSql(
                "SELECT device_code AS deviceCode, log_level AS level, log_message AS message, occurred_at AS occurredAt FROM device_log_cloud_sim_view LIMIT 10",
                new Dictionary<string, object?>())),
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
    }

    [Fact]
    public async Task RunAsync_ShouldRejectCloudReadOnlySemanticSql_WhenGeneratedSqlTouchesDisallowedTable()
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

        result.Should().Contain("Cloud 只读安全白名单");
        databaseConnector.WasCalled.Should().BeFalse();
    }

    [Fact]
    public async Task RunAsync_ShouldFallbackToCloudReadOnlyTextToSql_WhenSemanticSqlGuardRejectsGeneratedSql()
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

        databaseConnector.WasCalled.Should().BeTrue();
        databaseConnector.LastSql.Should().Contain("FROM devices");
        databaseConnector.LastSql.Should().NotContain("recipes");
        fallbackGenerator.Requests.Should().ContainSingle();
        using var resultJson = JsonDocument.Parse(result);
        resultJson.RootElement.GetProperty("source_mode").GetString()
            .Should().Be("DataAnalysis/Text-to-SQL 补充分析");
    }

    [Fact]
    public async Task RunAsync_ShouldRejectCloudReadOnlySemanticSql_WhenGeneratedSqlTouchesSensitiveField()
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

        result.Should().Contain("Cloud 只读安全白名单");
        databaseConnector.WasCalled.Should().BeFalse();
    }

    [Fact]
    public async Task RunAsync_ShouldAllowCloudReadOnlySemanticSql_WithPostgresLateralJoin()
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

        databaseConnector.WasCalled.Should().BeTrue();
        result.Should().NotContain("Cloud 只读安全白名单");
    }

    [Fact]
    public async Task RunAsync_ShouldReturnCloudReadOnlyPermissionDiagnostic_WhenReadonlyRoleMissesTableGrant()
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

        result.Should().Contain("CloudReadOnly 只读权限不足");
        result.Should().Contain("mfg_processes");
        result.Should().NotContain("SELECT");
        result.Should().NotContain("Password");
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

        public Task<JsonDocument> SendJsonAsync(
            HttpMethod method,
            string path,
            IReadOnlyDictionary<string, string?>? query = null,
            CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("Cloud AiRead must not be called for recipe data.");
        }

        public Task<CloudAiReadResult<CloudAiReadDeviceDto>> GetDevicesAsync(
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

    private sealed class RecordingCloudAiReadClient(CloudAiReadResult<object> result) : ICloudAiReadClient
    {
        public bool IsEnabled => true;

        public List<SemanticQueryPlan> RequestedPlans { get; } = [];

        public Task<JsonDocument> SendJsonAsync(
            HttpMethod method,
            string path,
            IReadOnlyDictionary<string, string?>? query = null,
            CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("Semantic runner test should use QuerySemanticAsync.");
        }

        public Task<CloudAiReadResult<CloudAiReadDeviceDto>> GetDevicesAsync(
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
