using AICopilot.Dapper.Security;
using AICopilot.DataAnalysisService.Semantics;
using AICopilot.Services.Contracts;

namespace AICopilot.BackendTests;

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
        var planningResult = _planner.Plan(
            "Analysis.DeviceLog.Range",
            """
            {
              "fields":["deviceCode","level","message","occurredAt"],
              "filters":[
                {"field":"deviceCode","operator":"eq","value":"DEV-01"},
                {"field":"level","operator":"eq","value":"Error"}
              ],
              "timeRange":{
                "field":"occurredAt",
                "start":"2026-04-20T00:00:00Z",
                "end":"2026-04-21T00:00:00Z"
              },
              "limit":30
            }
            """);

        planningResult.IsSuccess.Should().BeTrue(planningResult.ErrorMessage);
        _mappingProvider.TryGetMapping(SemanticQueryTarget.DeviceLog, out var mapping).Should().BeTrue();

        var sql = _sqlGenerator.Generate(planningResult.Plan!, mapping);

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
    public void SqlGenerator_ShouldGenerateRecipeVersionHistorySql()
    {
        var planningResult = _planner.Plan(
            "Analysis.Recipe.VersionHistory",
            """
            {
              "fields":["recipeName","deviceCode","version","isActive","updatedAt"],
              "filters":[
                {"field":"recipeName","operator":"eq","value":"Recipe-Cut-01"}
              ],
              "limit":20
            }
            """);

        planningResult.IsSuccess.Should().BeTrue(planningResult.ErrorMessage);
        _mappingProvider.TryGetMapping(SemanticQueryTarget.Recipe, out var mapping).Should().BeTrue();

        var sql = _sqlGenerator.Generate(planningResult.Plan!, mapping);

        sql.SqlText.Should().Contain("FROM recipe_view t");
        sql.SqlText.Should().Contain("t.recipe_name = @p0");
        sql.SqlText.Should().Contain("ORDER BY t.version DESC");
        sql.SqlText.Should().Contain("LIMIT 20");
        sql.Parameters.Should().ContainKey("@p0");
    }

    [Fact]
    public void SqlGenerator_ShouldGenerateProductionRangeSql()
    {
        var planningResult = _planner.Plan(
            "Analysis.ProductionData.Range",
            """
            {
              "fields":["deviceCode","barcode","result","occurredAt"],
              "filters":[
                {"field":"deviceCode","operator":"eq","value":"DEV-01"}
              ],
              "timeRange":{
                "field":"occurredAt",
                "start":"2026-04-20T00:00:00Z",
                "end":"2026-04-21T00:00:00Z"
              },
              "limit":30
            }
            """);

        planningResult.IsSuccess.Should().BeTrue(planningResult.ErrorMessage);
        _mappingProvider.TryGetMapping(SemanticQueryTarget.ProductionData, out var mapping).Should().BeTrue();

        var sql = _sqlGenerator.Generate(planningResult.Plan!, mapping);

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
        var planningResult = _planner.Plan(
            "Analysis.DeviceLog.ByLevel",
            """
            {
              "queryText":"查看设备 DEV-001 的错误日志",
              "fields":["deviceCode","level","message","occurredAt"],
              "filters":[
                {"field":"deviceCode","operator":"eq","value":"DEV-001"},
                {"field":"level","operator":"eq","value":"Error"}
              ],
              "limit":20
            }
            """);

        planningResult.IsSuccess.Should().BeTrue(planningResult.ErrorMessage);

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

        var sql = _sqlGenerator.Generate(planningResult.Plan!, mapping);

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
}
