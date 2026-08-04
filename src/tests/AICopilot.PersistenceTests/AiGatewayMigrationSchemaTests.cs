using AICopilot.EntityFrameworkCore;
using AICopilot.EntityFrameworkCore.Persistence;
using AICopilot.Core.AiGateway.Aggregates.ConversationTemplate;
using AICopilot.Core.AiGateway.Aggregates.LanguageModel;
using AICopilot.Core.AiGateway.Aggregates.Sessions;
using AICopilot.Core.AiGateway.Aggregates.Tools;
using AICopilot.Core.AiGateway.Runtime.ModelQuota;
using AICopilot.SharedKernel.Ai;
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
        [.. AiGatewayProductionUpgradeContract.RetiredTableAllowlist];

    [Fact]
    public void Model_ShouldPreserveProductionHistoryAndAppendOneUpgrade()
    {
        using var dbContext = CreateDbContext(fixture.ConnectionString);

        dbContext.Database.GetMigrations()
            .Should().Equal(
                AiGatewayProductionUpgradeContract.ProductionMigrationIds
                    .Append(AiGatewayProductionUpgradeContract.CurrentUpgradeMigrationId));
        dbContext.Database.HasPendingModelChanges().Should().BeFalse();
    }

    [Fact]
    public async Task EmptyDatabase_ShouldRunFullAppendOnlyChainToCurrentSchema()
    {
        await using var database = await PostgresScratchDatabase.CreateAsync(
            fixture.ConnectionString,
            "aicopilot_gateway_harness_schema");

        var initial = await AiGatewayProductionUpgradePreflight.InspectAsync(
            database.ConnectionString);
        initial.State.Should().Be(AiGatewayProductionUpgradeState.Fresh);

        await using (var dbContext = CreateDbContext(database.ConnectionString))
        {
            await dbContext.Database.MigrateAsync();
        }

        var migrated = await AiGatewayProductionUpgradePreflight.InspectAsync(
            database.ConnectionString);
        migrated.State.Should().Be(AiGatewayProductionUpgradeState.Current);

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

    [Fact]
    public async Task FrozenProductionDatabase_ShouldUpgradeAndPreserveAuthoritativeRecords()
    {
        await using var database = await PostgresScratchDatabase.CreateAsync(
            fixture.ConnectionString,
            "aicopilot_gateway_production_upgrade");
        await using (var baselineContext = CreateDbContext(database.ConnectionString))
        {
            await baselineContext.Database.MigrateAsync(
                AiGatewayProductionUpgradeContract.LastProductionMigrationId);
        }

        var baseline = await AiGatewayProductionUpgradePreflight.InspectAsync(
            database.ConnectionString);
        baseline.Should().BeEquivalentTo(new AiGatewayProductionUpgradeInspection(
            AiGatewayProductionUpgradeState.ProductionBaseline,
            AiGatewayProductionUpgradeContract.ExpectedProductionHistorySha256,
            AiGatewayProductionUpgradeContract.ProductionMigrationIds.Count,
            AiGatewayProductionUpgradeContract.ExpectedProductionSchemaSha256,
            621));

        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        var tablesBefore = await QueryTableNamesAsync(connection, "aigateway");
        var sequenceBefore = await ReadInt64Async(
            connection,
            "SELECT nextval('aigateway.model_quota_fencing_seq');");

        var now = DateTimeOffset.UtcNow;
        var userId = Guid.NewGuid();
        await using (var seedContext = CreateDbContext(database.ConnectionString))
        {
            var model = new LanguageModel(
                "production-upgrade-test",
                "preserved-model",
                "http://model.internal.example/v1",
                null,
                new ModelParameters
                {
                    MaxTokens = 65536,
                    MaxOutputTokens = 4096,
                    Temperature = 0.2f
                });
            var template = new ConversationTemplate(
                "preserved-template",
                "production migration preservation evidence",
                "Answer only from trusted evidence.",
                model.Id,
                new TemplateSpecification { MaxTokens = 4096, Temperature = 0.2f });
            var session = new Session(userId, template.Id);
            session.AddMessage(
                "preserved-message",
                MessageType.User,
                new MessageModelSnapshot(model.Id.Value, model.Name, 65536, 4096));
            var tool = new ToolRegistration(
                "production.upgrade.preserved",
                "Preserved tool",
                "Read-only production migration evidence tool.",
                ToolProviderType.CloudReadonly,
                ToolRegistrationTargetType.Plugin,
                "production-upgrade-test",
                "{\"additionalProperties\":false,\"properties\":{},\"type\":\"object\"}",
                "{\"additionalProperties\":false,\"properties\":{},\"type\":\"object\"}",
                AiToolRiskLevel.Low,
                "AiRead.ProductionSummary",
                false,
                true,
                30,
                ToolAuditLevel.Standard,
                now,
                businessDomains: ["Production"],
                dataBoundary: ToolDataBoundary.GovernedBusinessReadOnly);
            var reservation = new ModelQuotaReservation(
                "tenant-hash",
                userId,
                "role-hash",
                model.Id,
                "endpoint-preserved",
                "pool-preserved",
                now,
                now.AddMinutes(1),
                128,
                64,
                1,
                sequenceBefore,
                "correlation-preserved",
                now,
                now.AddMinutes(2));

            seedContext.AddRange(model, template, session, tool, reservation);
            await seedContext.SaveChangesAsync();
        }

        await using (var upgradeContext = CreateDbContext(database.ConnectionString))
        {
            await upgradeContext.Database.MigrateAsync();
        }

        var current = await AiGatewayProductionUpgradePreflight.InspectAsync(
            database.ConnectionString);
        current.State.Should().Be(AiGatewayProductionUpgradeState.Current);

        var tablesAfter = await QueryTableNamesAsync(connection, "aigateway");
        tablesBefore.Except(tablesAfter, StringComparer.Ordinal)
            .Should().BeEquivalentTo(RetiredTables);
        tablesAfter.Should().Contain(CurrentTables);
        tablesAfter.Should().NotContain(RetiredTables);

        await using (var verifyContext = CreateDbContext(database.ConnectionString))
        {
            (await verifyContext.LanguageModels.CountAsync(model =>
                    model.Name == "preserved-model"))
                .Should().Be(1);
            (await verifyContext.ConversationTemplates.CountAsync(template =>
                    template.Name == "preserved-template"))
                .Should().Be(1);
            (await verifyContext.Sessions.CountAsync(session => session.UserId == userId))
                .Should().Be(1);
            (await verifyContext.Messages.CountAsync(message =>
                    message.Content == "preserved-message"))
                .Should().Be(1);
            (await verifyContext.ToolRegistrations.CountAsync(tool =>
                    tool.ToolCode == "production.upgrade.preserved"))
                .Should().Be(1);
            (await verifyContext.ModelQuotaReservations.CountAsync(reservation =>
                    reservation.CorrelationHash == "correlation-preserved"))
                .Should().Be(1);
        }

        var sequenceAfter = await ReadInt64Async(
            connection,
            "SELECT nextval('aigateway.model_quota_fencing_seq');");
        sequenceAfter.Should().BeGreaterThan(sequenceBefore);

        var indexes = await QueryIndexNamesAsync(connection, "aigateway");
        indexes.Should().Contain(
            "ix_agent_session_states_user_expiry",
            "ux_model_quota_reservations_correlation",
            "ix_model_quota_reservations_endpoint_window");

        var history = await QueryMigrationIdsAsync(connection);
        history.Should().Equal(
            AiGatewayProductionUpgradeContract.ProductionMigrationIds
                .Append(AiGatewayProductionUpgradeContract.CurrentUpgradeMigrationId));
    }

    [Fact]
    public async Task ProductionHistoryWithSchemaDrift_ShouldFailPreflightWithoutWritingHistory()
    {
        await using var database = await PostgresScratchDatabase.CreateAsync(
            fixture.ConnectionString,
            "aicopilot_gateway_unknown_production_state");
        await using (var baselineContext = CreateDbContext(database.ConnectionString))
        {
            await baselineContext.Database.MigrateAsync(
                AiGatewayProductionUpgradeContract.LastProductionMigrationId);
        }

        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using (var driftCommand = connection.CreateCommand())
        {
            driftCommand.CommandText =
                "ALTER TABLE aigateway.sessions ADD COLUMN unexpected_production_drift text;";
            await driftCommand.ExecuteNonQueryAsync();
        }

        var action = () => AiGatewayProductionUpgradePreflight.InspectAsync(
            database.ConnectionString);

        await action.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*unknown schema/history state*Do not infer or insert migration history*");
        var history = await QueryMigrationIdsAsync(connection);
        history.Should().Equal(AiGatewayProductionUpgradeContract.ProductionMigrationIds);
        history.Should().NotContain(AiGatewayProductionUpgradeContract.CurrentUpgradeMigrationId);
    }

    public static TheoryData<string> StructuralDriftCommands => new()
    {
        """
        DROP INDEX aigateway.ux_model_quota_reservations_correlation;
        CREATE INDEX ux_model_quota_reservations_correlation
            ON aigateway.model_quota_reservations (correlation_hash);
        """,
        """
        ALTER TABLE aigateway.model_quota_reservations
            DROP CONSTRAINT "PK_model_quota_reservations";
        ALTER TABLE aigateway.model_quota_reservations
            ADD CONSTRAINT "PK_model_quota_reservations" PRIMARY KEY (correlation_hash);
        """,
        """
        CREATE FUNCTION aigateway.structural_drift_trigger_function()
        RETURNS trigger
        LANGUAGE plpgsql
        AS $$
        BEGIN
            NEW.correlation_hash := NEW.correlation_hash;
            RETURN NEW;
        END;
        $$;
        CREATE TRIGGER structural_drift_trigger
            BEFORE UPDATE ON aigateway.model_quota_reservations
            FOR EACH ROW EXECUTE FUNCTION aigateway.structural_drift_trigger_function();
        """,
        """
        ALTER TABLE aigateway.model_quota_reservations ENABLE ROW LEVEL SECURITY;
        CREATE POLICY structural_drift_policy
            ON aigateway.model_quota_reservations
            USING (true);
        """
    };

    [Theory]
    [MemberData(nameof(StructuralDriftCommands))]
    public async Task ProductionHistoryWithSameNamedStructuralDrift_ShouldFailPreflight(
        string driftSql)
    {
        await using var database = await PostgresScratchDatabase.CreateAsync(
            fixture.ConnectionString,
            "aicopilot_gateway_structural_drift");
        await using (var baselineContext = CreateDbContext(database.ConnectionString))
        {
            await baselineContext.Database.MigrateAsync(
                AiGatewayProductionUpgradeContract.LastProductionMigrationId);
        }

        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using (var driftCommand = connection.CreateCommand())
        {
            driftCommand.CommandText = driftSql;
            await driftCommand.ExecuteNonQueryAsync();
        }

        var action = () => AiGatewayProductionUpgradePreflight.InspectAsync(
            database.ConnectionString);

        await action.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*unknown schema/history state*Do not infer or insert migration history*");
        var history = await QueryMigrationIdsAsync(connection);
        history.Should().Equal(AiGatewayProductionUpgradeContract.ProductionMigrationIds);
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

    private static async Task<long> ReadInt64Async(
        NpgsqlConnection connection,
        string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return (long)(await command.ExecuteScalarAsync()
            ?? throw new InvalidOperationException("Expected a PostgreSQL bigint result."));
    }

    private static async Task<string[]> QueryIndexNamesAsync(
        NpgsqlConnection connection,
        string schemaName)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT indexname
            FROM pg_indexes
            WHERE schemaname = @schemaName
            ORDER BY indexname;
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

    private static async Task<string[]> QueryMigrationIdsAsync(NpgsqlConnection connection)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT "MigrationId"
            FROM aigateway."__EFMigrationsHistory_AiGateway"
            ORDER BY "MigrationId";
            """;
        var result = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            result.Add(reader.GetString(0));
        }
        return result.ToArray();
    }
}
