using AICopilot.Infrastructure.AiGateway;
using AICopilot.Services.Contracts;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace AICopilot.BackendTests;

[Collection(CoreBackendTestCollection.Name)]
[Trait("Suite", "AcceptanceClosure")]
[Trait("Runtime", "DockerRequired")]
public sealed class AcceptanceClosureVerificationTests
{
    private readonly AICopilotAppFixture _fixture;

    public AcceptanceClosureVerificationTests(CoreAICopilotAppFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task MigrationSchema_ShouldContainOnsiteAttestationColumns_AndUtcSafeTypes()
    {
        var connectionString = await _fixture.GetConnectionStringAsync();

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();

        var sessionColumns = await QueryColumnMetadataAsync(
            connection,
            "aigateway",
            "sessions",
            ["onsite_confirmed_at", "onsite_confirmation_expires_at", "onsite_confirmed_by"]);

        sessionColumns.Should().ContainKey("onsite_confirmed_at");
        sessionColumns["onsite_confirmed_at"].Should().Be("timestamp with time zone");
        sessionColumns.Should().ContainKey("onsite_confirmation_expires_at");
        sessionColumns["onsite_confirmation_expires_at"].Should().Be("timestamp with time zone");
        sessionColumns.Should().ContainKey("onsite_confirmed_by");
        sessionColumns["onsite_confirmed_by"].Should().BeOneOf("text", "character varying");

        var approvalPolicyColumns = await QueryColumnMetadataAsync(
            connection,
            "aigateway",
            "approval_policies",
            ["requires_onsite_attestation"]);

        approvalPolicyColumns.Should().ContainKey("requires_onsite_attestation");
        approvalPolicyColumns["requires_onsite_attestation"].Should().Be("boolean");

        await using var migrationCommand = connection.CreateCommand();
        migrationCommand.CommandText =
            """
            SELECT COUNT(*)
            FROM "__EFMigrationsHistory"
            WHERE "MigrationId" = '20260423064237_Phase44OnsiteAttestationColumns';
            """;

        var migrationCount = Convert.ToInt32(await migrationCommand.ExecuteScalarAsync());
        migrationCount.Should().Be(1);
    }

    [Fact]
    public async Task RedisFinalAgentContextStore_ShouldShareContextAcrossStoreInstances()
    {
        var redisConnectionString = await _fixture.GetConnectionStringAsync("final-agent-context-redis");
        using var serviceProviderA = CreateRedisServiceProvider(redisConnectionString);
        using var serviceProviderB = CreateRedisServiceProvider(redisConnectionString);

        var storeA = new RedisFinalAgentContextStore(serviceProviderA.GetRequiredService<IDistributedCache>());
        var storeB = new RedisFinalAgentContextStore(serviceProviderB.GetRequiredService<IDistributedCache>());

        var sessionId = Guid.NewGuid();
        var storedContext = new StoredFinalAgentContext(
            sessionId,
            "prepare diagnostic checklist for device DEV-001",
            128,
            64,
            new ChatTokenTelemetryContext(sessionId, "fake-model", "fake-template", 4096, 512),
            512,
            0.3f,
            ["GenerateDiagnosticChecklist"],
            """{"threadId":"acceptance-redis-test"}""",
            [
                new StoredToolApprovalRequest(
                    "request-1",
                    "call-1",
                    "Function",
                    "GenerateDiagnosticChecklist",
                    null,
                    new Dictionary<string, object?>
                    {
                        ["deviceCode"] = "DEV-001"
                    })
            ]);

        await storeA.SetAsync(sessionId, storedContext);

        var restoredContext = await storeB.GetAsync(sessionId);
        restoredContext.Should().NotBeNull();
        restoredContext!.SessionId.Should().Be(sessionId);
        restoredContext.InputText.Should().Be(storedContext.InputText);
        restoredContext.ToolNames.Should().Equal(storedContext.ToolNames);
        restoredContext.PendingApprovals.Should().HaveCount(1);
        restoredContext.PendingApprovals[0].CallId.Should().Be("call-1");
        restoredContext.PendingApprovals[0].ToolName.Should().Be("GenerateDiagnosticChecklist");
        restoredContext.PendingApprovals[0].Arguments.Should().ContainKey("deviceCode");
        restoredContext.PendingApprovals[0].Arguments["deviceCode"]?.ToString().Should().Be("DEV-001");

        await storeB.RemoveAsync(sessionId);

        var removedContext = await storeA.GetAsync(sessionId);
        removedContext.Should().BeNull();
    }

    private static ServiceProvider CreateRedisServiceProvider(string redisConnectionString)
    {
        var services = new ServiceCollection();
        services.AddStackExchangeRedisCache(options =>
        {
            options.Configuration = redisConnectionString;
            options.InstanceName = "acceptance:";
        });

        return services.BuildServiceProvider();
    }

    private static async Task<Dictionary<string, string>> QueryColumnMetadataAsync(
        NpgsqlConnection connection,
        string schemaName,
        string tableName,
        IReadOnlyCollection<string> columnNames)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT column_name, data_type
            FROM information_schema.columns
            WHERE table_schema = @schemaName
              AND table_name = @tableName
              AND column_name = ANY(@columnNames)
            ORDER BY column_name;
            """;
        command.Parameters.AddWithValue("schemaName", schemaName);
        command.Parameters.AddWithValue("tableName", tableName);
        command.Parameters.AddWithValue("columnNames", columnNames.ToArray());

        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            result[reader.GetString(0)] = reader.GetString(1);
        }

        return result;
    }
}
