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
            .UseNpgsql(database.ConnectionString)
            .Options;

        await using (var dbContext = new McpServerDbContext(options))
        {
            await dbContext.Database.MigrateAsync();
        }

        (await ExecuteScalarAsync(database.ConnectionString, "SELECT to_regclass('public.mcp_server_info')::text"))
            .Should().BeNull();
        (await ExecuteScalarAsync(database.ConnectionString, "SELECT to_regclass('mcp.mcp_server_info')::text"))
            .Should().Be("mcp.mcp_server_info");
        (await ExecuteScalarAsync(
            database.ConnectionString,
            "SELECT name || ':' || transport_type || ':' || array_to_string(allowed_tool_names, ',') FROM mcp.mcp_server_info"))
            .Should().Be("legacy-mcp:Stdio:read_status,restart");
    }

    [Fact]
    public async Task IdentityGuidMigration_ShouldFail_WhenIdentitySchemaAlreadyContainsRows()
    {
        await using var database = await ScratchDatabase.CreateAsync(await fixture.GetConnectionStringAsync());

        var options = new DbContextOptionsBuilder<AiCopilotDbContext>()
            .UseNpgsql(database.ConnectionString)
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
