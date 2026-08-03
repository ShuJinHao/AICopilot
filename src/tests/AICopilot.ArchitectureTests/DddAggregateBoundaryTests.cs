using System.Reflection;
using AICopilot.EntityFrameworkCore;
using AICopilot.EntityFrameworkCore.Outbox;
using AICopilot.EntityFrameworkCore.Persistence;
using AICopilot.Core.AiGateway.Aggregates.ConversationTemplate;
using AICopilot.Core.AiGateway.Aggregates.LanguageModel;
using AICopilot.Core.AiGateway.Aggregates.Sessions;
using AICopilot.Core.AiGateway.Aggregates.Tools;
using AICopilot.Core.DataAnalysis.Aggregates.BusinessDatabase;
using AICopilot.Core.DataAnalysis.Ids;
using AICopilot.Core.McpServer.Aggregates.McpServerInfo;
using AICopilot.Core.Rag.Aggregates.EmbeddingModel;
using AICopilot.Core.Rag.Aggregates.KnowledgeBase;
using AICopilot.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace AICopilot.ArchitectureTests;

public sealed class DddAggregateBoundaryTests
{
    private const string ModelConnectionString =
        "Host=localhost;Database=aicopilot_ddd_model;Username=test;Password=test";
    private static readonly string SolutionRoot = FindSolutionRoot();

    private static readonly Type[] AllowedAggregateRoots =
    [
        typeof(Session),
        typeof(LanguageModel),
        typeof(ConversationTemplate),
        typeof(ToolRegistration),
        typeof(BusinessDatabase),
        typeof(DataSourcePermissionGrant),
        typeof(McpServerInfo),
        typeof(KnowledgeBase),
        typeof(EmbeddingModel),
        typeof(KnowledgeCategory),
        typeof(KnowledgeSupplement)
    ];

    private static readonly Type[] KnownArchitectureDebt = [];

    private static readonly IReadOnlyDictionary<string, string> DbSetTypeClassifications =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["BusinessDatabase"] = "Aggregate",
            ["ConversationTemplate"] = "Aggregate",
            ["DataSourcePermissionGrant"] = "Aggregate",
            ["EmbeddingModel"] = "Aggregate",
            ["KnowledgeBase"] = "Aggregate",
            ["KnowledgeCategory"] = "Aggregate",
            ["KnowledgeSupplement"] = "Aggregate",
            ["LanguageModel"] = "Aggregate",
            ["McpServerInfo"] = "Aggregate",
            ["Session"] = "Aggregate",
            ["ToolRegistration"] = "Aggregate",
            ["Document"] = "AggregateChild",
            ["DocumentChunk"] = "AggregateChild",
            ["Message"] = "AggregateChild",
            ["ModelParameters"] = "OwnedValueObject",
            ["TemplateSpecification"] = "OwnedValueObject",
            ["AgentSessionState"] = "RuntimeRecord",
            ["ModelQuotaReservation"] = "RuntimeRecord",
            ["AuditLogEntry"] = "Audit",
            ["OutboxMessage"] = "Audit",
            ["PersistenceCommitMarker"] = "RuntimeRecord",
            ["ApplicationUser"] = "IdentityRecord",
            ["ExternalIdentityBinding"] = "IdentityRecord",
            ["IdentityRoleClaim`1"] = "IdentityRecord",
            ["IdentityRole`1"] = "IdentityRecord",
            ["IdentityUserClaim`1"] = "IdentityRecord",
            ["IdentityUserLogin`1"] = "IdentityRecord",
            ["IdentityUserRole`1"] = "IdentityRecord",
            ["IdentityUserToken`1"] = "IdentityRecord"
        };

    [Fact]
    public void AggregateRoots_ShouldStayExplicitlyWhitelistedOrKnownDebt()
    {
        var expected = AllowedAggregateRoots
            .Concat(KnownArchitectureDebt)
            .Select(type => type.FullName)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var actual = GetConcreteAggregateRoots()
            .Select(type => type.FullName)
            .Order(StringComparer.Ordinal)
            .ToArray();

        actual.Should().BeEquivalentTo(
            expected,
            "new aggregate roots require an explicit DDD boundary decision; debt roots must be removed from the debt list when fixed");
    }

    [Fact]
    public void AggregateRootNames_ShouldNotUseProcessRecordShapesExceptKnownDebt()
    {
        var knownDebt = KnownArchitectureDebt
            .Select(type => type.FullName)
            .ToHashSet(StringComparer.Ordinal);
        var violations = GetConcreteAggregateRoots()
            .Where(type => IsForbiddenProcessRecordShape(type.Name))
            .Where(type => !knownDebt.Contains(type.FullName!))
            .Select(type => type.FullName)
            .Order(StringComparer.Ordinal)
            .ToArray();

        violations.Should().BeEmpty(
            "queue items, worker heartbeats, execution records and projection events are not allowed as new aggregate-root shapes");
    }

    [Fact]
    public void KnownArchitectureDebt_ShouldStayDocumentedAndCurrent()
    {
        var aggregateRootNames = GetConcreteAggregateRoots()
            .Select(type => type.FullName)
            .ToHashSet(StringComparer.Ordinal);
        var staleDebtEntries = KnownArchitectureDebt
            .Where(type => !aggregateRootNames.Contains(type.FullName!))
            .Select(type => type.FullName)
            .ToArray();
        var contract = File.ReadAllText(Path.Combine(SolutionRoot, "docs", "DDD聚合根边界.md"));

        staleDebtEntries.Should().BeEmpty(
            "debt entries must be deleted from KnownArchitectureDebt as soon as they are no longer aggregate roots");
        foreach (var type in AllowedAggregateRoots.Concat(KnownArchitectureDebt))
        {
            contract.Should().Contain(type.Name);
        }
    }

    [Fact]
    public void DbSets_ShouldBeClassifiedAndDebtTypesShouldNotBeAggregate()
    {
        var dbSetTypes = CreatePersistenceContexts()
            .SelectMany(context => context.GetService<IDesignTimeModel>().Model.GetEntityTypes())
            .Where(entityType => entityType.GetTableName() is not null)
            .Select(entityType => entityType.ClrType.Name)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

        var classifiedNames = DbSetTypeClassifications.Keys.ToHashSet(StringComparer.Ordinal);
        var unclassifiedTypes = dbSetTypes
            .Where(typeName => !classifiedNames.Contains(typeName))
            .ToArray();
        var staleClassifications = classifiedNames
            .Where(typeName => !dbSetTypes.Contains(typeName, StringComparer.Ordinal))
            .Order(StringComparer.Ordinal)
            .ToArray();

        unclassifiedTypes.Should().BeEmpty(
            "every persisted entity must be classified as aggregate, child, projection, queue, audit, runtime record, worker state or identity record; unclassified: {0}",
            string.Join(", ", unclassifiedTypes));
        staleClassifications.Should().BeEmpty(
            "classifications must be deleted when their persisted entity disappears; stale: {0}",
            string.Join(", ", staleClassifications));

        var debtNames = KnownArchitectureDebt
            .Select(type => type.Name)
            .ToHashSet(StringComparer.Ordinal);
        var debtClassifiedAsAggregate = DbSetTypeClassifications
            .Where(item => debtNames.Contains(item.Key))
            .Where(item => item.Value == "Aggregate")
            .Select(item => item.Key)
            .ToArray();

        debtClassifiedAsAggregate.Should().BeEmpty(
            "known debt types are tables, but they are no longer accepted as aggregate-root direction");
    }

    [Fact]
    public void DataSourcePermissionGrant_ShouldRemainAnIndependentAggregateWithIdOnlyCrossReference()
    {
        typeof(IAggregateRoot<DataSourcePermissionGrantId>)
            .IsAssignableFrom(typeof(DataSourcePermissionGrant))
            .Should().BeTrue();
        typeof(DataSourcePermissionGrant)
            .GetProperty(nameof(DataSourcePermissionGrant.Id))!
            .PropertyType.Should().Be(typeof(DataSourcePermissionGrantId));
        typeof(DataSourcePermissionGrant)
            .GetProperty(nameof(DataSourcePermissionGrant.DataSourceId))!
            .PropertyType.Should().Be(typeof(BusinessDatabaseId));
        DbSetTypeClassifications[nameof(DataSourcePermissionGrant)]
            .Should().Be("Aggregate");

        var grantOwnedByBusinessDatabase = GetInstanceMemberTypes(typeof(BusinessDatabase))
            .Where(type => ContainsType(type, typeof(DataSourcePermissionGrant)))
            .ToArray();
        var databaseOwnedByGrant = GetInstanceMemberTypes(typeof(DataSourcePermissionGrant))
            .Where(type => ContainsType(type, typeof(BusinessDatabase)))
            .ToArray();

        grantOwnedByBusinessDatabase.Should().BeEmpty(
            "BusinessDatabase and DataSourcePermissionGrant are separate aggregates and may cross-reference only by BusinessDatabaseId");
        databaseOwnedByGrant.Should().BeEmpty(
            "DataSourcePermissionGrant may reference BusinessDatabase only by BusinessDatabaseId, never by entity navigation");

        using var context = new DataAnalysisDbContext(Options<DataAnalysisDbContext>());
        var model = context.GetService<IDesignTimeModel>().Model;
        var businessDatabaseEntity = model.FindEntityType(typeof(BusinessDatabase))!;
        var permissionGrantEntity = model.FindEntityType(typeof(DataSourcePermissionGrant))!;
        var databaseToGrantNavigations = businessDatabaseEntity.GetNavigations()
            .Select(navigation => navigation.TargetEntityType.ClrType)
            .Concat(businessDatabaseEntity.GetSkipNavigations()
                .Select(navigation => navigation.TargetEntityType.ClrType))
            .Where(type => type == typeof(DataSourcePermissionGrant))
            .ToArray();
        var grantToDatabaseNavigations = permissionGrantEntity.GetNavigations()
            .Select(navigation => navigation.TargetEntityType.ClrType)
            .Concat(permissionGrantEntity.GetSkipNavigations()
                .Select(navigation => navigation.TargetEntityType.ClrType))
            .Where(type => type == typeof(BusinessDatabase))
            .ToArray();

        databaseToGrantNavigations.Should().BeEmpty(
            "the EF model must not turn DataSourcePermissionGrant into a BusinessDatabase child");
        grantToDatabaseNavigations.Should().BeEmpty(
            "the EF model must preserve the BusinessDatabaseId-only cross-aggregate reference");
    }

    private static IReadOnlyCollection<Type> GetInstanceMemberTypes(Type ownerType)
    {
        return ownerType
            .GetMembers(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Select(member => member switch
            {
                PropertyInfo property => property.PropertyType,
                FieldInfo field => field.FieldType,
                _ => null
            })
            .Where(type => type is not null)
            .Cast<Type>()
            .ToArray();
    }

    private static bool ContainsType(Type candidate, Type forbiddenType)
    {
        if (candidate == forbiddenType)
        {
            return true;
        }

        if (candidate.HasElementType && candidate.GetElementType() is { } elementType &&
            ContainsType(elementType, forbiddenType))
        {
            return true;
        }

        return candidate.IsGenericType &&
               candidate.GetGenericArguments().Any(type => ContainsType(type, forbiddenType));
    }

    private static IReadOnlyCollection<Type> GetConcreteAggregateRoots()
    {
        return new[]
            {
                typeof(Session).Assembly,
                typeof(BusinessDatabase).Assembly,
                typeof(McpServerInfo).Assembly,
                typeof(KnowledgeBase).Assembly
            }
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type => type is { IsClass: true, IsAbstract: false })
            .Where(type => typeof(IAggregateRoot).IsAssignableFrom(type))
            .ToArray();
    }

    private static bool IsForbiddenProcessRecordShape(string typeName)
    {
        return typeName.EndsWith("QueueItem", StringComparison.Ordinal)
               || typeName.EndsWith("Heartbeat", StringComparison.Ordinal)
               || typeName.EndsWith("ExecutionRecord", StringComparison.Ordinal)
               || typeName.EndsWith("Event", StringComparison.Ordinal);
    }

    private static IReadOnlyCollection<DbContext> CreatePersistenceContexts()
    {
        return
        [
            new AiCopilotDbContext(Options<AiCopilotDbContext>()),
            new IdentityStoreDbContext(Options<IdentityStoreDbContext>()),
            new AiGatewayDbContext(Options<AiGatewayDbContext>()),
            new RagDbContext(Options<RagDbContext>()),
            new DataAnalysisDbContext(Options<DataAnalysisDbContext>()),
            new McpServerDbContext(Options<McpServerDbContext>()),
            new OutboxDbContext(Options<OutboxDbContext>()),
            new PersistenceCommitMarkerDbContext(Options<PersistenceCommitMarkerDbContext>())
        ];
    }

    private static DbContextOptions<TContext> Options<TContext>()
        where TContext : DbContext
    {
        return new DbContextOptionsBuilder<TContext>()
            .UseNpgsql(ModelConnectionString)
            .Options;
    }

    private static string FindSolutionRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "AICopilot.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate AICopilot.slnx from the test output directory.");
    }
}
