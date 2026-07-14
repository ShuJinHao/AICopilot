using System.Reflection;
using System.Text.RegularExpressions;
using AICopilot.Core.AiGateway.Aggregates.AgentTasks;
using AICopilot.Core.AiGateway.Aggregates.ApprovalPolicy;
using AICopilot.Core.AiGateway.Aggregates.Approvals;
using AICopilot.Core.AiGateway.Aggregates.Artifacts;
using AICopilot.Core.AiGateway.Aggregates.ConversationTemplate;
using AICopilot.Core.AiGateway.Aggregates.LanguageModel;
using AICopilot.Core.AiGateway.Aggregates.RoutingModel;
using AICopilot.Core.AiGateway.Aggregates.RuntimeSettings;
using AICopilot.Core.AiGateway.Aggregates.Sessions;
using AICopilot.Core.AiGateway.Aggregates.Skills;
using AICopilot.Core.AiGateway.Aggregates.Tools;
using AICopilot.Core.AiGateway.Aggregates.Uploads;
using AICopilot.Core.DataAnalysis.Aggregates.BusinessDatabase;
using AICopilot.Core.McpServer.Aggregates.McpServerInfo;
using AICopilot.Core.Rag.Aggregates.EmbeddingModel;
using AICopilot.Core.Rag.Aggregates.KnowledgeBase;
using AICopilot.SharedKernel.Domain;

namespace AICopilot.ArchitectureTests;

public sealed class DddAggregateBoundaryTests
{
    private static readonly string SolutionRoot = FindSolutionRoot();

    private static readonly Type[] AllowedAggregateRoots =
    [
        typeof(Session),
        typeof(AgentTask),
        typeof(ArtifactWorkspace),
        typeof(ApprovalRequest),
        typeof(LanguageModel),
        typeof(ConversationTemplate),
        typeof(ApprovalPolicy),
        typeof(RoutingModelConfiguration),
        typeof(ToolRegistration),
        typeof(SkillDefinition),
        typeof(ChatRuntimeSettings),
        typeof(UploadRecord),
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
            ["AgentTask"] = "Aggregate",
            ["ApprovalPolicy"] = "Aggregate",
            ["ApprovalRequest"] = "Aggregate",
            ["ArtifactWorkspace"] = "Aggregate",
            ["BusinessDatabase"] = "Aggregate",
            ["ChatRuntimeSettings"] = "Aggregate",
            ["ConversationTemplate"] = "Aggregate",
            ["DataSourcePermissionGrant"] = "AggregatePendingReview",
            ["EmbeddingModel"] = "Aggregate",
            ["KnowledgeBase"] = "Aggregate",
            ["KnowledgeCategory"] = "Aggregate",
            ["KnowledgeSupplement"] = "Aggregate",
            ["LanguageModel"] = "Aggregate",
            ["McpServerInfo"] = "Aggregate",
            ["RoutingModelConfiguration"] = "Aggregate",
            ["Session"] = "Aggregate",
            ["SkillDefinition"] = "Aggregate",
            ["ToolRegistration"] = "Aggregate",
            ["UploadRecord"] = "Aggregate",
            ["Document"] = "AggregateChild",
            ["DocumentChunk"] = "AggregateChild",
            ["Message"] = "AggregateChild",
            ["MessageEvent"] = "Projection",
            ["AgentTaskRunQueueItem"] = "Queue",
            ["AgentTaskRunAttempt"] = "RuntimeRecord",
            ["AgentWorkerHeartbeat"] = "WorkerState",
            ["ToolExecutionRecord"] = "Audit",
            ["AuditLogEntry"] = "Audit",
            ["OutboxMessage"] = "Audit",
            ["PersistenceCommitMarker"] = "RuntimeRecord",
            ["ExternalIdentityBinding"] = "IdentityRecord"
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
        var dbSetTypes = Directory
            .EnumerateFiles(Path.Combine(SolutionRoot, "src", "infrastructure", "AICopilot.EntityFrameworkCore"), "*.cs", SearchOption.AllDirectories)
            .Where(file => !IsGeneratedOrBuildOutput(file))
            .SelectMany(file => Regex
                .Matches(File.ReadAllText(file), @"\bDbSet<\s*(?<type>[A-Za-z0-9_]+)\s*>")
                .Select(match => match.Groups["type"].Value))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

        dbSetTypes.Should().BeEquivalentTo(
            DbSetTypeClassifications.Keys.Order(StringComparer.Ordinal),
            "every persisted entity must be classified as aggregate, child, projection, queue, audit, runtime record, worker state or identity record");

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

    private static bool IsGeneratedOrBuildOutput(string file)
    {
        return file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
               || file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
               || file.Contains($"{Path.DirectorySeparatorChar}Migrations{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase);
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
