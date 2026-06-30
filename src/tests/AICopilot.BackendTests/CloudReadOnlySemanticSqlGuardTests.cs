using AICopilot.AiGatewayService.Workflows.Executors;

namespace AICopilot.BackendTests;

public sealed class CloudReadOnlySemanticSqlGuardTests
{
    [Fact]
    public void Validate_ShouldAllowAstCoveredColumnReferences()
    {
        var sql =
            """
            SELECT d.client_code, p.process_name, COUNT(l.id) AS log_count
            FROM devices d
            JOIN mfg_processes p ON p.id = d.process_id
            LEFT JOIN device_logs l ON l.device_id = d.id
            WHERE d.client_code = @client_code AND l.level = @level
            GROUP BY d.client_code, p.process_name
            HAVING COUNT(l.id) > 0
            ORDER BY log_count DESC, d.client_code ASC
            LIMIT 10
            """;

        CloudReadOnlySemanticSqlGuard.Validate(sql).Should().BeNull();
    }

    [Fact]
    public void Validate_ShouldRejectDisallowedColumn_InProjectionAndJoin()
    {
        CloudReadOnlySemanticSqlGuard.Validate(
                "SELECT d.device_code FROM devices d LIMIT 10")
            .Should().Contain("Column 'device_code'");

        CloudReadOnlySemanticSqlGuard.Validate(
                "SELECT d.client_code FROM devices d JOIN mfg_processes p ON p.device_code = d.client_code LIMIT 10")
            .Should().Contain("Column 'device_code'");
    }

    [Fact]
    public void Validate_ShouldAllowPostgresLateralDerivedQuery_WhenInnerColumnsAreGoverned()
    {
        var sql =
            """
            SELECT d.device_name, latest_log.level
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
            """;

        CloudReadOnlySemanticSqlGuard.Validate(sql).Should().BeNull();
    }
}
