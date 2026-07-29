using AICopilot.EntityFrameworkCore;
using AICopilot.EntityFrameworkCore.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace AICopilot.PersistenceTests;

[Collection(PostgresPersistenceTestCollection.Name)]
public sealed class AiGatewayMigrationSchemaTests(PostgresPersistenceFixture fixture)
{
    [Fact]
    public async Task FreshMigration_ShouldCreateOnsiteAttestationAndFinalOutputClosureSchema()
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
                "source_approval_request_id",
                "lease_expires_at",
                "available_at"
            ]);
        queueColumns["task_id"].Should().Be("uuid");
        queueColumns["trigger_type"].Should().Be("character varying");
        queueColumns["status"].Should().Be("character varying");
        queueColumns["requested_by"].Should().Be("uuid");
        queueColumns["run_attempt_id"].Should().Be("uuid");
        queueColumns["source_approval_request_id"].Should().Be("uuid");
        queueColumns["lease_expires_at"].Should().Be("timestamp with time zone");
        queueColumns["available_at"].Should().Be("timestamp with time zone");

        var approvalColumns = await QueryColumnMetadataAsync(
            connection,
            "aigateway",
            "approval_requests",
            [
                "final_output_proof_version",
                "final_output_workspace_id",
                "final_output_final_step_id",
                "final_output_run_attempt_id",
                "final_output_node_run_id",
                "final_output_task_fencing_token",
                "final_output_node_fencing_token",
                "final_output_evidence_set_digest",
                "final_output_manifest_digest",
                "final_output_artifact_bindings_json",
                "final_output_artifact_binding_digest",
                "final_output_proof_digest",
                "final_output_decision_proof_digest"
            ]);
        approvalColumns.Should().HaveCount(13);
        approvalColumns["final_output_artifact_bindings_json"].Should().Be("jsonb");

        var approvalIndexes = await QueryIndexDefinitionsAsync(
            connection,
            "aigateway",
            "approval_requests");
        approvalIndexes.Should().Contain(definition =>
            definition.Contains(
                "ux_approval_requests_final_output_task",
                StringComparison.OrdinalIgnoreCase) &&
            definition.Contains("UNIQUE", StringComparison.OrdinalIgnoreCase) &&
            definition.Contains("task_id", StringComparison.OrdinalIgnoreCase) &&
            definition.Contains("approval_type", StringComparison.OrdinalIgnoreCase) &&
            !definition.Contains("target_id", StringComparison.OrdinalIgnoreCase));
        var queueIndexes = await QueryIndexDefinitionsAsync(
            connection,
            "aigateway",
            "agent_task_run_queue_items");
        queueIndexes.Should().Contain(definition =>
            definition.Contains(
                "ux_agent_task_run_queue_items_source_approval",
                StringComparison.OrdinalIgnoreCase) &&
            definition.Contains("UNIQUE", StringComparison.OrdinalIgnoreCase));
        var messageEventIndexes = await QueryIndexDefinitionsAsync(
            connection,
            "aigateway",
            "message_events");
        messageEventIndexes.Should().Contain(definition =>
            definition.Contains(
                "ux_message_events_approval_lifecycle_event",
                StringComparison.OrdinalIgnoreCase) &&
            definition.Contains("UNIQUE", StringComparison.OrdinalIgnoreCase) &&
            definition.Contains("approval_request_id", StringComparison.OrdinalIgnoreCase) &&
            definition.Contains("event_type", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("active")]
    [InlineData("duplicate")]
    [InlineData("non-terminal")]
    public async Task B03Migration_ShouldBlockProoflessNonTerminalActiveOrDuplicateFinalOutputData(
        string dirtyState)
    {
        await using var database = await PostgresScratchDatabase.CreateAsync(
            fixture.ConnectionString,
            "aicopilot_gateway_b03_guard");
        await MigrateAiGatewayAsync(
            database.ConnectionString,
            "20260722170000_AddArtifactEvidenceSetDigest");
        await SeedLegacyFinalOutputAsync(database.ConnectionString, dirtyState);

        Func<Task> migrate = () => MigrateAiGatewayAsync(database.ConnectionString);

        var failure = await migrate.Should().ThrowAsync<PostgresException>();
        failure.Which.SqlState.Should().Be(PostgresErrorCodes.CheckViolation);
        failure.Which.MessageText.Should().Contain(
            dirtyState == "duplicate"
                ? "duplicate final-output approvals"
                : "non-terminal approval, active task, or orphaned proofless final-output data");
    }

    [Fact]
    public async Task B03Migration_ShouldKeepTerminalLegacyApprovalReadOnlyWithoutInventingProof()
    {
        await using var database = await PostgresScratchDatabase.CreateAsync(
            fixture.ConnectionString,
            "aicopilot_gateway_b03_legacy");
        await MigrateAiGatewayAsync(
            database.ConnectionString,
            "20260722170000_AddArtifactEvidenceSetDigest");
        await SeedLegacyFinalOutputAsync(database.ConnectionString, "terminal");

        await MigrateAiGatewayAsync(database.ConnectionString);

        await using var dbContext = new AiGatewayDbContext(
            new DbContextOptionsBuilder<AiGatewayDbContext>()
                .UseNpgsqlWithMigrationHistory(
                    database.ConnectionString,
                    MigrationHistoryTables.AiGateway)
                .Options);
        var approval = await dbContext.ApprovalRequests.AsNoTracking().SingleAsync();
        approval.FinalOutputProofVersion.Should().Be("legacy-read-only-v0");
        approval.FinalOutputProofDigest.Should().BeNull();
        approval.HasValidFinalOutputProof().Should().BeFalse();
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

    private static async Task MigrateAiGatewayAsync(
        string connectionString,
        string? targetMigration = null)
    {
        var options = new DbContextOptionsBuilder<AiGatewayDbContext>()
            .UseNpgsqlWithMigrationHistory(
                connectionString,
                MigrationHistoryTables.AiGateway)
            .Options;
        await using var dbContext = new AiGatewayDbContext(options);
        await dbContext.GetService<IMigrator>().MigrateAsync(targetMigration);
    }

    private static async Task SeedLegacyFinalOutputAsync(
        string connectionString,
        string dirtyState)
    {
        var taskId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using (var task = connection.CreateCommand())
        {
            task.CommandText =
                """
                INSERT INTO aigateway.agent_tasks (
                    id, task_code, session_id, user_id, title, goal, task_type, status,
                    risk_level, run_attempt_count, run_fencing_token, plan_json,
                    created_at, updated_at)
                VALUES (
                    @id, @taskCode, @sessionId, @userId, 'B03 legacy guard',
                    'B03 legacy guard', 'ReportGeneration', @status, 'Low', 0, 0,
                    '{"version":1}', @now, @now);
                """;
            task.Parameters.AddWithValue("id", taskId);
            task.Parameters.AddWithValue("taskCode", $"TASK-B03-{Guid.NewGuid():N}");
            task.Parameters.AddWithValue("sessionId", Guid.NewGuid());
            task.Parameters.AddWithValue("userId", userId);
            task.Parameters.AddWithValue(
                "status",
                dirtyState == "active" ? "WaitingFinalApproval" : "Completed");
            task.Parameters.AddWithValue("now", DateTimeOffset.UtcNow);
            await task.ExecuteNonQueryAsync();
        }

        var approvalCount = dirtyState == "duplicate" ? 2 : 1;
        for (var index = 0; index < approvalCount; index++)
        {
            await using var approval = connection.CreateCommand();
            var approvalStatus = dirtyState == "terminal" ? "Approved" : "Pending";
            approval.CommandText =
                """
                INSERT INTO aigateway.approval_requests (
                    id, task_id, approval_type, target_id, status, requested_by,
                    approved_by, approved_at, created_at)
                VALUES (
                    @id, @taskId, 'FinalOutput', @targetId,
                    @status, @requestedBy,
                    CASE WHEN @status = 'Approved' THEN @requestedBy ELSE NULL END,
                    CASE WHEN @status = 'Approved' THEN @createdAt ELSE NULL END,
                    @createdAt);
                """;
            approval.Parameters.AddWithValue("id", Guid.NewGuid());
            approval.Parameters.AddWithValue("taskId", taskId);
            approval.Parameters.AddWithValue(
                "targetId",
                dirtyState == "duplicate"
                    ? $"ws_legacy_guard_{index}"
                    : "ws_legacy_guard");
            approval.Parameters.AddWithValue("status", approvalStatus);
            approval.Parameters.AddWithValue("requestedBy", userId);
            approval.Parameters.AddWithValue("createdAt", DateTimeOffset.UtcNow);
            await approval.ExecuteNonQueryAsync();
        }
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
