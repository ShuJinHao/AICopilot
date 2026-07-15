using AICopilot.EntityFrameworkCore;
using AICopilot.EntityFrameworkCore.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace AICopilot.PersistenceTests;

[Collection(PostgresPersistenceTestCollection.Name)]
public sealed class AiGatewayMigrationSchemaTests(PostgresPersistenceFixture fixture)
{
    [Fact]
    public async Task FreshMigration_ShouldCreateOnsiteAttestationAndRunQueueSchema()
    {
        await using var database = await PostgresScratchDatabase.CreateAsync(
            fixture.ConnectionString,
            "aicopilot_gateway_schema");
        await MigrateAiGatewayAsync(database.ConnectionString);

        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();

        var sessionColumns = await QueryColumnMetadataAsync(
            connection,
            "aigateway",
            "sessions",
            ["onsite_confirmed_at", "onsite_confirmation_expires_at", "onsite_confirmed_by"]);
        sessionColumns["onsite_confirmed_at"].Should().Be("timestamp with time zone");
        sessionColumns["onsite_confirmation_expires_at"].Should().Be(
            "timestamp with time zone");
        sessionColumns["onsite_confirmed_by"].Should().BeOneOf(
            "text",
            "character varying");

        var approvalPolicyColumns = await QueryColumnMetadataAsync(
            connection,
            "aigateway",
            "approval_policies",
            ["requires_onsite_attestation"]);
        approvalPolicyColumns["requires_onsite_attestation"].Should().Be("boolean");

        var queueColumns = await QueryColumnMetadataAsync(
            connection,
            "aigateway",
            "agent_task_run_queue_items",
            [
                "task_id",
                "trigger_type",
                "status",
                "requested_by",
                "run_attempt_id",
                "lease_expires_at",
                "available_at"
            ]);
        queueColumns["task_id"].Should().Be("uuid");
        queueColumns["trigger_type"].Should().Be("character varying");
        queueColumns["status"].Should().Be("character varying");
        queueColumns["requested_by"].Should().Be("uuid");
        queueColumns["run_attempt_id"].Should().Be("uuid");
        queueColumns["lease_expires_at"].Should().Be("timestamp with time zone");
        queueColumns["available_at"].Should().Be("timestamp with time zone");
    }

    [Fact]
    public async Task FreshMigration_ShouldCreateDynamicRoutingSchemaAndSingleActiveIndex()
    {
        await using var database = await PostgresScratchDatabase.CreateAsync(
            fixture.ConnectionString,
            "aicopilot_gateway_schema");
        await MigrateAiGatewayAsync(database.ConnectionString);

        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();

        var languageModelColumns = await QueryColumnMetadataAsync(
            connection,
            "aigateway",
            "language_models",
            [
                "protocol_type",
                "usage",
                "is_enabled",
                "max_output_tokens",
                "connectivity_status",
                "connectivity_checked_at",
                "connectivity_error"
            ]);
        languageModelColumns["protocol_type"].Should().Be("character varying");
        languageModelColumns["usage"].Should().Be("integer");
        languageModelColumns["is_enabled"].Should().Be("boolean");
        languageModelColumns["max_output_tokens"].Should().Be("integer");
        languageModelColumns["connectivity_status"].Should().Be("integer");
        languageModelColumns["connectivity_checked_at"].Should().Be(
            "timestamp with time zone");
        languageModelColumns["connectivity_error"].Should().Be("character varying");

        var messageColumns = await QueryColumnMetadataAsync(
            connection,
            "aigateway",
            "messages",
            [
                "final_model_id",
                "final_model_name",
                "routing_model_id",
                "routing_model_name",
                "context_window_tokens",
                "max_output_tokens"
            ]);
        messageColumns["final_model_id"].Should().Be("uuid");
        messageColumns["final_model_name"].Should().Be("character varying");
        messageColumns["routing_model_id"].Should().Be("uuid");
        messageColumns["routing_model_name"].Should().Be("character varying");
        messageColumns["context_window_tokens"].Should().Be("integer");
        messageColumns["max_output_tokens"].Should().Be("integer");

        var routingModelColumns = await QueryColumnMetadataAsync(
            connection,
            "aigateway",
            "routing_model_configurations",
            ["id", "name", "model_id", "is_active"]);
        routingModelColumns["id"].Should().Be("uuid");
        routingModelColumns["name"].Should().Be("character varying");
        routingModelColumns["model_id"].Should().Be("uuid");
        routingModelColumns["is_active"].Should().Be("boolean");

        var indexDefinitions = await QueryIndexDefinitionsAsync(
            connection,
            "aigateway",
            "routing_model_configurations");
        indexDefinitions.Should().Contain(definition =>
            definition.Contains("UNIQUE", StringComparison.OrdinalIgnoreCase) &&
            definition.Contains("is_active", StringComparison.OrdinalIgnoreCase) &&
            definition.Contains("WHERE is_active", StringComparison.OrdinalIgnoreCase));
    }

    private static async Task MigrateAiGatewayAsync(string connectionString)
    {
        var options = new DbContextOptionsBuilder<AiGatewayDbContext>()
            .UseNpgsqlWithMigrationHistory(
                connectionString,
                MigrationHistoryTables.AiGateway)
            .Options;
        await using var dbContext = new AiGatewayDbContext(options);
        await dbContext.Database.MigrateAsync();
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

    private static async Task<List<string>> QueryIndexDefinitionsAsync(
        NpgsqlConnection connection,
        string schemaName,
        string tableName)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT indexdef
            FROM pg_indexes
            WHERE schemaname = @schemaName
              AND tablename = @tableName
            ORDER BY indexname;
            """;
        command.Parameters.AddWithValue("schemaName", schemaName);
        command.Parameters.AddWithValue("tableName", tableName);

        var result = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            result.Add(reader.GetString(0));
        }

        return result;
    }
}
