using AICopilot.Dapper;
using AICopilot.Dapper.Security;
using AICopilot.Services.Contracts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace AICopilot.PersistenceTests;

[Collection(IdentityPersistenceTestCollection.Name)]
public sealed class DapperDatabaseConnectorPersistenceTests(PostgresPersistenceFixture fixture)
{
    [Fact]
    public async Task Query_ShouldUseReadOnlyTransactionAndBoundRows()
    {
        await using var database = await PostgresScratchDatabase.CreateAsync(
            fixture.ConnectionString,
            "aicopilot_dapper");
        await using (var connection = new NpgsqlConnection(database.ConnectionString))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "CREATE TABLE samples(id integer PRIMARY KEY); INSERT INTO samples VALUES (1), (2), (3), (4);";
            await command.ExecuteNonQueryAsync();
        }

        var connector = new DapperDatabaseConnector(
            new AstSqlGuardrail(),
            NullLogger<DapperDatabaseConnector>.Instance);
        var source = new BusinessDatabaseConnectionInfo(
            Guid.NewGuid(),
            "readonly-postgres",
            "real PostgreSQL read-only transaction test",
            database.ConnectionString,
            DatabaseProviderType.PostgreSql,
            IsEnabled: true,
            IsReadOnly: true);

        var transactionMode = await connector.ExecuteQueryWithMetadataAsync(
            source,
            "SELECT current_setting('transaction_read_only') AS read_only",
            options: new DatabaseQueryOptions(MaxRows: 1));
        var bounded = await connector.ExecuteQueryWithMetadataAsync(
            source,
            "SELECT id FROM samples ORDER BY id",
            options: new DatabaseQueryOptions(MaxRows: 2));

        transactionMode.Rows.Should().ContainSingle();
        transactionMode.Rows[0]["read_only"].Should().Be("on");
        bounded.Rows.Select(row => Convert.ToInt32(row["id"])).Should().Equal(1, 2);
        bounded.IsTruncated.Should().BeTrue();
        bounded.ReturnedRowCount.Should().Be(3);
    }

    [Fact]
    public async Task QueryExecutionFailure_ShouldUseRealPostgresAndRedactSqlAndConnectionDetails()
    {
        await using var database = await PostgresScratchDatabase.CreateAsync(
            fixture.ConnectionString,
            "aicopilot_dapper_error");
        var logger = new CapturingLogger<DapperDatabaseConnector>();
        var connector = new DapperDatabaseConnector(new AstSqlGuardrail(), logger);
        const string sensitiveSql =
            "SELECT * FROM missing_orders WHERE customer_name = 'secret customer'";
        var source = new BusinessDatabaseConnectionInfo(
            Guid.NewGuid(),
            "readonly-postgres-error",
            "real PostgreSQL execution-failure redaction test",
            database.ConnectionString,
            DatabaseProviderType.PostgreSql,
            IsEnabled: true,
            IsReadOnly: true);

        var action = async () => await connector.ExecuteQueryWithMetadataAsync(source, sensitiveSql);

        await action.Should().ThrowAsync<PostgresException>();
        var log = logger.Entries.Should().ContainSingle(entry => entry.Level == LogLevel.Error).Subject;
        foreach (var expectedFragment in new[]
                 {
                     $"SqlLength={sensitiveSql.Length}",
                     "SqlSha256=",
                     "Provider=PostgreSql",
                     "ErrorType=PostgresException",
                     "OriginalMessage=hidden_by_security_policy"
                 })
        {
            log.Message.Should().Contain(expectedFragment);
        }
        log.Exception.Should().BeNull();
        foreach (var forbidden in new[]
                 {
                     sensitiveSql,
                     "secret customer",
                     "missing_orders",
                     database.ConnectionString,
                     "Password="
                 })
        {
            log.Message.Should().NotContain(forbidden);
        }
    }
}
