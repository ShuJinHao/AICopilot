using AICopilot.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace AICopilot.BackendTests;

[Collection(CoreBackendTestCollection.Name)]
[Trait("Suite", "MigrationSafety")]
[Trait("Runtime", "DockerRequired")]
public sealed class MigrationSafetyTests(CoreAICopilotAppFixture fixture)
{
    private const string PreIdentityGuidMigration = "20260428150008_DetachAiGatewayFromAiCopilotDbContext";

    [Fact]
    public async Task McpInitialMigration_ShouldMoveLegacyPublicTable_AndPreserveRows()
    {
        await using var database = await ScratchDatabase.CreateAsync(await fixture.GetConnectionStringAsync());

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
        await using var database = await ScratchDatabase.CreateAsync(await fixture.GetConnectionStringAsync());

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
        await using var database = await ScratchDatabase.CreateAsync(await fixture.GetConnectionStringAsync());

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
        }
    }

    [Fact]
    public async Task MigrationHistoryBootstrap_ShouldFail_WhenSplitHistoryIsPartial()
    {
        await using var database = await ScratchDatabase.CreateAsync(await fixture.GetConnectionStringAsync());
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

    private sealed class ScratchDatabase : IAsyncDisposable
    {
        private ScratchDatabase(string adminConnectionString, string databaseName, string connectionString)
        {
            AdminConnectionString = adminConnectionString;
            DatabaseName = databaseName;
            ConnectionString = connectionString;
        }

        public string ConnectionString { get; }

        private string AdminConnectionString { get; }

        private string DatabaseName { get; }

        public static async Task<ScratchDatabase> CreateAsync(string baseConnectionString)
        {
            var databaseName = $"aicopilot_migration_{Guid.NewGuid():N}";
            var adminBuilder = new NpgsqlConnectionStringBuilder(baseConnectionString)
            {
                Database = "postgres"
            };
            var scratchBuilder = new NpgsqlConnectionStringBuilder(baseConnectionString)
            {
                Database = databaseName
            };

            await using var connection = new NpgsqlConnection(adminBuilder.ConnectionString);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = $"CREATE DATABASE {QuoteIdentifier(databaseName)}";
            await command.ExecuteNonQueryAsync();

            return new ScratchDatabase(adminBuilder.ConnectionString, databaseName, scratchBuilder.ConnectionString);
        }

        public async ValueTask DisposeAsync()
        {
            await using var connection = new NpgsqlConnection(AdminConnectionString);
            await connection.OpenAsync();

            await using (var terminate = connection.CreateCommand())
            {
                terminate.CommandText = """
                    SELECT pg_terminate_backend(pid)
                    FROM pg_stat_activity
                    WHERE datname = @database_name
                      AND pid <> pg_backend_pid()
                    """;
                var parameter = terminate.CreateParameter();
                parameter.ParameterName = "database_name";
                parameter.Value = DatabaseName;
                terminate.Parameters.Add(parameter);
                await terminate.ExecuteNonQueryAsync();
            }

            await using var drop = connection.CreateCommand();
            drop.CommandText = $"DROP DATABASE IF EXISTS {QuoteIdentifier(DatabaseName)} WITH (FORCE)";
            await drop.ExecuteNonQueryAsync();
        }

        private static string QuoteIdentifier(string value)
        {
            return "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
        }
    }
}
