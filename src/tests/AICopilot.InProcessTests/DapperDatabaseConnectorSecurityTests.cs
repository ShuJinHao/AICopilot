using AICopilot.Dapper;
using AICopilot.Dapper.Security;
using Microsoft.Extensions.Logging;

namespace AICopilot.InProcessTests;

public sealed class DapperDatabaseConnectorSecurityTests
{
    [Fact]
    public async Task Connector_ShouldRedactRejectedSqlAndLogOnlyDerivedMetadata()
    {
        var logger = new CapturingLogger<DapperDatabaseConnector>();
        var connector = new DapperDatabaseConnector(new AstSqlGuardrail(), logger);
        const string sensitiveSql = "DELETE FROM orders WHERE customer_name = 'secret customer'";
        var database = new BusinessDatabaseConnectionInfo(
            Guid.NewGuid(),
            "readonly-db",
            "test",
            "Host=localhost;Database=readonly;Username=test;Password=test;",
            DatabaseProviderType.PostgreSql,
            IsEnabled: true,
            IsReadOnly: true);

        var action = async () => await connector.ExecuteQueryWithMetadataAsync(database, sensitiveSql);

        await action.Should().ThrowAsync<InvalidOperationException>();
        var log = logger.Entries.Should().ContainSingle(entry => entry.Level == LogLevel.Warning).Subject;
        log.Message.Should().Contain($"SqlLength={sensitiveSql.Length}");
        log.Message.Should().MatchRegex(".*SqlSha256=[0-9A-Fa-f]{64}.*");
        log.Message.Should().Contain("ReasonCode=non_select");
        log.Message.Should().Contain("OriginalMessage=hidden_by_security_policy");
        AssertSensitiveDataIsRedacted(log, sensitiveSql);
    }

    [Fact]
    public async Task Connector_ShouldRejectWritableOrDisabledSourcesBeforeOpeningConnection()
    {
        var logger = new CapturingLogger<DapperDatabaseConnector>();
        var connector = new DapperDatabaseConnector(new AstSqlGuardrail(), logger);
        var writable = CreateDatabase("writable-source", isReadOnly: false);
        var disabled = CreateDatabase("disabled-source", isEnabled: false);

        var writableAction = async () => await connector.ExecuteQueryWithMetadataAsync(writable, "SELECT 1");
        var disabledAction = async () => await connector.ExecuteQueryWithMetadataAsync(disabled, "SELECT 1");

        await writableAction.Should().ThrowAsync<InvalidOperationException>().WithMessage("*只读模式*");
        await disabledAction.Should().ThrowAsync<InvalidOperationException>().WithMessage("*已被禁用*");
        logger.Entries.Should().BeEmpty("source eligibility must fail before SQL validation or connection I/O");
    }

    [Fact]
    public async Task SqlServerSource_ShouldRequireVerifiedReadOnlyCredential()
    {
        var connector = new DapperDatabaseConnector(
            new AstSqlGuardrail(),
            new CapturingLogger<DapperDatabaseConnector>());
        var database = CreateDatabase(
            "sql-server-source",
            provider: DatabaseProviderType.SqlServer,
            externalSystemType: DataSourceExternalSystemType.NonCloud);

        var action = async () => await connector.ExecuteQueryWithMetadataAsync(database, "SELECT 1");

        await action.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*verified read-only database account is required*");
    }

    [Fact]
    public async Task CloudReadOnlySource_ShouldRequireVerifiedReadOnlyCredential()
    {
        var connector = new DapperDatabaseConnector(
            new AstSqlGuardrail(),
            new CapturingLogger<DapperDatabaseConnector>());
        var database = CreateDatabase(
            "cloud-reporting-source",
            externalSystemType: DataSourceExternalSystemType.CloudReadOnly);

        var action = async () => await connector.ExecuteQueryWithMetadataAsync(database, "SELECT 1");

        await action.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*database account has not been verified as read-only*");
    }

    [Fact]
    public void DapperConnector_ShouldRejectEmptyConnectionString()
    {
        var connector = new DapperDatabaseConnector(
            new AstSqlGuardrail(),
            new CapturingLogger<DapperDatabaseConnector>());
        var database = CreateDatabase("empty-connection", connectionString: " ");

        var action = () => connector.GetConnection(database);

        action.Should().Throw<ArgumentException>()
            .WithMessage("*Connection string is required*");
    }

    private static BusinessDatabaseConnectionInfo CreateDatabase(
        string name,
        string connectionString = "Host=localhost;Database=test;Username=reader;Password=fake-test-only",
        DatabaseProviderType provider = DatabaseProviderType.PostgreSql,
        bool isEnabled = true,
        bool isReadOnly = true,
        DataSourceExternalSystemType externalSystemType = DataSourceExternalSystemType.Unknown)
    {
        return new BusinessDatabaseConnectionInfo(
            Guid.NewGuid(),
            name,
            "real connector boundary test",
            connectionString,
            provider,
            isEnabled,
            isReadOnly,
            externalSystemType,
            ReadOnlyCredentialVerified: false);
    }

    private static void AssertSensitiveDataIsRedacted(
        CapturedLogEntry log,
        string sensitiveSql,
        params string[] additionalForbiddenFragments)
    {
        log.Exception.Should().BeNull();
        foreach (var fragment in new[] { sensitiveSql, "secret customer", "Password=test" }
                     .Concat(additionalForbiddenFragments))
        {
            log.Message.Should().NotContain(fragment);
        }
    }
}
