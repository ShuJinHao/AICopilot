using AICopilot.EntityFrameworkCore;
using AICopilot.EntityFrameworkCore.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace AICopilot.PersistenceTests;

[Collection(PostgresPersistenceTestCollection.Name)]
public sealed class AiGatewayMigrationSchemaTests(PostgresPersistenceFixture fixture)
{
    private static readonly string[] CurrentTables =
    [
        "agent_session_states",
        "conversation_templates",
        "language_models",
        "messages",
        "model_quota_reservations",
        "sessions",
        "tool_registrations"
    ];

    private static readonly string[] RetiredTables =
    [
        "agent_tasks",
        "agent_task_runs",
        "agent_task_run_attempts",
        "agent_task_run_queue_items",
        "agent_node_runs",
        "agent_worker_heartbeats",
        "approval_policies",
        "approval_requests",
        "artifact_workspaces",
        "artifacts",
        "chat_runtime_settings",
        "message_events",
        "routing_model_configurations",
        "tool_execution_records",
        "upload_records"
    ];

    [Fact]
    public void Model_ShouldHaveSingleHarnessBaselineAndNoPendingChanges()
    {
        using var dbContext = CreateDbContext(fixture.ConnectionString);

        dbContext.Database.GetMigrations()
            .Should().ContainSingle()
            .Which.Should().EndWith("_AiGatewayHarnessBaseline");
        dbContext.Database.HasPendingModelChanges().Should().BeFalse();
    }

    [Fact]
    public async Task FreshHarnessBaseline_ShouldCreateOnlyCurrentAiGatewaySchema()
    {
        await using var database = await PostgresScratchDatabase.CreateAsync(
            fixture.ConnectionString,
            "aicopilot_gateway_harness_schema");
        await using (var dbContext = CreateDbContext(database.ConnectionString))
        {
            await dbContext.Database.MigrateAsync();
        }

        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();

        var actualTables = await QueryTableNamesAsync(connection, "aigateway");
        actualTables.Should().Contain(CurrentTables);
        actualTables.Should().NotContain(RetiredTables);

        var messageColumns = await QueryColumnMetadataAsync(
            connection,
            "aigateway",
            "messages",
            [
                "final_model_id",
                "final_model_name",
                "context_window_tokens",
                "max_output_tokens",
                "routing_model_id",
                "routing_model_name"
            ]);
        messageColumns.Should().ContainKeys(
            "final_model_id",
            "final_model_name",
            "context_window_tokens",
            "max_output_tokens");
        messageColumns.Should().NotContainKeys("routing_model_id", "routing_model_name");

        var agentSessionColumns = await QueryColumnMetadataAsync(
            connection,
            "aigateway",
            "agent_session_states",
            [
                "session_id",
                "protected_state",
                "protected_approval_bindings",
                "status",
                "version",
                "expires_at_utc"
            ]);
        agentSessionColumns.Should().Contain(new Dictionary<string, string>
        {
            ["session_id"] = "uuid",
            ["protected_state"] = "text",
            ["protected_approval_bindings"] = "text",
            ["status"] = "character varying",
            ["version"] = "bigint",
            ["expires_at_utc"] = "timestamp with time zone"
        });

        var toolColumns = await QueryColumnMetadataAsync(
            connection,
            "aigateway",
            "tool_registrations",
            [
                "requires_approval",
                "risk_level",
                "required_permission",
                "audit_level",
                "data_boundary",
                "is_executable_by_agent",
                "schema_version",
                "catalog_version",
                "is_visible_to_planner",
                "approval_policy"
            ]);
        toolColumns.Should().ContainKeys(
            "requires_approval",
            "risk_level",
            "required_permission",
            "audit_level",
            "data_boundary",
            "is_executable_by_agent",
            "schema_version",
            "catalog_version");
        toolColumns.Should().NotContainKeys("is_visible_to_planner", "approval_policy");
    }

    private static AiGatewayDbContext CreateDbContext(string connectionString)
    {
        var options = new DbContextOptionsBuilder<AiGatewayDbContext>()
            .UseNpgsqlWithMigrationHistory(
                connectionString,
                MigrationHistoryTables.AiGateway)
            .Options;
        return new AiGatewayDbContext(options);
    }

    private static async Task<string[]> QueryTableNamesAsync(
        NpgsqlConnection connection,
        string schemaName)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT table_name
            FROM information_schema.tables
            WHERE table_schema = @schemaName
              AND table_type = 'BASE TABLE'
            ORDER BY table_name;
            """;
        command.Parameters.AddWithValue("schemaName", schemaName);

        var result = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            result.Add(reader.GetString(0));
        }

        return result.ToArray();
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
