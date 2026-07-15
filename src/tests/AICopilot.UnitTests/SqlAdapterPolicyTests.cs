using AICopilot.Dapper.Security;
using AICopilot.Services.Contracts;

namespace AICopilot.UnitTests;

public sealed class SqlAdapterPolicyTests
{
    [Fact]
    public void BusinessDatabaseConnectionPolicy_ShouldRejectWritableOrDisabledSources()
    {
        var writable = CreateConnectionInfo(isEnabled: true, isReadOnly: false);
        var disabled = CreateConnectionInfo(isEnabled: false, isReadOnly: true);

        var writableAction = () => BusinessDatabaseConnectionPolicy.EnsureQueryable(writable);
        var disabledAction = () => BusinessDatabaseConnectionPolicy.EnsureQueryable(disabled);

        writableAction.Should().Throw<InvalidOperationException>().WithMessage("*只读模式*");
        disabledAction.Should().Throw<InvalidOperationException>().WithMessage("*已被禁用*");
    }

    [Fact]
    public void SqlExecutionLogPolicy_ShouldHashSqlWithoutRetainingPlaintext()
    {
        const string sensitiveSql = "SELECT * FROM orders WHERE customer_name = 'secret customer'";

        var metadata = SqlExecutionLogPolicy.CreateMetadata(sensitiveSql);

        metadata.Length.Should().Be(sensitiveSql.Length);
        metadata.Sha256.Should().MatchRegex("^[0-9a-f]{64}$");
        metadata.Sha256.Should().NotContain("secret customer");
        SqlExecutionLogPolicy.ClassifyGuardrailFailure("Only SELECT queries are allowed.")
            .Should().Be("non_select");
    }

    private static BusinessDatabaseConnectionInfo CreateConnectionInfo(bool isEnabled, bool isReadOnly)
    {
        return new BusinessDatabaseConnectionInfo(
            Guid.NewGuid(),
            "policy-source",
            "policy test",
            "Host=localhost;Database=test;Username=reader;Password=fake-test-only",
            DatabaseProviderType.PostgreSql,
            isEnabled,
            isReadOnly);
    }
}
