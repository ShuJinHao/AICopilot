using AICopilot.Dapper.Security;
using AICopilot.DataAnalysisService.Semantics;
using AICopilot.Services.Contracts;
using Microsoft.Extensions.Configuration;

namespace AICopilot.ApplicationTests;

public sealed class SemanticSqlGenerationTests
{
    private readonly ISemanticQueryPlanner _planner;
    private readonly ISemanticSqlGenerator _sqlGenerator;
    private readonly SampleSemanticPhysicalMappingProvider _mappingProvider = new();

    public SemanticSqlGenerationTests()
    {
        var definitions = new SemanticDefinitionCatalog();
        var intents = new SemanticIntentCatalog(definitions);
        _planner = new SemanticQueryPlanner(intents, definitions);
        _sqlGenerator = new SemanticSqlGenerator(new AstSqlGuardrail());
    }

    [Fact]
    public void SqlGenerator_ShouldGenerateParameterizedReadOnlySql_FromSemanticPlan()
    {
        var plan = CreateDirectSqlPlan(
            SemanticQueryTarget.DeviceLog,
            SemanticQueryKind.Range,
            ["deviceCode", "level", "message", "occurredAt"],
            [
                new SemanticFilter("deviceCode", SemanticFilterOperator.Equal, "DEV-01"),
                new SemanticFilter("level", SemanticFilterOperator.Equal, "Error")
            ]);
        _mappingProvider.TryGetMapping(SemanticQueryTarget.DeviceLog, out var mapping).Should().BeTrue();

        var sql = _sqlGenerator.Generate(plan, mapping);

        sql.SqlText.Should().StartWith("SELECT");
        sql.SqlText.Should().Contain("FROM device_log_view t");
        sql.SqlText.Should().Contain("t.device_code = @p");
        sql.SqlText.Should().Contain("t.log_level = @p");
        sql.SqlText.Should().Contain("t.occurred_at >= @p");
        sql.SqlText.Should().Contain("t.occurred_at <= @p");
        sql.SqlText.Should().Contain("ORDER BY t.occurred_at DESC");
        sql.SqlText.Should().Contain("LIMIT 30");
        sql.SqlText.Should().NotContain("DEV-01");
        sql.Parameters.Should().ContainKey("@p0");
        sql.Parameters.Should().ContainKey("@p1");
        sql.Parameters.Should().ContainKey("@p2");
        sql.Parameters.Should().ContainKey("@p3");
    }

    [Fact]
    public void SqlGenerator_ShouldGenerateProductionRangeSql()
    {
        var plan = CreateDirectSqlPlan(
            SemanticQueryTarget.ProductionData,
            SemanticQueryKind.Range,
            ["deviceCode", "barcode", "result", "occurredAt"],
            [new SemanticFilter("deviceCode", SemanticFilterOperator.Equal, "DEV-01")]);
        _mappingProvider.TryGetMapping(SemanticQueryTarget.ProductionData, out var mapping).Should().BeTrue();

        var sql = _sqlGenerator.Generate(plan, mapping);

        sql.SqlText.Should().Contain("FROM production_data_view t");
        sql.SqlText.Should().Contain("t.device_code = @p0");
        sql.SqlText.Should().Contain("t.occurred_at >= @p1");
        sql.SqlText.Should().Contain("t.occurred_at <= @p2");
        sql.SqlText.Should().Contain("ORDER BY t.occurred_at DESC");
        sql.SqlText.Should().Contain("LIMIT 30");
        sql.Parameters.Should().ContainKey("@p0");
        sql.Parameters.Should().ContainKey("@p1");
        sql.Parameters.Should().ContainKey("@p2");
    }

    [Fact]
    public void SqlGenerator_ShouldRejectAccessToUnmappedFields()
    {
        _mappingProvider.TryGetMapping(SemanticQueryTarget.Device, out var mapping).Should().BeTrue();

        var invalidPlan = new SemanticQueryPlan(
            "Analysis.Device.List",
            SemanticQueryTarget.Device,
            SemanticQueryKind.List,
            QueryText: null,
            new SemanticProjection(["password"]),
            [],
            TimeRange: null,
            Sort: null,
            Limit: 20);

        var action = () => _sqlGenerator.Generate(invalidPlan, mapping);

        action.Should().Throw<InvalidOperationException>()
            .WithMessage("*projection whitelist*");
    }

    [Fact]
    public void SqlGenerator_ShouldSupportConfiguredJoinFromClause_ForRealMappings()
    {
        var plan = new SemanticQueryPlan(
            "Analysis.DeviceLog.ByLevel",
            SemanticQueryTarget.DeviceLog,
            SemanticQueryKind.ByLevel,
            "查看设备 DEV-001 的错误日志",
            new SemanticProjection(["deviceCode", "level", "message", "occurredAt"]),
            [
                new SemanticFilter("deviceCode", SemanticFilterOperator.Equal, "DEV-001"),
                new SemanticFilter("level", SemanticFilterOperator.Equal, "Error")
            ],
            null,
            new SemanticSort("occurredAt", SemanticSortDirection.Desc),
            20);

        var mapping = new SemanticPhysicalMapping(
            SemanticQueryTarget.DeviceLog,
            DatabaseProviderType.PostgreSql,
            "device_logs",
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["deviceCode"] = "d.client_code",
                ["level"] = "l.level",
                ["message"] = "l.message",
                ["occurredAt"] = "l.log_time"
            },
            allowedProjectionFields: ["deviceCode", "level", "message", "occurredAt"],
            allowedFilterFields: ["deviceCode", "level"],
            allowedSortFields: ["occurredAt"],
            fromClause: "device_logs l INNER JOIN devices d ON d.id = l.device_id",
            defaultSort: new SemanticSort("occurredAt", SemanticSortDirection.Desc));

        var sql = _sqlGenerator.Generate(plan, mapping);

        sql.SqlText.Should().Contain("FROM device_logs l INNER JOIN devices d ON d.id = l.device_id");
        sql.SqlText.Should().Contain("d.client_code = @p0");
        sql.SqlText.Should().Contain("l.level = @p1");
        sql.SqlText.Should().Contain("ORDER BY l.log_time DESC");
        sql.SqlText.Should().Contain("LIMIT 20");
        sql.SqlText.Should().NotContain("t.");
        sql.Parameters.Should().ContainKey("@p0");
        sql.Parameters.Should().ContainKey("@p1");
    }

    [Fact]
    public void SqlGenerator_ShouldNotInventProcessNameScope_ForRealDeviceLogQuery()
    {
        var planningResult = _planner.Plan(
            "Analysis.DeviceLog.Latest",
            "替我查询下模切设备最近1天的日志并帮我分析错误信息");

        planningResult.IsSuccess.Should().BeTrue(planningResult.ErrorMessage);

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DataAnalysis:CloudReadOnly:Enabled"] = "true"
            })
            .Build();
        var mappingProvider = new ConfiguredSemanticPhysicalMappingProvider(configuration);
        mappingProvider.TryGetMapping(SemanticQueryTarget.DeviceLog, out var mapping).Should().BeTrue();

        var sql = _sqlGenerator.Generate(planningResult.Plan!, mapping);

        sql.SqlText.Should().Contain("FROM device_logs l INNER JOIN devices d ON l.device_id = d.id LEFT JOIN mfg_processes mp ON d.process_id = mp.id");
        sql.SqlText.Should().Contain("d.device_name AS deviceName");
        sql.SqlText.Should().NotContain("mp.process_name AS processName");
        sql.SqlText.Should().NotContain("mp.process_name ILIKE @p");
        sql.SqlText.Should().Contain("l.level IN (");
        sql.SqlText.Should().Contain("l.log_time >= @p");
        sql.SqlText.Should().Contain("l.log_time <= @p");
        sql.Parameters.Values.Should().NotContain("%模切%");
        sql.Parameters.Values.Should().Contain("ERROR");
        sql.Parameters.Values.Should().Contain("WARN");
    }

    [Fact]
    public void SqlGenerator_ShouldUseConfiguredAlias_ForTimeRangeOnRealMappings()
    {
        var plan = new SemanticQueryPlan(
            "Analysis.DeviceLog.Range",
            SemanticQueryTarget.DeviceLog,
            SemanticQueryKind.Range,
            QueryText: null,
            new SemanticProjection(["deviceCode", "level", "message", "occurredAt"]),
            [
                new SemanticFilter("deviceCode", SemanticFilterOperator.Equal, "DEV-001")
            ],
            new SemanticTimeRange(
                "occurredAt",
                DateTimeOffset.Parse("2026-04-20T00:00:00Z"),
                DateTimeOffset.Parse("2026-04-21T00:00:00Z")),
            new SemanticSort("occurredAt", SemanticSortDirection.Desc),
            20);

        var mapping = new SemanticPhysicalMapping(
            SemanticQueryTarget.DeviceLog,
            DatabaseProviderType.PostgreSql,
            "device_logs",
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["deviceCode"] = "d.client_code",
                ["level"] = "l.level",
                ["message"] = "l.message",
                ["occurredAt"] = "l.log_time"
            },
            allowedProjectionFields: ["deviceCode", "level", "message", "occurredAt"],
            allowedFilterFields: ["deviceCode", "level"],
            allowedSortFields: ["occurredAt"],
            fromClause: "device_logs l INNER JOIN devices d ON d.id = l.device_id",
            defaultSort: new SemanticSort("occurredAt", SemanticSortDirection.Desc));

        var sql = _sqlGenerator.Generate(plan, mapping);

        sql.SqlText.Should().Contain("d.client_code = @p0");
        sql.SqlText.Should().Contain("l.log_time >= @p1");
        sql.SqlText.Should().Contain("l.log_time <= @p2");
        sql.SqlText.Should().NotContain("t.l.log_time");
    }

    [Fact]
    public void SqlGenerator_ShouldSupportLiteralFieldMappings_ForRealMappings()
    {
        var plan = new SemanticQueryPlan(
            "Analysis.DeviceLog.Latest",
            SemanticQueryTarget.DeviceLog,
            SemanticQueryKind.Latest,
            QueryText: null,
            new SemanticProjection(["deviceCode", "source", "occurredAt"]),
            [],
            TimeRange: null,
            new SemanticSort("occurredAt", SemanticSortDirection.Desc),
            10);
        var mapping = new SemanticPhysicalMapping(
            SemanticQueryTarget.DeviceLog,
            DatabaseProviderType.PostgreSql,
            "device_logs",
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["deviceCode"] = "d.client_code",
                ["source"] = "'Cloud'",
                ["occurredAt"] = "l.log_time"
            },
            allowedProjectionFields: ["deviceCode", "source", "occurredAt"],
            allowedFilterFields: ["deviceCode"],
            allowedSortFields: ["occurredAt"],
            fromClause: "device_logs l INNER JOIN devices d ON d.id = l.device_id",
            defaultSort: new SemanticSort("occurredAt", SemanticSortDirection.Desc));

        var sql = _sqlGenerator.Generate(plan, mapping);

        sql.SqlText.Should().Contain("'Cloud' AS source");
        sql.SqlText.Should().Contain("d.client_code AS deviceCode");
        sql.SqlText.Should().Contain("ORDER BY l.log_time DESC");
    }

    [Fact]
    public void SqlGenerator_ShouldRejectUnsafeFromClause()
    {
        var mapping = new SemanticPhysicalMapping(
            SemanticQueryTarget.Device,
            DatabaseProviderType.PostgreSql,
            "devices",
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["deviceCode"] = "d.client_code"
            },
            allowedProjectionFields: ["deviceCode"],
            allowedFilterFields: ["deviceCode"],
            allowedSortFields: ["deviceCode"],
            fromClause: "devices d; DROP TABLE devices",
            defaultSort: new SemanticSort("deviceCode", SemanticSortDirection.Asc));

        var plan = new SemanticQueryPlan(
            "Analysis.Device.List",
            SemanticQueryTarget.Device,
            SemanticQueryKind.List,
            QueryText: "列出设备",
            new SemanticProjection(["deviceCode"]),
            [],
            TimeRange: null,
            Sort: null,
            Limit: 10);

        var action = () => _sqlGenerator.Generate(plan, mapping);

        action.Should().Throw<InvalidOperationException>()
            .WithMessage("*unsafe token*");
    }

    [Fact]
    public void KeywordGuardrail_ShouldStillBlockUnsafeFallbackSql()
    {
        var guardrail = new AstSqlGuardrail();

        var result = guardrail.Validate("SELECT * FROM device_log_view; DROP TABLE device_log_view;", DatabaseProviderType.PostgreSql);

        result.IsSafe.Should().BeFalse();
        result.ErrorMessage.Should().Contain("禁止");
    }

    private static SemanticQueryPlan CreateDirectSqlPlan(
        SemanticQueryTarget target,
        SemanticQueryKind kind,
        IReadOnlyList<string> projectionFields,
        IReadOnlyList<SemanticFilter> filters)
    {
        return new SemanticQueryPlan(
            $"Analysis.{target}.{kind}",
            target,
            kind,
            QueryText: null,
            new SemanticProjection(projectionFields),
            filters,
            new SemanticTimeRange(
                "occurredAt",
                DateTimeOffset.Parse("2026-04-20T00:00:00Z"),
                DateTimeOffset.Parse("2026-04-21T00:00:00Z")),
            new SemanticSort("occurredAt", SemanticSortDirection.Desc),
            30);
    }
}
