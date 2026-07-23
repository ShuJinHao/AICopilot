using AICopilot.Dapper.Security;
using AICopilot.Services.Contracts;

namespace AICopilot.InProcessTests;

public sealed class SqlGuardrailTests
{
    private readonly ISqlGuardrail _guardrail = new AstSqlGuardrail();
    private static readonly BusinessQuerySecurityProfile FixtureProfile =
        BusinessQuerySecurityProfile.TableOnly(
            new HashSet<string>(["public"], StringComparer.OrdinalIgnoreCase),
            new HashSet<string>(
                [
                    "device_master_cloud_sim_view",
                    "device_log_cloud_sim_view",
                    "device_logs"
                ],
                StringComparer.OrdinalIgnoreCase),
            new Dictionary<string, IReadOnlySet<string>>(StringComparer.OrdinalIgnoreCase)
            {
                ["device_master_cloud_sim_view"] = new HashSet<string>(
                    ["device_code"],
                    StringComparer.OrdinalIgnoreCase),
                ["device_log_cloud_sim_view"] = new HashSet<string>(
                    ["device_code"],
                    StringComparer.OrdinalIgnoreCase),
                ["device_logs"] = new HashSet<string>(
                    ["log_message"],
                    StringComparer.OrdinalIgnoreCase)
            },
            new HashSet<string>(StringComparer.OrdinalIgnoreCase));

    [Theory]
    [InlineData("SELECT device_code FROM public.device_master_cloud_sim_view")]
    [InlineData("SELECT ';' AS marker, device_code FROM public.device_master_cloud_sim_view")]
    [InlineData("SELECT log_message FROM public.device_logs WHERE log_message LIKE '%UPDATE%'")]
    [InlineData("WITH recent AS (SELECT device_code FROM public.device_log_cloud_sim_view) SELECT device_code FROM recent")]
    public void Validate_ShouldAllowSingleReadOnlyQueries(string sql)
    {
        var result = _guardrail.Validate(
            sql,
            DatabaseProviderType.PostgreSql,
            FixtureProfile);

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
        var result = _guardrail.Validate(
            sql,
            DatabaseProviderType.PostgreSql,
            FixtureProfile);

        result.IsSafe.Should().BeFalse();
        result.ErrorMessage.Should().Contain(expectedFragment);
    }

    [Theory]
    [InlineData("SELECT client_code FROM public.devices LIMIT 10")]
    [InlineData("SELECT d.client_code FROM public.devices d WHERE d.device_name = 'UPDATE' LIMIT 10")]
    public void Validate_ShouldApplyCloudProfileAtSharedAstOwner(string sql)
    {
        var result = _guardrail.Validate(
            sql,
            DatabaseProviderType.PostgreSql,
            StandardBusinessDataSourceProfiles.CloudReadOnly.QuerySecurity);

        result.IsSafe.Should().BeTrue(result.ErrorMessage);
    }

    [Theory]
    [InlineData(DatabaseProviderType.PostgreSql, "SELECT d.client_code FROM public.devices d")]
    [InlineData(DatabaseProviderType.SqlServer, "SELECT d.client_code FROM dbo.devices d")]
    [InlineData(DatabaseProviderType.MySql, "SELECT d.client_code FROM public.devices d")]
    public void Validate_ShouldApplyColumnAllowlistAcrossSupportedDialects(
        DatabaseProviderType provider,
        string sql)
    {
        var profile = BusinessQuerySecurityProfile.TableOnly(
            new HashSet<string>(["public", "dbo"], StringComparer.OrdinalIgnoreCase),
            new HashSet<string>(["devices"], StringComparer.OrdinalIgnoreCase),
            new Dictionary<string, IReadOnlySet<string>>(StringComparer.OrdinalIgnoreCase)
            {
                ["devices"] = new HashSet<string>(["client_code"], StringComparer.OrdinalIgnoreCase)
            },
            new HashSet<string>(StringComparer.OrdinalIgnoreCase));

        var allowed = _guardrail.Validate(sql, provider, profile);
        var denied = _guardrail.Validate(
            sql.Replace("client_code", "bootstrap_secret", StringComparison.Ordinal),
            provider,
            profile);

        allowed.IsSafe.Should().BeTrue(allowed.ErrorMessage);
        denied.IsSafe.Should().BeFalse();
        denied.ErrorMessage.Should().Contain("Column 'bootstrap_secret'");
    }

    [Theory]
    [InlineData("SELECT * FROM public.devices", "通配符")]
    [InlineData("SELECT device_code FROM public.devices", "Column 'device_code'")]
    [InlineData("SELECT client_code FROM public.recipes", "not allowed")]
    [InlineData("SELECT table_name FROM information_schema.tables", "系统目录")]
    [InlineData("SELECT bootstrap_secret_hash FROM public.devices", "敏感字段")]
    [InlineData("SELECT 1", "必须引用")]
    [InlineData("SELECT pg_read_file('/etc/passwd') FROM public.devices", "Function")]
    [InlineData("SELECT current_setting('data_directory') FROM public.devices", "Function")]
    [InlineData("SELECT pg_ls_dir('.') FROM public.devices", "Function")]
    public void Validate_ShouldRejectProfileViolationsAtSharedAstOwner(
        string sql,
        string expectedFragment)
    {
        var result = _guardrail.Validate(
            sql,
            DatabaseProviderType.PostgreSql,
            StandardBusinessDataSourceProfiles.CloudReadOnly.QuerySecurity);

        result.IsSafe.Should().BeFalse();
        result.ErrorMessage.Should().Contain(expectedFragment);
    }

    [Fact]
    public void Validate_ShouldApplyCloudColumnProfileAcrossJoinGroupingAndOrdering()
    {
        var sql =
            """
            SELECT d.client_code, p.process_name, COUNT(l.id) AS log_count
            FROM public.devices d
            JOIN public.mfg_processes p ON p.id = d.process_id
            LEFT JOIN public.device_logs l ON l.device_id = d.id
            WHERE d.client_code = @client_code AND l.level = @level
            GROUP BY d.client_code, p.process_name
            HAVING COUNT(l.id) > 0
            ORDER BY log_count DESC, d.client_code ASC
            LIMIT 10
            """;

        var result = _guardrail.Validate(
            sql,
            DatabaseProviderType.PostgreSql,
            StandardBusinessDataSourceProfiles.CloudReadOnly.QuerySecurity);

        result.IsSafe.Should().BeTrue(result.ErrorMessage);
    }

    [Theory]
    [InlineData("SELECT d.device_code FROM public.devices d LIMIT 10")]
    [InlineData("SELECT d.client_code FROM public.devices d JOIN public.mfg_processes p ON p.device_code = d.client_code LIMIT 10")]
    public void Validate_ShouldRejectDisallowedCloudColumnsAcrossProjectionAndJoin(string sql)
    {
        var result = _guardrail.Validate(
            sql,
            DatabaseProviderType.PostgreSql,
            StandardBusinessDataSourceProfiles.CloudReadOnly.QuerySecurity);

        result.IsSafe.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Column 'device_code'");
    }

    [Fact]
    public void Validate_ShouldAllowPostgresLateralDerivedQuery_WhenInnerColumnsAreGoverned()
    {
        var sql =
            """
            SELECT d.device_name, latest_log.level
            FROM public.devices d
            LEFT JOIN LATERAL (
                SELECT l.level, l.log_time
                FROM public.device_logs l
                WHERE l.device_id = d.id
                ORDER BY l.log_time DESC
                LIMIT 1
            ) latest_log ON true
            ORDER BY d.device_name ASC
            LIMIT 10
            """;

        var result = _guardrail.Validate(
            sql,
            DatabaseProviderType.PostgreSql,
            StandardBusinessDataSourceProfiles.CloudReadOnly.QuerySecurity);

        result.IsSafe.Should().BeTrue(result.ErrorMessage);
    }

    [Theory]
    [InlineData("SELECT employee_no FROM public.employees LIMIT 25", true)]
    [InlineData("SELECT password_hash FROM public.employees", false)]
    [InlineData("SELECT employee_no FROM public.unknown_real_business_table", false)]
    [InlineData("SELECT * FROM pg_catalog.pg_user", false)]
    public void Validate_ShouldOwnSimulationProfileBoundariesAtSharedAstGuard(
        string sql,
        bool expectedSafe)
    {
        var result = _guardrail.Validate(
            sql,
            DatabaseProviderType.PostgreSql,
            BusinessQuerySecurityProfile.TableOnly(
                new HashSet<string>(
                    ["public"],
                    StringComparer.OrdinalIgnoreCase),
                new HashSet<string>(
                    ["employees", "production_records"],
                    StringComparer.OrdinalIgnoreCase),
                new Dictionary<string, IReadOnlySet<string>>(StringComparer.OrdinalIgnoreCase)
                {
                    ["employees"] = new HashSet<string>(
                        ["employee_no"],
                        StringComparer.OrdinalIgnoreCase),
                    ["production_records"] = new HashSet<string>(
                        ["record_id"],
                        StringComparer.OrdinalIgnoreCase)
                },
                new HashSet<string>(
                    ["password", "secret", "token"],
                    StringComparer.OrdinalIgnoreCase)));

        result.IsSafe.Should().Be(expectedSafe, result.ErrorMessage);
    }

    [Fact]
    public void Validate_ShouldRejectUnqualifiedPhysicalTableNames()
    {
        var result = _guardrail.Validate(
            "SELECT client_code FROM devices",
            DatabaseProviderType.PostgreSql,
            StandardBusinessDataSourceProfiles.CloudReadOnly.QuerySecurity);

        result.IsSafe.Should().BeFalse();
        result.ErrorMessage.Should().Contain("schema.table");
    }
}
