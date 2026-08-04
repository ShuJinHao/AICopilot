using AICopilot.EntityFrameworkCore;
using AICopilot.EntityFrameworkCore.Persistence;
using AICopilot.MigrationWorkApp;
using AICopilot.Core.AiGateway.Aggregates.ConversationTemplate;
using AICopilot.Core.AiGateway.Aggregates.LanguageModel;
using AICopilot.Core.AiGateway.Aggregates.Sessions;
using AICopilot.Core.AiGateway.Aggregates.Tools;
using AICopilot.Core.AiGateway.Runtime.ModelQuota;
using AICopilot.SharedKernel.Ai;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

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
        var productionConnectionString = await PrepareProductionOwnedSchemaAsync(database);

        var initial = await AiGatewayProductionUpgradePreflight.InspectAsync(
            productionConnectionString);
        initial.State.Should().Be(AiGatewayProductionUpgradeState.Fresh);

        await using (var dbContext = CreateDbContext(productionConnectionString))
        {
            await MigrationWorkerDatabaseMigrator.RunMigrationsAsync(
                [new MigrationHistoryBootstrapper.MigrationContext(
                    dbContext,
                    MigrationHistoryTables.AiGateway)],
                CancellationToken.None);
        }

        var migrated = await AiGatewayProductionUpgradePreflight.InspectAsync(
            productionConnectionString);
        migrated.State.Should().Be(AiGatewayProductionUpgradeState.Current);

        await using var connection = new NpgsqlConnection(productionConnectionString);
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

    [Theory]
    [InlineData("CREATE SEQUENCE aigateway.leftover_sequence;")]
    [InlineData("CREATE VIEW aigateway.leftover_view AS SELECT 1 AS value;")]
    [InlineData("CREATE FUNCTION aigateway.leftover_function() RETURNS integer LANGUAGE sql AS 'SELECT 1';")]
    [InlineData("CREATE TYPE aigateway.leftover_type AS ENUM ('legacy');")]
    [InlineData("CREATE COLLATION aigateway.leftover_collation (provider = libc, locale = 'C');")]
    [InlineData("CREATE OPERATOR aigateway.=== (LEFTARG = integer, RIGHTARG = integer, FUNCTION = pg_catalog.int4eq);")]
    public async Task NonEmptySchemaWithoutHistory_ShouldNeverBeClassifiedAsFresh(
        string leftoverObjectSql)
    {
        await using var database = await PostgresScratchDatabase.CreateAsync(
            fixture.ConnectionString,
            "aicopilot_gateway_nonempty_without_history");
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await ExecuteNonQueryAsync(connection, "CREATE SCHEMA aigateway;");
        await ExecuteNonQueryAsync(connection, leftoverObjectSql);

        var inspect = async () => await AiGatewayProductionUpgradePreflight.InspectAsync(
            database.ConnectionString);

        await inspect.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*rejected an unknown schema/history state*");
    }

    [Fact]
    public async Task FrozenProductionDatabase_ShouldUpgradeAndPreserveAuthoritativeRecords()
    {
        await using var database = await PostgresScratchDatabase.CreateAsync(
            fixture.ConnectionString,
            "aicopilot_gateway_production_upgrade");
        var productionConnectionString = await PrepareProductionOwnedSchemaAsync(database);
        await using (var baselineContext = CreateDbContext(productionConnectionString))
        {
            await baselineContext.Database.MigrateAsync(
                AiGatewayProductionUpgradeContract.LastProductionMigrationId);
        }

        var baseline = await AiGatewayProductionUpgradePreflight.InspectAsync(
            productionConnectionString);
        baseline.Should().BeEquivalentTo(new AiGatewayProductionUpgradeInspection(
            AiGatewayProductionUpgradeState.ProductionBaseline,
            AiGatewayProductionUpgradeContract.ExpectedProductionHistorySha256,
            AiGatewayProductionUpgradeContract.ProductionMigrationIds.Count,
            AiGatewayProductionUpgradeContract.ExpectedProductionSchemaSha256,
            956));
        var productionPrecision = await ReadColumnPrecisionProjectionAsync(
            productionConnectionString);
        productionPrecision.Sha256.Should().Be(
            AiGatewayProductionUpgradeContract.ExpectedProductionColumnPrecisionProjectionSha256);
        productionPrecision.ColumnCount.Should().Be(
            AiGatewayProductionUpgradeContract.ExpectedProductionColumnCount);
        productionPrecision.TemporalColumnCount.Should().Be(
            AiGatewayProductionUpgradeContract.ExpectedProductionTemporalColumnCount);

        await using var connection = new NpgsqlConnection(productionConnectionString);
        await connection.OpenAsync();
        var tablesBefore = await QueryTableNamesAsync(connection, "aigateway");
        var sequenceBefore = await ReadInt64Async(
            connection,
            "SELECT nextval('aigateway.model_quota_fencing_seq');");

        var now = DateTimeOffset.UtcNow;
        var userId = Guid.NewGuid();
        await using (var seedContext = CreateDbContext(productionConnectionString))
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

        await using (var upgradeContext = CreateDbContext(productionConnectionString))
        {
            await MigrationWorkerDatabaseMigrator.RunMigrationsAsync(
                [new MigrationHistoryBootstrapper.MigrationContext(
                    upgradeContext,
                    MigrationHistoryTables.AiGateway)],
                CancellationToken.None);
        }

        var current = await AiGatewayProductionUpgradePreflight.InspectAsync(
            productionConnectionString);
        current.State.Should().Be(AiGatewayProductionUpgradeState.Current);

        var tablesAfter = await QueryTableNamesAsync(connection, "aigateway");
        tablesBefore.Except(tablesAfter, StringComparer.Ordinal)
            .Should().BeEquivalentTo(RetiredTables);
        tablesAfter.Should().Contain(CurrentTables);
        tablesAfter.Should().NotContain(RetiredTables);

        await using (var verifyContext = CreateDbContext(productionConnectionString))
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
    public async Task EmptyDatabaseWithDefaultPrivilegeDrift_ShouldFailPreflight()
    {
        await using var database = await PostgresScratchDatabase.CreateAsync(
            fixture.ConnectionString,
            "aicopilot_gateway_fresh_default_acl_drift");
        var productionConnectionString = await PrepareProductionOwnedSchemaAsync(database);
        await using var connection = new NpgsqlConnection(productionConnectionString);
        await connection.OpenAsync();
        await ExecuteNonQueryAsync(
            connection,
            """
            ALTER DEFAULT PRIVILEGES IN SCHEMA aigateway
                GRANT SELECT ON TABLES TO PUBLIC;
            """);

        var action = () => AiGatewayProductionUpgradePreflight.InspectAsync(
            productionConnectionString);

        await action.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*unknown schema/history state*Do not infer or insert migration history*");
    }

    [Fact]
    public async Task EmptyDatabaseWithForeignSchemaOwner_ShouldFailPreflight()
    {
        await using var database = await PostgresScratchDatabase.CreateAsync(
            fixture.ConnectionString,
            "aicopilot_gateway_fresh_schema_owner_drift");
        var productionConnectionString = await PrepareProductionOwnedSchemaAsync(database);
        await using (var adminConnection = new NpgsqlConnection(database.ConnectionString))
        {
            await adminConnection.OpenAsync();
            await ExecuteNonQueryAsync(
                adminConnection,
                """
                ALTER SCHEMA aigateway OWNER TO CURRENT_USER;
                GRANT USAGE ON SCHEMA aigateway TO aicopilot;
                """);
        }

        var action = () => AiGatewayProductionUpgradePreflight.InspectAsync(
            productionConnectionString);

        await action.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*unknown schema/history state*Do not infer or insert migration history*");
    }

    [Fact]
    public async Task EmptyDatabaseWithPermissiveSchemaAcl_ShouldFailPreflight()
    {
        await using var database = await PostgresScratchDatabase.CreateAsync(
            fixture.ConnectionString,
            "aicopilot_gateway_fresh_schema_acl_drift");
        var productionConnectionString = await PrepareProductionOwnedSchemaAsync(database);
        await using var connection = new NpgsqlConnection(productionConnectionString);
        await connection.OpenAsync();
        await ExecuteNonQueryAsync(
            connection,
            "GRANT USAGE ON SCHEMA aigateway TO PUBLIC;");

        var action = () => AiGatewayProductionUpgradePreflight.InspectAsync(
            productionConnectionString);

        await action.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*unknown schema/history state*Do not infer or insert migration history*");
    }

    [Fact]
    public async Task ProductionHistoryWithSchemaDrift_ShouldFailPreflightWithoutWritingHistory()
    {
        await using var database = await PostgresScratchDatabase.CreateAsync(
            fixture.ConnectionString,
            "aicopilot_gateway_unknown_production_state");
        var productionConnectionString = await PrepareProductionOwnedSchemaAsync(database);
        await using (var baselineContext = CreateDbContext(productionConnectionString))
        {
            await baselineContext.Database.MigrateAsync(
                AiGatewayProductionUpgradeContract.LastProductionMigrationId);
        }

        await using var connection = new NpgsqlConnection(productionConnectionString);
        await connection.OpenAsync();
        await using (var driftCommand = connection.CreateCommand())
        {
            driftCommand.CommandText =
                "ALTER TABLE aigateway.sessions ADD COLUMN unexpected_production_drift text;";
            await driftCommand.ExecuteNonQueryAsync();
        }

        var action = () => AiGatewayProductionUpgradePreflight.InspectAsync(
            productionConnectionString);

        await action.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*unknown schema/history state*Do not infer or insert migration history*");
        var history = await QueryMigrationIdsAsync(connection);
        history.Should().Equal(AiGatewayProductionUpgradeContract.ProductionMigrationIds);
        history.Should().NotContain(AiGatewayProductionUpgradeContract.CurrentUpgradeMigrationId);
    }

    [Fact]
    public async Task ProductionRoleMembershipDrift_ShouldFailFrozenPreflight()
    {
        await using var database = await PostgresScratchDatabase.CreateAsync(
            fixture.ConnectionString,
            "aicopilot_gateway_role_membership_drift");
        var productionConnectionString = await PrepareProductionOwnedSchemaAsync(database);
        await using (var baselineContext = CreateDbContext(productionConnectionString))
        {
            await baselineContext.Database.MigrateAsync(
                AiGatewayProductionUpgradeContract.LastProductionMigrationId);
        }

        const string inheritorRole = "aicopilot_fingerprint_inheritor";
        await using var adminConnection = new NpgsqlConnection(database.ConnectionString);
        await adminConnection.OpenAsync();
        await ExecuteNonQueryAsync(
            adminConnection,
            $"DROP ROLE IF EXISTS {inheritorRole}; CREATE ROLE {inheritorRole} NOLOGIN;");
        try
        {
            await ExecuteNonQueryAsync(
                adminConnection,
                $"GRANT aicopilot TO {inheritorRole};");

            var action = () => AiGatewayProductionUpgradePreflight.InspectAsync(
                productionConnectionString);

            await action.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("*unknown schema/history state*Do not infer or insert migration history*");
            await using var productionConnection = new NpgsqlConnection(productionConnectionString);
            await productionConnection.OpenAsync();
            (await QueryMigrationIdsAsync(productionConnection)).Should()
                .Equal(AiGatewayProductionUpgradeContract.ProductionMigrationIds);
        }
        finally
        {
            await ExecuteNonQueryAsync(
                adminConnection,
                $"REVOKE aicopilot FROM {inheritorRole}; DROP ROLE {inheritorRole};");
        }
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
        """,
        """
        ALTER TABLE aigateway."__EFMigrationsHistory_AiGateway"
            ADD CONSTRAINT "CK_history_rejects_current_upgrade"
            CHECK ("MigrationId" <> '20260804055544_UpgradeHarnessRuntimeFromProduction');
        """,
        """
        CREATE TYPE aigateway.structural_drift_enum AS ENUM ('unexpected');
        """,
        """
        CREATE DOMAIN aigateway.structural_drift_domain AS text
            CHECK (VALUE <> 'unexpected');
        """,
        """
        CREATE FUNCTION aigateway.structural_drift_function(value integer)
        RETURNS integer
        LANGUAGE sql
        IMMUTABLE
        AS 'SELECT value + 1';
        """,
        """
        CREATE MATERIALIZED VIEW aigateway.structural_drift_view
        AS SELECT 1 AS value;
        """,
        """
        GRANT USAGE ON SCHEMA aigateway TO PUBLIC;
        """,
        """
        GRANT SELECT ON TABLE aigateway.model_quota_reservations TO PUBLIC;
        """,
        """
        GRANT SELECT (api_key) ON TABLE aigateway.language_models TO PUBLIC;
        """,
        """
        GRANT USAGE ON SEQUENCE aigateway.model_quota_fencing_seq TO PUBLIC;
        """,
        """
        ALTER SEQUENCE aigateway.model_quota_fencing_seq
            OWNED BY aigateway.agent_tasks.id;
        """,
        """
        ALTER SEQUENCE aigateway.model_quota_fencing_seq CACHE 100;
        """,
        """
        ALTER DEFAULT PRIVILEGES IN SCHEMA aigateway
            GRANT SELECT ON TABLES TO PUBLIC;
        """,
        """
        ALTER TABLE aigateway.messages
            ALTER COLUMN created_at TYPE timestamp(0) with time zone;
        """,
        """
        CREATE SCHEMA structural_drift_external AUTHORIZATION aicopilot;
        CREATE TABLE structural_drift_external.messages_child ()
            INHERITS (aigateway.messages);
        """,
        """
        CREATE COLLATION aigateway.structural_drift_collation
            (provider = libc, locale = 'C');
        """
    };

    [Fact]
    public async Task ProductionUpgradeLock_ShouldExcludeConcurrentMigrationSession()
    {
        await using var database = await PostgresScratchDatabase.CreateAsync(
            fixture.ConnectionString,
            "aicopilot_gateway_upgrade_lock");
        var productionConnectionString = await PrepareProductionOwnedSchemaAsync(database);
        await using var owner = new NpgsqlConnection(productionConnectionString);
        await using var contender = new NpgsqlConnection(productionConnectionString);
        await owner.OpenAsync();
        await contender.OpenAsync();

        await MigrationWorkerDatabaseMigrator.AcquireAiGatewayProductionUpgradeLockAsync(
            owner,
            CancellationToken.None);
        try
        {
            (await TryAcquireProductionUpgradeLockAsync(contender)).Should().BeFalse();
        }
        finally
        {
            await MigrationWorkerDatabaseMigrator.ReleaseAiGatewayProductionUpgradeLockAsync(
                owner,
                CancellationToken.None);
        }

        (await TryAcquireProductionUpgradeLockAsync(contender)).Should().BeTrue();
        await ExecuteNonQueryAsync(
            contender,
            "SELECT pg_advisory_unlock(@lock_id);",
            ("lock_id", MigrationWorkerDatabaseMigrator.AiGatewayProductionUpgradeLockId));
    }

    [Fact]
    public async Task ProductionDdlFence_ShouldBlockConcurrentRelationAndNamespaceChanges()
    {
        await using var database = await PostgresScratchDatabase.CreateAsync(
            fixture.ConnectionString,
            "aicopilot_gateway_ddl_fence");
        var productionConnectionString = await PrepareProductionOwnedSchemaAsync(database);
        await using (var baselineContext = CreateDbContext(productionConnectionString))
        {
            await baselineContext.Database.MigrateAsync(
                AiGatewayProductionUpgradeContract.LastProductionMigrationId);
        }

        await using var contender = new NpgsqlConnection(productionConnectionString);
        await contender.OpenAsync();
        const string inheritorRole = "aicopilot_fence_inheritor";
        await ExecuteNonQueryAsync(
            contender,
            $"DROP ROLE IF EXISTS {inheritorRole}; CREATE ROLE {inheritorRole} NOLOGIN;");

        await using var ownerContext = CreateDbContext(productionConnectionString);
        await ownerContext.Database.OpenConnectionAsync();
        await using var transaction = await ownerContext.Database.BeginTransactionAsync();
        var ownerConnection = (NpgsqlConnection)ownerContext.Database.GetDbConnection();
        await MigrationWorkerDatabaseMigrator.AcquireAiGatewaySchemaDdlFenceAsync(
            ownerConnection,
            CancellationToken.None);

        await ExecuteNonQueryAsync(contender, "SET lock_timeout = '250ms';");

        var alterTable = () => ExecuteNonQueryAsync(
            contender,
            "ALTER TABLE aigateway.sessions ADD COLUMN concurrent_drift text;");
        var tableFailure = await alterTable.Should().ThrowAsync<PostgresException>();
        tableFailure.Which.SqlState.Should().Be(PostgresErrorCodes.LockNotAvailable);

        var alterSequence = () => ExecuteNonQueryAsync(
            contender,
            "ALTER SEQUENCE aigateway.model_quota_fencing_seq CACHE 100;");
        var sequenceFailure = await alterSequence.Should().ThrowAsync<PostgresException>();
        sequenceFailure.Which.SqlState.Should().Be(PostgresErrorCodes.LockNotAvailable);

        foreach (var namespaceDdl in new[]
                 {
                     "CREATE FUNCTION aigateway.concurrent_function() RETURNS integer LANGUAGE sql AS 'SELECT 1';",
                     "CREATE TYPE aigateway.concurrent_type AS ENUM ('unexpected');",
                     "CREATE COLLATION aigateway.concurrent_collation (provider = libc, locale = 'C');",
                     "ALTER DEFAULT PRIVILEGES IN SCHEMA aigateway GRANT SELECT ON TABLES TO PUBLIC;",
                     "ALTER SCHEMA aigateway RENAME TO concurrent_aigateway;"
                 })
        {
            var alterNamespace = () => ExecuteNonQueryAsync(contender, namespaceDdl);
            var namespaceFailure = await alterNamespace.Should().ThrowAsync<PostgresException>();
            namespaceFailure.Which.SqlState.Should().Be(PostgresErrorCodes.LockNotAvailable);
        }

        var grantOwnerRole = () => ExecuteNonQueryAsync(
            contender,
            $"GRANT aicopilot TO {inheritorRole};");
        var membershipFailure = await grantOwnerRole.Should().ThrowAsync<PostgresException>();
        membershipFailure.Which.SqlState.Should().Be(PostgresErrorCodes.LockNotAvailable);

        await transaction.RollbackAsync();

        await ExecuteNonQueryAsync(
            contender,
            "ALTER TABLE aigateway.sessions ADD COLUMN concurrent_drift text;");
        await ExecuteNonQueryAsync(
            contender,
            "ALTER SEQUENCE aigateway.model_quota_fencing_seq CACHE 100;");
        await ExecuteNonQueryAsync(
            contender,
            "CREATE FUNCTION aigateway.concurrent_function() RETURNS integer LANGUAGE sql AS 'SELECT 1';");
        await ExecuteNonQueryAsync(
            contender,
            $"GRANT aicopilot TO {inheritorRole}; REVOKE aicopilot FROM {inheritorRole}; " +
            $"DROP ROLE {inheritorRole};");
    }

    [Fact]
    public async Task PreDeploymentFencePreflight_ShouldRejectLimitedRoleWithoutWritingHistory()
    {
        await using var database = await PostgresScratchDatabase.CreateAsync(
            fixture.ConnectionString,
            "aicopilot_gateway_limited_fence_preflight");
        var productionConnectionString = await PrepareProductionOwnedSchemaAsync(database);
        await using (var baselineContext = CreateDbContext(productionConnectionString))
        {
            await baselineContext.Database.MigrateAsync(
                AiGatewayProductionUpgradeContract.LastProductionMigrationId);
        }
        var before = await AiGatewayProductionUpgradePreflight.InspectAsync(
            productionConnectionString);
        before.State.Should().Be(AiGatewayProductionUpgradeState.ProductionBaseline);

        await using var adminConnection = new NpgsqlConnection(database.ConnectionString);
        await adminConnection.OpenAsync();
        await ExecuteNonQueryAsync(adminConnection, "ALTER ROLE aicopilot NOSUPERUSER;");
        try
        {
            await using var limitedContext = CreateDbContext(productionConnectionString);
            var action = () => MigrationWorkerDatabaseMigrator
                .RunAiGatewayPreDeploymentPreflightAsync(
                    limitedContext,
                    CancellationToken.None);

            var failure = await action.Should().ThrowAsync<PostgresException>();
            failure.Which.SqlState.Should().Be(PostgresErrorCodes.InsufficientPrivilege);
            var unchanged = await AiGatewayProductionUpgradePreflight.InspectAsync(
                productionConnectionString);
            unchanged.Should().Be(before);
        }
        finally
        {
            await ExecuteNonQueryAsync(adminConnection, "ALTER ROLE aicopilot SUPERUSER;");
        }
    }

    [Theory]
    [MemberData(nameof(StructuralDriftCommands))]
    public async Task ProductionHistoryWithSameNamedStructuralDrift_ShouldFailPreflight(
        string driftSql)
    {
        await using var database = await PostgresScratchDatabase.CreateAsync(
            fixture.ConnectionString,
            "aicopilot_gateway_structural_drift");
        var productionConnectionString = await PrepareProductionOwnedSchemaAsync(database);
        await using (var baselineContext = CreateDbContext(productionConnectionString))
        {
            await baselineContext.Database.MigrateAsync(
                AiGatewayProductionUpgradeContract.LastProductionMigrationId);
        }

        await using var connection = new NpgsqlConnection(productionConnectionString);
        await connection.OpenAsync();
        await using (var driftCommand = connection.CreateCommand())
        {
            driftCommand.CommandText = driftSql;
            await driftCommand.ExecuteNonQueryAsync();
        }

        var action = () => AiGatewayProductionUpgradePreflight.InspectAsync(
            productionConnectionString);

        await action.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*unknown schema/history state*Do not infer or insert migration history*");
        var history = await QueryMigrationIdsAsync(connection);
        history.Should().Equal(AiGatewayProductionUpgradeContract.ProductionMigrationIds);
    }

    [Fact]
    public async Task ExternalTriggerFunctionBodyDrift_ShouldChangeFrozenSchemaFingerprint()
    {
        await using var database = await PostgresScratchDatabase.CreateAsync(
            fixture.ConnectionString,
            "aicopilot_gateway_external_trigger_function_drift");
        var productionConnectionString = await PrepareProductionOwnedSchemaAsync(database);
        await using (var baselineContext = CreateDbContext(productionConnectionString))
        {
            await baselineContext.Database.MigrateAsync(
                AiGatewayProductionUpgradeContract.LastProductionMigrationId);
        }

        await using (var adminConnection = new NpgsqlConnection(database.ConnectionString))
        {
            await adminConnection.OpenAsync();
            await ExecuteNonQueryAsync(
                adminConnection,
                "CREATE SCHEMA trigger_external AUTHORIZATION aicopilot;");
        }

        await using var connection = new NpgsqlConnection(productionConnectionString);
        await connection.OpenAsync();
        await ExecuteNonQueryAsync(
            connection,
            """
            CREATE FUNCTION trigger_external.guard_language_model()
            RETURNS trigger
            LANGUAGE plpgsql
            SECURITY DEFINER
            SET search_path = pg_catalog
            AS $function$
            BEGIN
                RETURN NEW;
            END
            $function$;
            CREATE TRIGGER guard_language_model
                BEFORE UPDATE ON aigateway.language_models
                FOR EACH ROW
                EXECUTE FUNCTION trigger_external.guard_language_model();
            """);

        var firstHash = await ReadRejectedSchemaHashAsync(productionConnectionString);
        await ExecuteNonQueryAsync(
            connection,
            """
            CREATE OR REPLACE FUNCTION trigger_external.guard_language_model()
            RETURNS trigger
            LANGUAGE plpgsql
            SECURITY DEFINER
            SET search_path = pg_catalog
            AS $function$
            BEGIN
                NEW.name := NEW.name;
                RETURN NEW;
            END
            $function$;
            """);
        var secondHash = await ReadRejectedSchemaHashAsync(productionConnectionString);

        secondHash.Should().NotBe(firstHash,
            "an external function used by an aigateway trigger is executable schema state");
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

    private static async Task<string> ReadRejectedSchemaHashAsync(string connectionString)
    {
        var action = () => AiGatewayProductionUpgradePreflight.InspectAsync(connectionString);
        var failure = await action.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*unknown schema/history state*schemaSha256=*");
        var marker = "schemaSha256=";
        var message = failure.Which.Message;
        var start = message.IndexOf(marker, StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0);
        start += marker.Length;
        var end = message.IndexOf(' ', start);
        end.Should().BeGreaterThan(start);
        return message[start..end];
    }

    private static async Task<string> PrepareProductionOwnedSchemaAsync(
        PostgresScratchDatabase database)
    {
        await using (var connection = new NpgsqlConnection(database.ConnectionString))
        {
            await connection.OpenAsync();
            await ExecuteNonQueryAsync(
                connection,
                """
                DO $role$
                BEGIN
                    IF NOT EXISTS (
                        SELECT 1 FROM pg_roles WHERE rolname = 'aicopilot') THEN
                        CREATE ROLE aicopilot NOLOGIN SUPERUSER;
                    END IF;
                    ALTER ROLE aicopilot SUPERUSER;
                END
                $role$;
                DO $grant$
                BEGIN
                    EXECUTE format(
                        'GRANT CREATE ON DATABASE %I TO aicopilot',
                        current_database());
                END
                $grant$;
                CREATE SCHEMA aigateway AUTHORIZATION aicopilot;
                """);
        }

        return new NpgsqlConnectionStringBuilder(database.ConnectionString)
        {
            Options = "-c role=aicopilot"
        }.ConnectionString;
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

    private static async Task ExecuteNonQueryAsync(
        NpgsqlConnection connection,
        string sql,
        params (string Name, object Value)[] parameters)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<bool> TryAcquireProductionUpgradeLockAsync(
        NpgsqlConnection connection)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT pg_try_advisory_lock(@lock_id);";
        command.Parameters.AddWithValue(
            "lock_id",
            MigrationWorkerDatabaseMigrator.AiGatewayProductionUpgradeLockId);
        return (bool)(await command.ExecuteScalarAsync()
            ?? throw new InvalidOperationException("Expected PostgreSQL advisory-lock result."));
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

    private static async Task<(
        string Sha256,
        int ColumnCount,
        int TemporalColumnCount)> ReadColumnPrecisionProjectionAsync(
        string connectionString)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT table_name,
                   ordinal_position,
                   column_name,
                   datetime_precision
            FROM information_schema.columns
            WHERE table_schema = 'aigateway'
            ORDER BY table_name, ordinal_position;
            """;
        var lines = new List<string>();
        var temporalColumnCount = 0;
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var precision = reader.IsDBNull(3)
                ? string.Empty
                : reader.GetInt32(3).ToString(CultureInfo.InvariantCulture);
            if (precision.Length > 0)
            {
                temporalColumnCount++;
            }
            lines.Add(string.Join(
                '|',
                reader.GetString(0),
                reader.GetInt32(1).ToString("D4", CultureInfo.InvariantCulture),
                reader.GetString(2),
                precision));
        }

        var canonical = lines.Count == 0
            ? string.Empty
            : string.Join('\n', lines) + "\n";
        var sha256 = Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(canonical)))
            .ToLowerInvariant();
        return (sha256, lines.Count, temporalColumnCount);
    }
}
