using AICopilot.Dapper;
using AICopilot.Dapper.Security;
using AICopilot.Core.DataAnalysis.Aggregates.BusinessDatabase;
using AICopilot.DataAnalysisService.BusinessDatabases;
using Microsoft.Extensions.Logging.Abstractions;

namespace AICopilot.BackendTests;

public sealed class DataAnalysisReadOnlyGuardrailTests
{
    [Fact]
    public async Task SqlServerSource_ShouldRequireVerifiedReadOnlyCredential()
    {
        var connector = CreateConnector();
        var database = new BusinessDatabaseConnectionInfo(
            Guid.NewGuid(),
            "sql-server-source",
            "diagnostics",
            "Server=localhost;Database=test;User Id=reader;Password=secret;TrustServerCertificate=True;",
            DatabaseProviderType.SqlServer,
            IsEnabled: true,
            IsReadOnly: true,
            DataSourceExternalSystemType.NonCloud,
            ReadOnlyCredentialVerified: false);

        var act = () => connector.ExecuteQueryWithMetadataAsync(database, "SELECT 1");

        await act.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("*verified read-only database account is required*");
    }

    [Fact]
    public async Task CloudReadOnlySource_ShouldRequireVerifiedReadOnlyCredential()
    {
        var connector = CreateConnector();
        var database = new BusinessDatabaseConnectionInfo(
            Guid.NewGuid(),
            "cloud-reporting-source",
            "Cloud read-only diagnostics",
            "Host=localhost;Database=test;Username=reader;Password=secret",
            DatabaseProviderType.PostgreSql,
            IsEnabled: true,
            IsReadOnly: true,
            DataSourceExternalSystemType.CloudReadOnly,
            ReadOnlyCredentialVerified: false);

        var act = () => connector.ExecuteQueryWithMetadataAsync(database, "SELECT 1");

        await act.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("*database account has not been verified as read-only*");
    }

    [Fact]
    public void CloudReadOnlyGovernance_ShouldExposeSchemaOnlyForVerifiedReadOnlySource()
    {
        var database = new BusinessDatabase(
            "CloudPlatformReadonly",
            "Cloud Platform readonly business data",
            "Host=localhost;Database=cloud;Username=readonly;Password=secret",
            DbProviderType.PostgreSql,
            isReadOnly: true,
            BusinessDataExternalSystemType.CloudReadOnly,
            readOnlyCredentialVerified: true);

        BusinessDataSourceGovernancePolicy.ResolveGovernanceStatus(database)
            .Should().Be("GovernedSchemaReady");
        BusinessDataSourceGovernancePolicy.HasExecutableGovernedSchema(database)
            .Should().BeTrue();
        BusinessDataSourceGovernancePolicy.IsSelectableForMode(database, DataSourceSelectionMode.TextToSql)
            .Should().BeFalse("the legacy P1 rule-based Text-to-SQL generator still emits SimulationBusiness SQL");

        var schema = BusinessDataSourceGovernancePolicy.ResolveSafetySchema(database);
        schema.Should().NotBeNull();
        schema!.AllowedTables.Should().Contain(["devices", "mfg_processes", "device_logs", "hourly_capacity", "pass_station_records"]);

        BusinessReadonlyQuerySafetyPolicy.Validate(
                "SELECT client_code FROM devices LIMIT 10",
                schema)
            .Should().BeNull();
        BusinessReadonlyQuerySafetyPolicy.Validate(
                "SELECT mp.process_name FROM devices d LEFT JOIN mfg_processes mp ON d.process_id = mp.id LIMIT 10",
                schema)
            .Should().BeNull();
        BusinessReadonlyQuerySafetyPolicy.Validate(
                "SELECT * FROM devices",
                schema)
            .Should().Contain("Wildcard");
        BusinessReadonlyQuerySafetyPolicy.Validate(
                "SELECT recipe_name FROM recipes LIMIT 10",
                schema)
            .Should().Contain("not allowed");
        BusinessReadonlyQuerySafetyPolicy.Validate(
                "SELECT bootstrap_secret_hash FROM devices LIMIT 10",
                schema)
            .Should().Contain("Sensitive");
        BusinessReadonlyQuerySafetyPolicy.Validate(
                "UPDATE devices SET device_name = 'bad'",
                schema)
            .Should().Contain("Only SELECT");
    }

    private static DapperDatabaseConnector CreateConnector()
    {
        return new DapperDatabaseConnector(
            new AstSqlGuardrail(),
            NullLogger<DapperDatabaseConnector>.Instance);
    }
}
