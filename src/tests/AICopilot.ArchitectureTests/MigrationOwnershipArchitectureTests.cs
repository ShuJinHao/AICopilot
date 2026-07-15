using AICopilot.EntityFrameworkCore;
using AICopilot.EntityFrameworkCore.Outbox;
using AICopilot.EntityFrameworkCore.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using System.Reflection;

namespace AICopilot.ArchitectureTests;

public sealed class MigrationOwnershipArchitectureTests
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
            "identity.external_identity_bindings",
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
        MigratedTableIds(dbContext).Should().Contain("outbox.outbox_messages");
        MigratedTableIds(dbContext).Should().Contain("persistence.commit_markers");
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
            "identity.AspNetUserTokens",
            "identity.external_identity_bindings"
        });

        IsExcludedTable(dbContext, null, "audit_logs").Should().BeTrue();
    }

    [Fact]
    public void SharedPersistenceTables_ShouldHaveOneMigrationOwner_AndRuntimeOnlyContexts()
    {
        using var aiGateway = CreateAiGatewayDbContext();
        using var rag = CreateRagDbContext();
        using var dataAnalysis = CreateDataAnalysisDbContext();
        using var mcpServer = CreateMcpServerDbContext();
        using var outbox = new OutboxDbContext(
            new DbContextOptionsBuilder<OutboxDbContext>().UseNpgsql(ConnectionString).Options);
        using var markers = new PersistenceCommitMarkerDbContext(
            new DbContextOptionsBuilder<PersistenceCommitMarkerDbContext>()
                .UseNpgsql(ConnectionString)
                .Options);

        IsMappedTable(aiGateway, "outbox", "outbox_messages").Should().BeFalse();
        IsMappedTable(rag, "outbox", "outbox_messages").Should().BeFalse();
        IsMappedTable(dataAnalysis, "outbox", "outbox_messages").Should().BeFalse();
        IsMappedTable(mcpServer, "outbox", "outbox_messages").Should().BeFalse();
        IsMappedTable(outbox, "outbox", "outbox_messages").Should().BeTrue();
        IsExcludedTable(markers, "persistence", "commit_markers").Should().BeTrue();

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
    public void MigrationSnapshots_ShouldNotContainForeignOwnedCoreTables()
    {
        var expectedOwnedSchema = new Dictionary<Type, string>
        {
            [typeof(AiCopilotDbContext)] = "public",
            [typeof(IdentityStoreDbContext)] = "identity",
            [typeof(AiGatewayDbContext)] = "aigateway",
            [typeof(RagDbContext)] = "rag",
            [typeof(DataAnalysisDbContext)] = "dataanalysis",
            [typeof(McpServerDbContext)] = "mcp"
        };
        var snapshotTypes = typeof(AiCopilotDbContext).Assembly.GetTypes()
            .Where(type => !type.IsAbstract && typeof(ModelSnapshot).IsAssignableFrom(type))
            .ToArray();

        snapshotTypes.Should().HaveCount(expectedOwnedSchema.Count);
        foreach (var snapshotType in snapshotTypes)
        {
            var contextType = snapshotType.GetCustomAttribute<DbContextAttribute>()?.ContextType;
            contextType.Should().NotBeNull();
            expectedOwnedSchema.Should().ContainKey(contextType!);
            var snapshot = (ModelSnapshot)Activator.CreateInstance(snapshotType, nonPublic: true)!;
            var snapshotTables = snapshot.Model.GetEntityTypes()
                .Where(entityType => entityType.GetTableName() is not null)
                .Select(entityType => (
                    Schema: entityType.GetSchema() ?? "public",
                    Name: entityType.GetTableName()!))
                .ToHashSet();
            var foreignOwnedTables = CoreTables
                .Where(table => !StringComparer.OrdinalIgnoreCase.Equals(
                    table.Schema,
                    expectedOwnedSchema[contextType!]))
                .Where(snapshotTables.Contains)
                .ToArray();

            foreignOwnedTables.Should().BeEmpty(
                $"{snapshotType.Name} must not snapshot another bounded context's tables");
        }
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
        ("identity", "external_identity_bindings"),
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

    private static bool IsMappedTable(DbContext dbContext, string? schema, string tableName)
    {
        return dbContext.GetService<IDesignTimeModel>()
            .Model
            .GetEntityTypes()
            .Any(entityType =>
                string.Equals(entityType.GetSchema(), schema, StringComparison.OrdinalIgnoreCase)
                && string.Equals(entityType.GetTableName(), tableName, StringComparison.OrdinalIgnoreCase));
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

}
