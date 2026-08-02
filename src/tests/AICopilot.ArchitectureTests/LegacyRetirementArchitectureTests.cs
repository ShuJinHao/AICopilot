using System.Reflection;
using AICopilot.AiGatewayService.Agents;
using AICopilot.Core.AiGateway.Aggregates.ConversationTemplate;
using AICopilot.Core.AiGateway.Aggregates.LanguageModel;
using AICopilot.Core.AiGateway.Aggregates.Sessions;
using AICopilot.Core.AiGateway.Aggregates.Tools;
using AICopilot.Core.AiGateway.Runtime.AgentSessions;
using AICopilot.Core.AiGateway.Runtime.ModelQuota;
using AICopilot.DataWorker;
using AICopilot.EntityFrameworkCore;
using AICopilot.HttpApi.Infrastructure;
using AICopilot.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;

namespace AICopilot.ArchitectureTests;

public sealed class LegacyRetirementArchitectureTests
{
    private static readonly string SolutionRoot = FindSolutionRoot();

    private static readonly string[] RetiredTypeMarkers =
    [
        ".AgentTasks.",
        ".ApprovalPolicies.",
        ".Artifacts.",
        ".RoutingModels.",
        ".Workflows.",
        "AgentArtifact",
        "AgentTask",
        "ApprovalPolicy",
        ".Aggregates.Approvals.ApprovalRequest",
        "ArtifactWorkspace",
        "ChatRuntimeSettings",
        "FinalAgent",
        "MessageEvent",
        "Onsite",
        "RoutingModel"
    ];

    private static readonly string[] RetiredPathMarkers =
    [
        "/AgentTasks/",
        "/ApprovalPolicies/",
        "/Aggregates/Approvals/",
        "/Artifacts/",
        "/RoutingModels/",
        "/RuntimeSettings/",
        "/src/services/AICopilot.AiGatewayService/Workflows/",
        "/Workspaces/",
        "AgentArtifact",
        "ArtifactWorkspace",
        "AgentTask",
        "ApprovalPolicy",
        "RoutingModel",
        "ChatRuntimeSettings",
        "FinalAgent",
        "IntentRouting",
        "AgentWorkflow",
        "NodeRun",
        "PlanDraft",
        "MessageEvent",
        "/AICopilot.SimulationTests/",
        "/AICopilot.SimulationDockerTests/",
        "/AgentRunThread.vue",
        "/Run-AgentSimulationAcceptance.ps1",
        "/Test-AgentSimulationScope.ps1",
        "/aicopilot-simulation-release-candidate.yml"
    ];

    [Fact]
    public void ProductionAssemblies_ShouldNotContainRetiredOrchestrationTypes()
    {
        var assemblies = new[]
        {
            typeof(Session).Assembly,
            typeof(ChatStreamHandler).Assembly,
            typeof(AiGatewayDbContext).Assembly,
            typeof(AICopilot.Infrastructure.DependencyInjection).Assembly,
            typeof(ApiControllerBase).Assembly,
            typeof(PersistenceMaintenanceWorker).Assembly
        };

        var violations = assemblies
            .Distinct()
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type => type.Namespace is null ||
                !type.Namespace.Contains(".Migrations", StringComparison.Ordinal))
            .Where(type => !typeof(Migration).IsAssignableFrom(type))
            .Select(type => type.FullName ?? type.Name)
            .Where(typeName => RetiredTypeMarkers.Any(marker =>
                typeName.Contains(marker, StringComparison.OrdinalIgnoreCase)))
            .Order(StringComparer.Ordinal)
            .ToArray();

        violations.Should().BeEmpty(
            "the Harness is the only main-chat orchestration and retired framework types must be physically absent");
    }

    [Fact]
    public void RepositorySurface_ShouldNotRestoreRetiredOrchestrationPaths()
    {
        var scanRoots = new[]
        {
            Path.Combine(SolutionRoot, "src"),
            Path.Combine(SolutionRoot, "scripts"),
            Path.Combine(SolutionRoot, ".github", "workflows")
        };
        var violations = scanRoots
            .Where(Directory.Exists)
            .SelectMany(EnumerateSurfaceFiles)
            .Select(path => "/" + Path.GetRelativePath(SolutionRoot, path).Replace('\\', '/'))
            .Where(path => RetiredPathMarkers.Any(marker =>
                path.Contains(marker, StringComparison.OrdinalIgnoreCase)))
            .Order(StringComparer.Ordinal)
            .ToArray();

        violations.Should().BeEmpty(
            "retired orchestration projects, source folders, workers, scripts, and frontend entries must remain physically absent");
    }

    [Fact]
    public void AiGatewayDbContext_ShouldPersistOnlyHarnessRuntimeAndCurrentCatalogs()
    {
        var actualDbSets = typeof(AiGatewayDbContext)
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(property => property.PropertyType.IsGenericType)
            .Where(property => property.PropertyType.GetGenericTypeDefinition() == typeof(DbSet<>))
            .Select(property => property.PropertyType.GetGenericArguments().Single())
            .ToArray();

        actualDbSets.Should().BeEquivalentTo(
        [
            typeof(LanguageModel),
            typeof(ConversationTemplate),
            typeof(Session),
            typeof(Message),
            typeof(ToolRegistration),
            typeof(AgentSessionState),
            typeof(ModelQuotaReservation)
        ]);
    }

    [Fact]
    public void AiGatewayMigrationChain_ShouldRemainSingleHarnessBaseline()
    {
        var migrationDirectory = Path.Combine(
            SolutionRoot,
            "src",
            "infrastructure",
            "AICopilot.EntityFrameworkCore",
            "Migrations",
            "AiGatewayDbContext");
        var migrationFiles = Directory
            .EnumerateFiles(migrationDirectory, "*.cs", SearchOption.TopDirectoryOnly)
            .Select(Path.GetFileName)
            .Order(StringComparer.Ordinal)
            .ToArray();

        migrationFiles.Should().HaveCount(3);
        migrationFiles.Should().ContainSingle(name =>
            string.Equals(name, "AiGatewayDbContextModelSnapshot.cs", StringComparison.Ordinal));
        migrationFiles.Should().ContainSingle(name =>
            name!.EndsWith("_AiGatewayHarnessBaseline.cs", StringComparison.Ordinal) &&
            !name.EndsWith(".Designer.cs", StringComparison.Ordinal));
        migrationFiles.Should().ContainSingle(name =>
            name!.EndsWith("_AiGatewayHarnessBaseline.Designer.cs", StringComparison.Ordinal));
    }

    [Fact]
    public void AiGatewayHttpSurface_ShouldKeepHarnessRoutesAndRemoveRetiredRoutes()
    {
        var controllerTypes = typeof(ApiControllerBase).Assembly
            .GetTypes()
            .Where(type => type.Namespace == "AICopilot.HttpApi.Controllers")
            .Where(type => typeof(ControllerBase).IsAssignableFrom(type))
            .ToArray();
        var routes = controllerTypes
            .SelectMany(controller => controller.GetMethods(BindingFlags.Instance | BindingFlags.Public))
            .SelectMany(method => method.GetCustomAttributes<HttpMethodAttribute>())
            .SelectMany(attribute => attribute.Template is null ? [] : new[] { attribute.Template })
            .ToArray();

        routes.Should().Contain(
        [
            "chat",
            "session/{sessionId:guid}/agent-mode",
            "approval/decision",
            "approval/pending",
            "chat-message/list",
            "tools/catalog"
        ]);
        routes.Should().NotContain(route => new[]
        {
            "agent-task",
            "artifact",
            "business-approval",
            "routing-model",
            "runtime-settings",
            "timeline",
            "agent-upload"
        }.Any(retired => route.Contains(retired, StringComparison.OrdinalIgnoreCase)));
    }

    private static IEnumerable<string> EnumerateSurfaceFiles(string root)
    {
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.TryPop(out var current))
        {
            foreach (var directory in Directory.EnumerateDirectories(current))
            {
                var name = Path.GetFileName(directory);
                if (name is "bin" or "obj" or "node_modules" or "dist" or "artifacts")
                {
                    continue;
                }

                pending.Push(directory);
            }

            foreach (var file in Directory.EnumerateFiles(current))
            {
                yield return file;
            }
        }
    }

    private static string FindSolutionRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "AICopilot.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("AICopilot repository root was not found.");
    }
}
