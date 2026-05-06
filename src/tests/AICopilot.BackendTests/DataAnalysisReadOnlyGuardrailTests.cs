using AICopilot.Dapper;
using AICopilot.Dapper.Security;
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

    private static DapperDatabaseConnector CreateConnector()
    {
        return new DapperDatabaseConnector(
            new AstSqlGuardrail(),
            NullLogger<DapperDatabaseConnector>.Instance);
    }
}
