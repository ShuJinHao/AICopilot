using AICopilot.Core.Rag.Aggregates.EmbeddingModel;
using AICopilot.Core.Rag.Aggregates.KnowledgeBase;
using AICopilot.Core.Rag.Ids;
using AICopilot.EntityFrameworkCore;
using AICopilot.EntityFrameworkCore.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace AICopilot.PersistenceTests;

[Collection(PostgresPersistenceTestCollection.Name)]
public sealed class MigrationSafetyTests(PostgresPersistenceFixture fixture)
{
    private const string PreIdentityGuidMigration = "20260428150008_DetachAiGatewayFromAiCopilotDbContext";
    private const string PreDocumentSequenceCalibrationMigration = "20260519091000_AddKnowledgeGovernanceP0";

    [Fact]
    public async Task McpInitialMigration_ShouldMoveLegacyPublicTable_AndPreserveRows()
    {
        await using var database = await PostgresScratchDatabase.CreateAsync(fixture.ConnectionString, "aicopilot_migration");

        await ExecuteNonQueryAsync(
            database.ConnectionString,
            """
            CREATE TABLE public.mcp_server_info (
                id uuid NOT NULL,
                name character varying(100) NOT NULL,
                description character varying(500) NOT NULL,
                command character varying(200) NULL,
                arguments character varying(1000) NOT NULL,
                chat_exposure_mode character varying(50) NOT NULL,
                transport_type character varying(50) NOT NULL,
                is_enabled boolean NOT NULL,
                allowed_tool_names text[] NOT NULL,
                CONSTRAINT "PK_mcp_server_info" PRIMARY KEY (id)
            );

            CREATE UNIQUE INDEX "IX_mcp_server_info_name"
                ON public.mcp_server_info (name);

            INSERT INTO public.mcp_server_info (
                id,
                name,
                description,
                command,
                arguments,
                chat_exposure_mode,
                transport_type,
                is_enabled,
                allowed_tool_names)
            VALUES (
                '11111111-1111-1111-1111-111111111111',
                'legacy-mcp',
                'legacy server',
                'node',
                'server.js',
                'Disabled',
                'Stdio',
                true,
                ARRAY['read_status', 'restart']::text[]);
            """);

        var options = new DbContextOptionsBuilder<McpServerDbContext>()
            .UseNpgsqlWithMigrationHistory(database.ConnectionString, MigrationHistoryTables.McpServer)
            .Options;

        await using (var dbContext = new McpServerDbContext(options))
        {
            await MigrationHistoryBootstrapper.BootstrapLegacyHistoryAsync(
                [new MigrationHistoryBootstrapper.MigrationContext(dbContext, MigrationHistoryTables.McpServer)],
                CancellationToken.None);
            await dbContext.Database.MigrateAsync();
        }

        (await ExecuteScalarAsync(database.ConnectionString, "SELECT to_regclass('public.mcp_server_info')::text"))
            .Should().BeNull();
        (await ExecuteScalarAsync(database.ConnectionString, "SELECT to_regclass('mcp.mcp_server_info')::text"))
            .Should().Be("mcp.mcp_server_info");
        (await ExecuteScalarAsync(
            database.ConnectionString,
            """
            SELECT name || ':' || transport_type || ':' ||
                   string_agg(tool_item ->> 'toolName', ',' ORDER BY tool_item ->> 'toolName')
            FROM mcp.mcp_server_info
            CROSS JOIN LATERAL jsonb_array_elements(allowed_tools) AS tool_item
            GROUP BY name, transport_type
            """))
            .Should().Be("legacy-mcp:Stdio:read_status,restart");
    }

    [Fact]
    public async Task IdentityGuidMigration_ShouldFail_WhenIdentitySchemaAlreadyContainsRows()
    {
        await using var database = await PostgresScratchDatabase.CreateAsync(fixture.ConnectionString, "aicopilot_migration");

        var options = new DbContextOptionsBuilder<AiCopilotDbContext>()
            .UseNpgsqlWithMigrationHistory(database.ConnectionString, MigrationHistoryTables.AiCopilot)
            .Options;

        await using (var dbContext = new AiCopilotDbContext(options))
        {
            await dbContext.Database.MigrateAsync(PreIdentityGuidMigration);
        }

        await ExecuteNonQueryAsync(
            database.ConnectionString,
            """
            CREATE SCHEMA IF NOT EXISTS identity;
            CREATE TABLE identity."AspNetUsers" ("Id" text PRIMARY KEY);
            INSERT INTO identity."AspNetUsers" ("Id") VALUES ('legacy-user');
            """);

        await using var upgradeContext = new AiCopilotDbContext(options);
        Func<Task> act = () => upgradeContext.Database.MigrateAsync();

        var exception = await act.Should().ThrowAsync<Exception>();
        exception.Which.ToString().Should().Contain("Refusing to run destructive Identity GUID migration");
    }

    [Fact]
    public async Task MigrationHistoryBootstrap_ShouldCopyLegacySharedHistory_ToSplitHistoryTables()
    {
        await using var database = await PostgresScratchDatabase.CreateAsync(fixture.ConnectionString, "aicopilot_migration");

        await using var aiCopilotDbContext = new AiCopilotDbContext(
            CreateOptions<AiCopilotDbContext>(database.ConnectionString, MigrationHistoryTables.AiCopilot));
        await using var identityStoreDbContext = new IdentityStoreDbContext(
            CreateOptions<IdentityStoreDbContext>(database.ConnectionString, MigrationHistoryTables.IdentityStore));
        await using var aiGatewayDbContext = new AiGatewayDbContext(
            CreateOptions<AiGatewayDbContext>(database.ConnectionString, MigrationHistoryTables.AiGateway));
        await using var ragDbContext = new RagDbContext(
            CreateOptions<RagDbContext>(database.ConnectionString, MigrationHistoryTables.Rag));
        await using var dataAnalysisDbContext = new DataAnalysisDbContext(
            CreateOptions<DataAnalysisDbContext>(database.ConnectionString, MigrationHistoryTables.DataAnalysis));
        await using var mcpServerDbContext = new McpServerDbContext(
            CreateOptions<McpServerDbContext>(database.ConnectionString, MigrationHistoryTables.McpServer));

        var migrationContexts = new[]
        {
            new MigrationHistoryBootstrapper.MigrationContext(aiCopilotDbContext, MigrationHistoryTables.AiCopilot),
            new MigrationHistoryBootstrapper.MigrationContext(identityStoreDbContext, MigrationHistoryTables.IdentityStore),
            new MigrationHistoryBootstrapper.MigrationContext(aiGatewayDbContext, MigrationHistoryTables.AiGateway),
            new MigrationHistoryBootstrapper.MigrationContext(ragDbContext, MigrationHistoryTables.Rag),
            new MigrationHistoryBootstrapper.MigrationContext(dataAnalysisDbContext, MigrationHistoryTables.DataAnalysis),
            new MigrationHistoryBootstrapper.MigrationContext(mcpServerDbContext, MigrationHistoryTables.McpServer)
        };

        var legacyRows = migrationContexts
            .Select(context => (
                context.HistoryTable,
                MigrationId: context.DbContext.Database.GetMigrations().First()))
            .ToArray();

        await CreateLegacyHistoryAsync(
            database.ConnectionString,
            legacyRows.Select(row => row.MigrationId).ToArray());

        await MigrationHistoryBootstrapper.BootstrapLegacyHistoryAsync(migrationContexts, CancellationToken.None);
        await MigrationHistoryBootstrapper.BootstrapLegacyHistoryAsync(migrationContexts, CancellationToken.None);

        foreach (var (historyTable, migrationId) in legacyRows)
        {
            (await ExecuteScalarAsync(
                database.ConnectionString,
                $"SELECT to_regclass('{RegclassName(historyTable)}')::text"))
                .Should().NotBeNull($"{historyTable.ContextName} split history table must be created");

            (await CountMigrationRowsAsync(database.ConnectionString, historyTable, migrationId))
                .Should().Be(1, $"{historyTable.ContextName} legacy history row must be copied exactly once");
            (await CountMigrationRowsAsync(database.ConnectionString, historyTable))
                .Should().Be(1, $"{historyTable.ContextName} split history table must not receive other context rows");
        }
    }

    [Fact]
    public async Task FreshDatabaseMigration_ShouldCreateEverySplitHistoryTable_WithRows()
    {
        await using var database = await PostgresScratchDatabase.CreateAsync(fixture.ConnectionString, "aicopilot_migration");

        await using var aiCopilotDbContext = new AiCopilotDbContext(
            CreateOptions<AiCopilotDbContext>(database.ConnectionString, MigrationHistoryTables.AiCopilot));
        await using var identityStoreDbContext = new IdentityStoreDbContext(
            CreateOptions<IdentityStoreDbContext>(database.ConnectionString, MigrationHistoryTables.IdentityStore));
        await using var aiGatewayDbContext = new AiGatewayDbContext(
            CreateOptions<AiGatewayDbContext>(database.ConnectionString, MigrationHistoryTables.AiGateway));
        await using var ragDbContext = new RagDbContext(
            CreateOptions<RagDbContext>(database.ConnectionString, MigrationHistoryTables.Rag));
        await using var dataAnalysisDbContext = new DataAnalysisDbContext(
            CreateOptions<DataAnalysisDbContext>(database.ConnectionString, MigrationHistoryTables.DataAnalysis));
        await using var mcpServerDbContext = new McpServerDbContext(
            CreateOptions<McpServerDbContext>(database.ConnectionString, MigrationHistoryTables.McpServer));

        var migrationContexts = new[]
        {
            new MigrationHistoryBootstrapper.MigrationContext(aiCopilotDbContext, MigrationHistoryTables.AiCopilot),
            new MigrationHistoryBootstrapper.MigrationContext(identityStoreDbContext, MigrationHistoryTables.IdentityStore),
            new MigrationHistoryBootstrapper.MigrationContext(aiGatewayDbContext, MigrationHistoryTables.AiGateway),
            new MigrationHistoryBootstrapper.MigrationContext(ragDbContext, MigrationHistoryTables.Rag),
            new MigrationHistoryBootstrapper.MigrationContext(dataAnalysisDbContext, MigrationHistoryTables.DataAnalysis),
            new MigrationHistoryBootstrapper.MigrationContext(mcpServerDbContext, MigrationHistoryTables.McpServer)
        };

        await MigrationHistoryBootstrapper.BootstrapLegacyHistoryAsync(migrationContexts, CancellationToken.None);

        foreach (var migrationContext in migrationContexts)
        {
            await migrationContext.DbContext.Database.MigrateAsync();
        }

        foreach (var historyTable in MigrationHistoryTables.MigratedContexts)
        {
            (await ExecuteScalarAsync(
                database.ConnectionString,
                $"SELECT to_regclass('{RegclassName(historyTable)}')::text"))
                .Should().NotBeNull($"{historyTable.ContextName} split history table must exist after fresh migration");
            (await CountMigrationRowsAsync(database.ConnectionString, historyTable))
                .Should().BeGreaterThan(0, $"{historyTable.ContextName} split history table must record applied migrations");
        }

        (await ExecuteScalarAsync(
            database.ConnectionString,
            "SELECT to_regclass('persistence.commit_markers')::text"))
            .Should().Be("persistence.commit_markers");
        (await ExecuteScalarAsync(
            database.ConnectionString,
            """
            SELECT COUNT(*)::text
            FROM pg_index index_metadata
            JOIN pg_class table_metadata ON table_metadata.oid = index_metadata.indrelid
            JOIN pg_namespace schema_metadata ON schema_metadata.oid = table_metadata.relnamespace
            WHERE schema_metadata.nspname = 'persistence'
              AND table_metadata.relname = 'commit_markers'
              AND index_metadata.indisprimary
              AND index_metadata.indisunique
            """))
            .Should().Be("1");
        (await ExecuteScalarAsync(
            database.ConnectionString,
            """
            SELECT COUNT(*)::text
            FROM pg_indexes
            WHERE schemaname = 'persistence'
              AND tablename = 'commit_markers'
              AND indexname = 'ix_commit_markers_created_at_utc'
              AND indexdef LIKE '%(created_at_utc)%'
            """))
            .Should().Be("1");
        (await ExecuteScalarAsync(
            database.ConnectionString,
            "SELECT to_regclass('rag.documents_id_seq')::text"))
            .Should().Be("rag.documents_id_seq");
        (await ExecuteScalarAsync(
            database.ConnectionString,
            """
            SELECT (nextval('rag.documents_id_seq'::regclass) > COALESCE(MAX(id), 0))::text
            FROM rag.documents
            """))
            .Should().Be("true");
    }

    [Fact]
    public async Task RagDocumentSequenceMigration_ShouldAdvancePastExistingExplicitIds()
    {
        await using var database = await PostgresScratchDatabase.CreateAsync(
            fixture.ConnectionString,
            "aicopilot_migration");
        var options = CreateOptions<RagDbContext>(database.ConnectionString, MigrationHistoryTables.Rag);

        await using (var dbContext = new RagDbContext(options))
        {
            await dbContext.Database.MigrateAsync(PreDocumentSequenceCalibrationMigration);

            var embeddingModel = new EmbeddingModel(
                "sequence-upgrade-embedding",
                "OpenAI",
                "https://example.local",
                "text-embedding-test",
                1536,
                8191);
            var knowledgeBase = new KnowledgeBase(
                "sequence-upgrade-kb",
                "sequence upgrade test",
                embeddingModel.Id);
            knowledgeBase.AddDocument(
                new DocumentId(5000),
                "existing-document.txt",
                "documents/existing-document.txt",
                ".txt",
                "sequence-upgrade-hash");

            dbContext.EmbeddingModels.Add(embeddingModel);
            dbContext.KnowledgeBases.Add(knowledgeBase);
            await dbContext.SaveChangesAsync();

            (await ExecuteScalarAsync(
                database.ConnectionString,
                "SELECT (last_value < 5000)::text FROM rag.documents_id_seq"))
                .Should().Be("true");

            await dbContext.Database.MigrateAsync();
        }

        var allocated = await new PostgresDocumentIdAllocator(options).AllocateAsync();
        allocated.Value.Should().BeGreaterThan(5000);
    }

    [Fact]
    public async Task MigrationHistoryBootstrap_ShouldFail_WhenSplitHistoryIsPartial()
    {
        await using var database = await PostgresScratchDatabase.CreateAsync(fixture.ConnectionString, "aicopilot_migration");
        await using var dbContext = new AiCopilotDbContext(
            CreateOptions<AiCopilotDbContext>(database.ConnectionString, MigrationHistoryTables.AiCopilot));

        var migrations = dbContext.Database.GetMigrations().Take(2).ToArray();
        migrations.Should().HaveCount(2);

        await CreateLegacyHistoryAsync(database.ConnectionString, migrations);
        await CreateSplitHistoryAsync(
            database.ConnectionString,
            MigrationHistoryTables.AiCopilot,
            [migrations[0]]);

        var migrationContexts = new[]
        {
            new MigrationHistoryBootstrapper.MigrationContext(dbContext, MigrationHistoryTables.AiCopilot)
        };
        Func<Task> act = () => MigrationHistoryBootstrapper.BootstrapLegacyHistoryAsync(
            migrationContexts,
            CancellationToken.None);

        var exception = await act.Should().ThrowAsync<InvalidOperationException>();
        exception.Which.Message.Should().Contain("partial target history");
        exception.Which.Message.Should().Contain(migrations[1]);
    }

    private static DbContextOptions<TContext> CreateOptions<TContext>(
        string connectionString,
        MigrationHistoryTable historyTable)
        where TContext : DbContext
    {
        return new DbContextOptionsBuilder<TContext>()
            .UseNpgsqlWithMigrationHistory(connectionString, historyTable)
            .Options;
    }

    private static async Task CreateLegacyHistoryAsync(
        string connectionString,
        IReadOnlyCollection<string> migrationIds)
    {
        await ExecuteNonQueryAsync(
            connectionString,
            """
            CREATE TABLE public."__EFMigrationsHistory" (
                "MigrationId" character varying(150) NOT NULL,
                "ProductVersion" character varying(32) NOT NULL,
                CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY ("MigrationId")
            );
            """);

        foreach (var migrationId in migrationIds)
        {
            await InsertMigrationHistoryAsync(
                connectionString,
                new MigrationHistoryTable("Legacy", "public", "__EFMigrationsHistory"),
                migrationId);
        }
    }

    private static async Task CreateSplitHistoryAsync(
        string connectionString,
        MigrationHistoryTable historyTable,
        IReadOnlyCollection<string> migrationIds)
    {
        await ExecuteNonQueryAsync(
            connectionString,
            $"""
             CREATE SCHEMA IF NOT EXISTS {QuoteIdentifier(historyTable.Schema)};
             CREATE TABLE {HistoryTableSql(historyTable)} (
                 "MigrationId" character varying(150) NOT NULL,
                 "ProductVersion" character varying(32) NOT NULL,
                 CONSTRAINT {QuoteIdentifier("PK_" + historyTable.TableName)} PRIMARY KEY ("MigrationId")
             );
             """);

        foreach (var migrationId in migrationIds)
        {
            await InsertMigrationHistoryAsync(connectionString, historyTable, migrationId);
        }
    }

    private static async Task InsertMigrationHistoryAsync(
        string connectionString,
        MigrationHistoryTable historyTable,
        string migrationId)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"""
             INSERT INTO {HistoryTableSql(historyTable)} ("MigrationId", "ProductVersion")
             VALUES (@migration_id, '10.0.6')
             """;

        var parameter = command.CreateParameter();
        parameter.ParameterName = "migration_id";
        parameter.Value = migrationId;
        command.Parameters.Add(parameter);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<int> CountMigrationRowsAsync(
        string connectionString,
        MigrationHistoryTable historyTable,
        string migrationId)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"""
             SELECT COUNT(*)
             FROM {HistoryTableSql(historyTable)}
             WHERE "MigrationId" = @migration_id
             """;

        var parameter = command.CreateParameter();
        parameter.ParameterName = "migration_id";
        parameter.Value = migrationId;
        command.Parameters.Add(parameter);

        var result = await command.ExecuteScalarAsync();
        return Convert.ToInt32(result);
    }

    private static async Task<int> CountMigrationRowsAsync(
        string connectionString,
        MigrationHistoryTable historyTable)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"""
             SELECT COUNT(*)
             FROM {HistoryTableSql(historyTable)}
             """;

        var result = await command.ExecuteScalarAsync();
        return Convert.ToInt32(result);
    }

    private static async Task ExecuteNonQueryAsync(string connectionString, string commandText)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = commandText;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<string?> ExecuteScalarAsync(string connectionString, string commandText)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = commandText;
        var result = await command.ExecuteScalarAsync();
        return result is null or DBNull ? null : Convert.ToString(result);
    }

    private static string HistoryTableSql(MigrationHistoryTable historyTable)
    {
        return $"{QuoteIdentifier(historyTable.Schema)}.{QuoteIdentifier(historyTable.TableName)}";
    }

    private static string RegclassName(MigrationHistoryTable historyTable)
    {
        return $"{historyTable.Schema}.{QuoteIdentifier(historyTable.TableName)}";
    }

    private static string QuoteIdentifier(string value)
    {
        return "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }

}
