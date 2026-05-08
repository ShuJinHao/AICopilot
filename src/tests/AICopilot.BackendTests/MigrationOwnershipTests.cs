using AICopilot.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace AICopilot.BackendTests;

[Trait("Suite", "MigrationOwnership")]
public sealed class MigrationOwnershipTests
{
    private const string ConnectionString = "Host=localhost;Database=aicopilot_migration_ownership;Username=test;Password=test";

    [Fact]
    public void AiCopilotDbContext_ShouldNotOwnSplitDomainTables()
    {
        using var dbContext = CreateAiCopilotDbContext();

        MigratedTableIds(dbContext).Should().NotContain(new[]
        {
            "identity.AspNetUsers",
            "identity.AspNetRoles",
            "identity.AspNetUserClaims",
            "identity.AspNetUserLogins",
            "identity.AspNetUserRoles",
            "identity.AspNetRoleClaims",
            "identity.AspNetUserTokens",
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
        });
    }

    [Fact]
    public void IdentityStoreDbContext_ShouldOwnOnlyIdentityTables_AndExcludeAuditLogs()
    {
        using var dbContext = CreateIdentityStoreDbContext();

        MigratedTableIds(dbContext).Should().BeEquivalentTo(new[]
        {
            "identity.AspNetUsers",
            "identity.AspNetRoles",
            "identity.AspNetUserClaims",
            "identity.AspNetUserLogins",
            "identity.AspNetUserRoles",
            "identity.AspNetRoleClaims",
            "identity.AspNetUserTokens"
        });

        IsExcludedTable(dbContext, null, "audit_logs").Should().BeTrue();
    }

    [Fact]
    public void SplitDomainDbContexts_ShouldExcludeSharedOutboxFromMigrations()
    {
        using var aiGateway = CreateAiGatewayDbContext();
        using var rag = CreateRagDbContext();
        using var dataAnalysis = CreateDataAnalysisDbContext();
        using var mcpServer = CreateMcpServerDbContext();

        IsExcludedTable(aiGateway, "outbox", "outbox_messages").Should().BeTrue();
        IsExcludedTable(rag, "outbox", "outbox_messages").Should().BeTrue();
        IsExcludedTable(dataAnalysis, "outbox", "outbox_messages").Should().BeTrue();
        IsExcludedTable(mcpServer, "outbox", "outbox_messages").Should().BeTrue();

        MigratedTableIds(aiGateway).Should().Contain(new[]
        {
            "aigateway.language_models",
            "aigateway.conversation_templates",
            "aigateway.approval_policies",
            "aigateway.sessions",
            "aigateway.messages"
        });
        MigratedTableIds(rag).Should().Contain(new[]
        {
            "rag.embedding_models",
            "rag.knowledge_bases",
            "rag.documents",
            "rag.document_chunks"
        });
        MigratedTableIds(dataAnalysis).Should().Contain("dataanalysis.business_databases");
        MigratedTableIds(mcpServer).Should().Contain("mcp.mcp_server_info");
    }

    [Fact]
    public void MigrationHistoryTables_ShouldCoverMigratedContextsExactly()
    {
        MigrationHistoryTables.MigratedContexts
            .Select(context => context.ContextName)
            .Should().BeEquivalentTo(new[]
            {
                nameof(AiCopilotDbContext),
                nameof(IdentityStoreDbContext),
                nameof(AiGatewayDbContext),
                nameof(RagDbContext),
                nameof(DataAnalysisDbContext),
                nameof(McpServerDbContext)
            });

        MigrationHistoryTables.MigratedContexts
            .Should().OnlyHaveUniqueItems(context => $"{context.Schema}.{context.TableName}");
    }

    [Fact]
    public void SplitContextMigrationIds_ShouldBeGloballyUnique()
    {
        using var aiCopilot = CreateAiCopilotDbContext();
        using var identityStore = CreateIdentityStoreDbContext();
        using var aiGateway = CreateAiGatewayDbContext();
        using var rag = CreateRagDbContext();
        using var dataAnalysis = CreateDataAnalysisDbContext();
        using var mcpServer = CreateMcpServerDbContext();

        var duplicateIds = new DbContext[]
            {
                aiCopilot,
                identityStore,
                aiGateway,
                rag,
                dataAnalysis,
                mcpServer
            }
            .SelectMany(context => context.Database.GetMigrations(), (context, migrationId) => new
            {
                Context = context.GetType().Name,
                MigrationId = migrationId
            })
            .GroupBy(row => row.MigrationId, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => new
            {
                MigrationId = group.Key,
                Contexts = group.Select(row => row.Context).Order(StringComparer.Ordinal).ToArray()
            })
            .ToArray();

        duplicateIds.Should().BeEmpty();
    }

    [Fact]
    public void MigrationWiring_ShouldUseSplitHistoryTablesEverywhere()
    {
        var root = FindAicopilotRoot();

        AssertFileContains(
            Path.Combine(root, "src", "infrastructure", "AICopilot.EntityFrameworkCore", "DependencyInjection.cs"),
            "MigrationHistoryTables.AiCopilot",
            "MigrationHistoryTables.IdentityStore",
            "MigrationHistoryTables.AiGateway",
            "MigrationHistoryTables.Rag",
            "MigrationHistoryTables.DataAnalysis",
            "MigrationHistoryTables.McpServer");

        AssertFileContains(
            Path.Combine(root, "src", "infrastructure", "AICopilot.EntityFrameworkCore", "AiCopilotDbContextFactory.cs"),
            "MigrationHistoryTables.AiCopilot");
        AssertFileContains(
            Path.Combine(root, "src", "infrastructure", "AICopilot.EntityFrameworkCore", "IdentityStoreDbContext.cs"),
            "MigrationHistoryTables.IdentityStore");
        AssertFileContains(
            Path.Combine(root, "src", "infrastructure", "AICopilot.EntityFrameworkCore", "AiGatewayDbContext.cs"),
            "MigrationHistoryTables.AiGateway");
        AssertFileContains(
            Path.Combine(root, "src", "infrastructure", "AICopilot.EntityFrameworkCore", "RagDbContext.cs"),
            "MigrationHistoryTables.Rag");
        AssertFileContains(
            Path.Combine(root, "src", "infrastructure", "AICopilot.EntityFrameworkCore", "DataAnalysisDbContext.cs"),
            "MigrationHistoryTables.DataAnalysis");
        AssertFileContains(
            Path.Combine(root, "src", "infrastructure", "AICopilot.EntityFrameworkCore", "McpServerDbContext.cs"),
            "MigrationHistoryTables.McpServer");

        AssertFileContains(
            Path.Combine(root, "src", "hosts", "AICopilot.MigrationWorkApp", "Worker.cs"),
            "MigrationHistoryTables.AiCopilot",
            "MigrationHistoryTables.IdentityStore",
            "MigrationHistoryTables.AiGateway",
            "MigrationHistoryTables.Rag",
            "MigrationHistoryTables.DataAnalysis",
            "MigrationHistoryTables.McpServer");
    }

    [Fact]
    public void MigrationSnapshots_ShouldNotContainForeignOwnedCoreTables()
    {
        var root = FindAicopilotRoot();
        var migrationRoot = Path.Combine(root, "src", "infrastructure", "AICopilot.EntityFrameworkCore", "Migrations");

        AssertFileDoesNotContain(
            Path.Combine(migrationRoot, "AiCopilotDbContextModelSnapshot.cs"),
            ForeignCoreTableDeclarationsFor("public"));
        AssertFileDoesNotContain(
            Path.Combine(migrationRoot, "IdentityStoreDbContext", "IdentityStoreDbContextModelSnapshot.cs"),
            ForeignCoreTableDeclarationsFor("identity"));
        AssertFileDoesNotContain(
            Path.Combine(migrationRoot, "AiGatewayDbContext", "AiGatewayDbContextModelSnapshot.cs"),
            ForeignCoreTableDeclarationsFor("aigateway"));
        AssertFileDoesNotContain(
            Path.Combine(migrationRoot, "RagDbContext", "RagDbContextModelSnapshot.cs"),
            ForeignCoreTableDeclarationsFor("rag"));
        AssertFileDoesNotContain(
            Path.Combine(migrationRoot, "DataAnalysisDbContext", "DataAnalysisDbContextModelSnapshot.cs"),
            ForeignCoreTableDeclarationsFor("dataanalysis"));
        AssertFileDoesNotContain(
            Path.Combine(migrationRoot, "McpServerDbContext", "McpServerDbContextModelSnapshot.cs"),
            ForeignCoreTableDeclarationsFor("mcp"));
    }

    private static string[] ForeignCoreTableDeclarationsFor(string ownedSchema)
    {
        return CoreTables
            .Where(table => !StringComparer.OrdinalIgnoreCase.Equals(table.Schema, ownedSchema))
            .Select(table => $"ToTable(\"{table.Name}\", \"{table.Schema}\"")
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    private static readonly (string Schema, string Name)[] CoreTables =
    [
        ("identity", "AspNetUsers"),
        ("identity", "AspNetRoles"),
        ("identity", "AspNetUserClaims"),
        ("identity", "AspNetUserLogins"),
        ("identity", "AspNetUserRoles"),
        ("identity", "AspNetRoleClaims"),
        ("identity", "AspNetUserTokens"),
        ("aigateway", "language_models"),
        ("aigateway", "conversation_templates"),
        ("aigateway", "approval_policies"),
        ("aigateway", "sessions"),
        ("aigateway", "messages"),
        ("rag", "embedding_models"),
        ("rag", "knowledge_bases"),
        ("rag", "documents"),
        ("rag", "document_chunks"),
        ("dataanalysis", "business_databases"),
        ("mcp", "mcp_server_info")
    ];

    private static HashSet<string> MigratedTableIds(DbContext dbContext)
    {
        return dbContext.GetService<IDesignTimeModel>()
            .Model
            .GetEntityTypes()
            .Where(entityType => entityType.GetTableName() is not null)
            .Where(entityType => !entityType.IsTableExcludedFromMigrations())
            .Select(TableId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static bool IsExcludedTable(DbContext dbContext, string? schema, string tableName)
    {
        return dbContext.GetService<IDesignTimeModel>()
            .Model
            .GetEntityTypes()
            .Where(entityType => string.Equals(entityType.GetSchema(), schema, StringComparison.OrdinalIgnoreCase))
            .Where(entityType => string.Equals(entityType.GetTableName(), tableName, StringComparison.OrdinalIgnoreCase))
            .Any(entityType => entityType.IsTableExcludedFromMigrations());
    }

    private static string TableId(IReadOnlyEntityType entityType)
    {
        return $"{entityType.GetSchema() ?? "public"}.{entityType.GetTableName()}";
    }

    private static AiCopilotDbContext CreateAiCopilotDbContext()
    {
        return new AiCopilotDbContext(
            CreateOptions<AiCopilotDbContext>(MigrationHistoryTables.AiCopilot));
    }

    private static IdentityStoreDbContext CreateIdentityStoreDbContext()
    {
        return new IdentityStoreDbContext(
            CreateOptions<IdentityStoreDbContext>(MigrationHistoryTables.IdentityStore));
    }

    private static AiGatewayDbContext CreateAiGatewayDbContext()
    {
        return new AiGatewayDbContext(
            CreateOptions<AiGatewayDbContext>(MigrationHistoryTables.AiGateway));
    }

    private static RagDbContext CreateRagDbContext()
    {
        return new RagDbContext(
            CreateOptions<RagDbContext>(MigrationHistoryTables.Rag));
    }

    private static DataAnalysisDbContext CreateDataAnalysisDbContext()
    {
        return new DataAnalysisDbContext(
            CreateOptions<DataAnalysisDbContext>(MigrationHistoryTables.DataAnalysis));
    }

    private static McpServerDbContext CreateMcpServerDbContext()
    {
        return new McpServerDbContext(
            CreateOptions<McpServerDbContext>(MigrationHistoryTables.McpServer));
    }

    private static DbContextOptions<TContext> CreateOptions<TContext>(MigrationHistoryTable historyTable)
        where TContext : DbContext
    {
        return new DbContextOptionsBuilder<TContext>()
            .UseNpgsqlWithMigrationHistory(ConnectionString, historyTable)
            .Options;
    }

    private static void AssertFileContains(string path, params string[] expectedSnippets)
    {
        var content = File.ReadAllText(path);
        foreach (var expectedSnippet in expectedSnippets)
        {
            content.Should().Contain(expectedSnippet, $"{Path.GetFileName(path)} must keep split migration history wiring");
        }
    }

    private static void AssertFileDoesNotContain(string path, params string[] forbiddenSnippets)
    {
        var content = File.ReadAllText(path);
        foreach (var forbiddenSnippet in forbiddenSnippets)
        {
            content.Should().NotContain(forbiddenSnippet, $"{Path.GetFileName(path)} must not snapshot foreign-owned core tables");
        }
    }

    private static string FindAicopilotRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "src", "infrastructure", "AICopilot.EntityFrameworkCore");
            if (Directory.Exists(candidate))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the AICopilot repository root.");
    }
}
