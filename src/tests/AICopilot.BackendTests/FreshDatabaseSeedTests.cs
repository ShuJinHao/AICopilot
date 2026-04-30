using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AICopilot.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace AICopilot.BackendTests;

[Trait("Suite", "FreshDatabase")]
[Trait("Runtime", "DockerRequired")]
public sealed class FreshDatabaseSeedTests
{
    private const string IdentityStoreBaselineMigrationId = "20260429021832_IdentityStoreMigrationBaseline";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    [Fact]
    public async Task FreshDb_ShouldSeedBootstrapAdminRolesPermissions_AndNoBusinessData()
    {
        await using var fixture = new CoreAICopilotAppFixture();
        await fixture.InitializeAsync();
        fixture.ClearAuthToken();

        using var response = await fixture.HttpClient.GetAsync("/api/identity/initialization-status");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var status = (await response.Content.ReadFromJsonAsync<InitializationStatusDto>(JsonOptions))!;

        status.HasAdminRole.Should().BeTrue();
        status.HasUserRole.Should().BeTrue();
        status.BootstrapAdminConfigured.Should().BeTrue();
        status.HasAdminUser.Should().BeTrue();
        status.IsInitialized.Should().BeTrue();

        await using var dbContext = await CreateDbContextAsync(fixture);
        await using var identityDbContext = await CreateIdentityStoreDbContextAsync(fixture);

        var roles = await identityDbContext.Roles
            .OrderBy(role => role.Name)
            .ToListAsync();

        roles.Select(role => role.Name!).Should().Equal("Admin", "User");
        (await identityDbContext.Users.CountAsync()).Should().Be(1);

        await using var aiGatewayDbContext = await CreateAiGatewayDbContextAsync(fixture);
        (await aiGatewayDbContext.LanguageModels.CountAsync()).Should().Be(0);
        (await aiGatewayDbContext.ConversationTemplates.CountAsync()).Should().Be(0);
        (await aiGatewayDbContext.ApprovalPolicies.CountAsync()).Should().Be(0);

        await using var ragDbContext = await CreateRagDbContextAsync(fixture);
        (await ragDbContext.EmbeddingModels.CountAsync()).Should().Be(0);
        (await ragDbContext.KnowledgeBases.CountAsync()).Should().Be(0);

        await using var dataAnalysisDbContext = await CreateDataAnalysisDbContextAsync(fixture);
        (await dataAnalysisDbContext.BusinessDatabases.CountAsync()).Should().Be(0);

        var adminRole = roles.Single(role => role.Name == "Admin");
        var userRole = roles.Single(role => role.Name == "User");

        var adminPermissions = await identityDbContext.RoleClaims
            .Where(claim => claim.RoleId == adminRole.Id && claim.ClaimType == "permission")
            .Select(claim => claim.ClaimValue!)
            .OrderBy(value => value)
            .ToListAsync();

        var userPermissions = await identityDbContext.RoleClaims
            .Where(claim => claim.RoleId == userRole.Id && claim.ClaimType == "permission")
            .Select(claim => claim.ClaimValue!)
            .OrderBy(value => value)
            .ToListAsync();

        adminPermissions.Should().Contain("Identity.CreateRole");
        adminPermissions.Should().Contain("Identity.DisableUser");
        adminPermissions.Should().Contain("Identity.EnableUser");
        adminPermissions.Should().Contain("Identity.ResetUserPassword");
        adminPermissions.Should().Contain("Identity.DeleteRole");
        adminPermissions.Should().Contain("Identity.GetListAuditLogs");
        adminPermissions.Should().Contain("DataAnalysis.GetListBusinessDatabases");
        adminPermissions.Should().Contain("Rag.UploadDocument");

        userPermissions.Should().BeEquivalentTo(
            "AiGateway.CreateSession",
            "AiGateway.GetSession",
            "AiGateway.GetListSessions",
            "AiGateway.Chat");

        await using var mcpDbContext = await CreateMcpDbContextAsync(fixture);
        (await mcpDbContext.McpServerInfos.CountAsync()).Should().Be(0);

        await AssertSplitModuleTablesAsync(dbContext);
        await AssertIdentityStoreMigrationHistoryAsync(dbContext);
    }

    private static async Task<AiCopilotDbContext> CreateDbContextAsync(AICopilotAppFixture fixture)
    {
        var connectionString = await fixture.GetConnectionStringAsync();
        var options = new DbContextOptionsBuilder<AiCopilotDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new AiCopilotDbContext(options);
    }

    private static async Task<IdentityStoreDbContext> CreateIdentityStoreDbContextAsync(AICopilotAppFixture fixture)
    {
        var connectionString = await fixture.GetConnectionStringAsync();
        var options = new DbContextOptionsBuilder<IdentityStoreDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new IdentityStoreDbContext(options);
    }

    private static async Task<AiGatewayDbContext> CreateAiGatewayDbContextAsync(AICopilotAppFixture fixture)
    {
        var connectionString = await fixture.GetConnectionStringAsync();
        var options = new DbContextOptionsBuilder<AiGatewayDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new AiGatewayDbContext(options);
    }

    private static async Task<McpServerDbContext> CreateMcpDbContextAsync(AICopilotAppFixture fixture)
    {
        var connectionString = await fixture.GetConnectionStringAsync();
        var options = new DbContextOptionsBuilder<McpServerDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new McpServerDbContext(options);
    }

    private static async Task<DataAnalysisDbContext> CreateDataAnalysisDbContextAsync(AICopilotAppFixture fixture)
    {
        var connectionString = await fixture.GetConnectionStringAsync();
        var options = new DbContextOptionsBuilder<DataAnalysisDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new DataAnalysisDbContext(options);
    }

    private static async Task<RagDbContext> CreateRagDbContextAsync(AICopilotAppFixture fixture)
    {
        var connectionString = await fixture.GetConnectionStringAsync();
        var options = new DbContextOptionsBuilder<RagDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new RagDbContext(options);
    }

    private static async Task AssertSplitModuleTablesAsync(AiCopilotDbContext dbContext)
    {
        var expectedModuleTables = new[]
        {
            "identity.\"AspNetRoles\"",
            "identity.\"AspNetUsers\"",
            "identity.\"AspNetRoleClaims\"",
            "identity.\"AspNetUserClaims\"",
            "identity.\"AspNetUserLogins\"",
            "identity.\"AspNetUserRoles\"",
            "identity.\"AspNetUserTokens\"",
            "aigateway.language_models",
            "aigateway.conversation_templates",
            "aigateway.approval_policies",
            "aigateway.sessions",
            "aigateway.messages",
            "rag.embedding_models",
            "rag.knowledge_bases",
            "rag.documents",
            "rag.document_chunks",
            "dataanalysis.business_databases",
            "mcp.mcp_server_info"
        };

        var forbiddenPublicTables = new[]
        {
            "public.\"AspNetRoles\"",
            "public.\"AspNetUsers\"",
            "public.\"AspNetRoleClaims\"",
            "public.\"AspNetUserClaims\"",
            "public.\"AspNetUserLogins\"",
            "public.\"AspNetUserRoles\"",
            "public.\"AspNetUserTokens\"",
            "public.language_models",
            "public.conversation_templates",
            "public.approval_policies",
            "public.sessions",
            "public.messages",
            "public.embedding_models",
            "public.knowledge_bases",
            "public.documents",
            "public.document_chunks",
            "public.business_databases",
            "public.mcp_server_info"
        };

        await dbContext.Database.OpenConnectionAsync();
        try
        {
            foreach (var tableName in expectedModuleTables)
            {
                (await ResolveTableAsync(dbContext, tableName)).Should().Be(
                    tableName,
                    $"{tableName} must be owned by its split module schema after fresh database migration");
            }

            foreach (var tableName in forbiddenPublicTables)
            {
                (await ResolveTableAsync(dbContext, tableName)).Should().BeNull(
                    $"{tableName} must not remain after split module migrations move ownership out of public");
            }
        }
        finally
        {
            await dbContext.Database.CloseConnectionAsync();
        }
    }

    private static async Task AssertIdentityStoreMigrationHistoryAsync(AiCopilotDbContext dbContext)
    {
        await dbContext.Database.OpenConnectionAsync();
        try
        {
            await using var command = dbContext.Database.GetDbConnection().CreateCommand();
            command.CommandText = """
                SELECT COUNT(*)
                FROM "__EFMigrationsHistory"
                WHERE "MigrationId" = @migration_id
                """;

            var parameter = command.CreateParameter();
            parameter.ParameterName = "migration_id";
            parameter.Value = IdentityStoreBaselineMigrationId;
            command.Parameters.Add(parameter);

            var result = await command.ExecuteScalarAsync();
            Convert.ToInt32(result).Should().Be(1, "IdentityStoreDbContext must have an applied baseline migration");
        }
        finally
        {
            await dbContext.Database.CloseConnectionAsync();
        }
    }

    private static async Task<string?> ResolveTableAsync(DbContext dbContext, string tableName)
    {
        await using var command = dbContext.Database.GetDbConnection().CreateCommand();
        command.CommandText = "SELECT to_regclass(@table_name)::text";

        var parameter = command.CreateParameter();
        parameter.ParameterName = "table_name";
        parameter.Value = tableName;
        command.Parameters.Add(parameter);

        var result = await command.ExecuteScalarAsync();
        return result is null or DBNull ? null : (string)result;
    }

    private sealed record InitializationStatusDto(
        bool HasAdminRole,
        bool HasUserRole,
        bool HasAdminUser,
        bool BootstrapAdminConfigured,
        bool IsInitialized);
}
