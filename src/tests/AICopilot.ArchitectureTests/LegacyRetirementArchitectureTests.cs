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
        "ApprovalRequest",
        "ArtifactWorkspace",
        "ChatRuntimeSettings",
        "FinalAgent",
        "MessageEvent",
        "Onsite",
        "RoutingModel"
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
}
