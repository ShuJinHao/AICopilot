using AICopilot.Dapper.Security;
using AICopilot.Services.Contracts;

namespace AICopilot.BackendTests;

[Trait("Suite", "Phase43SafetyQuality")]
public sealed class SqlGuardrailTests
{
    private readonly ISqlGuardrail _guardrail = new AstSqlGuardrail();

    [Theory]
    [InlineData("SELECT * FROM device_master_cloud_sim_view")]
    [InlineData("SELECT ';' AS marker, device_code FROM device_master_cloud_sim_view")]
    [InlineData("SELECT log_message FROM device_logs WHERE log_message LIKE '%UPDATE%'")]
    [InlineData("WITH recent AS (SELECT * FROM device_log_cloud_sim_view) SELECT * FROM recent")]
    public void Validate_ShouldAllowSingleReadOnlyQueries(string sql)
    {
        var result = _guardrail.Validate(sql, DatabaseProviderType.PostgreSql);

        result.IsSafe.Should().BeTrue(result.ErrorMessage);
        result.ErrorMessage.Should().BeNull();
    }

    [Theory]
    [InlineData("SELECT * INTO temp_device_logs FROM device_log_cloud_sim_view", "SELECT INTO")]
    [InlineData("INSERT INTO device_logs(device_id) VALUES (1)", "SELECT")]
    [InlineData("UPDATE device_logs SET level = 'Warn'", "SELECT")]
    [InlineData("DELETE FROM device_logs", "SELECT")]
    [InlineData("DROP TABLE device_logs", "SELECT")]
    [InlineData("ALTER TABLE device_logs ADD COLUMN remark text", "SELECT")]
    [InlineData("TRUNCATE TABLE device_logs", "SELECT")]
    [InlineData("CREATE TABLE demo(id int)", "SELECT")]
    [InlineData("MERGE INTO device_logs USING devices ON true", "SELECT")]
    [InlineData("EXEC dbo.RestartServer", "SELECT")]
    [InlineData("CALL restart_server()", "SELECT")]
    [InlineData("SELECT * FROM device_logs; DELETE FROM device_logs", "多条")]
    [InlineData("SELECT 1 /* ok */; -- next\r\nDELETE FROM device_logs", "多条")]
    [InlineData("DE/*hidden*/LETE FROM device_logs", "SELECT")]
    public void Validate_ShouldRejectUnsafeOrNonQuerySql(string sql, string expectedFragment)
    {
        var result = _guardrail.Validate(sql, DatabaseProviderType.PostgreSql);

        result.IsSafe.Should().BeFalse();
        result.ErrorMessage.Should().Contain(expectedFragment);
    }
}
