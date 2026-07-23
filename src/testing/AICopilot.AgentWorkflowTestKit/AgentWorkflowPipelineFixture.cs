using System.Runtime.CompilerServices;
using System.Text.Json;
using AICopilot.AgentPlugin;
using AICopilot.AiGatewayService.Agents;
using AICopilot.AiGatewayService.Approvals;
using AICopilot.AiGatewayService.BusinessPolicies;
using AICopilot.AiGatewayService.BusinessSemantics;
using AICopilot.AiGatewayService.Models;
using AICopilot.AiGatewayService.RoutingModels;
using AICopilot.AiGatewayService.Runtime;
using AICopilot.AiGatewayService.Safety;
using AICopilot.AiGatewayService.Workflows;
using AICopilot.AiGatewayService.Workflows.Executors;
using AICopilot.Core.AiGateway.Aggregates.ApprovalPolicy;
using AICopilot.Core.AiGateway.Aggregates.ConversationTemplate;
using AICopilot.Core.AiGateway.Aggregates.LanguageModel;
using AICopilot.Core.AiGateway.Aggregates.Tools;
using AICopilot.DataAnalysisService.Semantics;
using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Ai;
using AICopilot.SharedKernel.Result;
using MediatR;
using Microsoft.Extensions.Logging.Abstractions;

namespace AICopilot.AgentWorkflowTestKit;

public static class AgentWorkflowPipelineFixture
{
    private const string PluginName = "SafetyFixture";
    private static readonly Guid UserId = Guid.Parse("52e51c2e-1b12-4c92-9769-cacbfbd777b7");

    public static AgentWorkflowPipeline CreatePlanDraftPipeline(
        IReadOnlyCollection<AiToolDefinition> tools)
    {
        var pluginCatalog = new FixedPluginCatalog(PluginName, tools);
        var registrations = tools
            .Where(tool => tool.TargetType == AiToolTargetType.McpServer)
            .Select(CreateRegistration)
            .ToArray();
        var registryGuard = new AICopilot.AiGatewayService.Tools.ToolRegistryGuard(
            new InMemoryReadRepository<ToolRegistration>(registrations),
            new AllowAllIdentityAccessService());
        var toolResolver = new ApprovalToolResolver(
            pluginCatalog,
            new ApprovalRequirementResolver(new InMemoryReadRepository<ApprovalPolicy>()),
            registryGuard,
            new TestCurrentUser(UserId));

        return new AgentWorkflowPipeline(
            CreateIntentRoutingExecutor(
                pluginCatalog,
                new IntentResult
                {
                    Intent = $"Action.{PluginName}",
                    Confidence = 1.0
                }),
            new ToolsPackExecutor(toolResolver, NullLogger<ToolsPackExecutor>.Instance),
            null!,
            null!,
            null!,
            null!,
            null!,
            null!,
            null!,
            null!,
            NullLogger<AgentWorkflowPipeline>.Instance);
    }

    public static AgentWorkflowPipeline CreateKnowledgeBranchPipeline(
        IKnowledgeBaseReadService knowledgeBaseReadService)
    {
        var pluginCatalog = new FixedPluginCatalog(PluginName, []);
        var semanticCatalog = new SemanticQuerySchemaRegistry(new SemanticDefinitionCatalog());
        var businessSemantics = new BusinessSemanticsCatalog(
            new BusinessPolicyCatalog(),
            semanticCatalog,
            new SemanticSummaryProfileCatalog());

        return new AgentWorkflowPipeline(
            CreateIntentRoutingExecutor(
                pluginCatalog,
                new IntentResult
                {
                    Intent = "Knowledge.CancellationFixture",
                    Confidence = 1.0,
                    Query = "observe cancellation quiescence"
                },
                knowledgeBaseReadService),
            new ToolsPackExecutor(null!, NullLogger<ToolsPackExecutor>.Instance),
            new KnowledgeRetrievalExecutor(
                null!,
                knowledgeBaseReadService,
                NullLogger<KnowledgeRetrievalExecutor>.Instance),
            new DataAnalysisExecutor(
                semanticCatalog,
                null!,
                null!,
                NullLogger<DataAnalysisExecutor>.Instance),
            new BusinessPolicyExecutor(
                businessSemantics,
                NullLogger<BusinessPolicyExecutor>.Instance),
            null!,
            null!,
            null!,
            null!,
            null!,
            NullLogger<AgentWorkflowPipeline>.Instance);
    }

    private static IntentRoutingExecutor CreateIntentRoutingExecutor(
        IAgentPluginCatalog pluginCatalog,
        IntentResult intent,
        IKnowledgeBaseReadService? knowledgeBaseReadService = null)
    {
        var runtimeFactory = new FakeRuntimeAgentFactory();
        runtimeFactory.EnqueueText(JsonSerializer.Serialize(new[] { intent }, JsonSerializerOptions.Web));
        var model = FakeRuntimeAgentFactory.CreateModel();
        var template = new ConversationTemplate(
            "IntentRoutingAgent",
            "intent routing fixture",
            "Route the request. {{$IntentList}}",
            model.Id,
            new TemplateSpecification { MaxTokens = 512, Temperature = 0 });
        var configuredFactory = new ConfiguredAgentRuntimeFactory(
            new InMemoryReadRepository<ConversationTemplate>([template]),
            new InMemoryReadRepository<LanguageModel>([model]),
            runtimeFactory);
        var semanticCatalog = new SemanticQuerySchemaRegistry(new SemanticDefinitionCatalog());
        var businessSemantics = new BusinessSemanticsCatalog(
            new BusinessPolicyCatalog(),
            semanticCatalog,
            new SemanticSummaryProfileCatalog());
        var metadata = new AgentExecutionMetadataAccessor();
        var agentBuilder = new IntentRoutingAgentBuilder(
            configuredFactory,
            pluginCatalog,
            knowledgeBaseReadService ?? EmptyKnowledgeBaseReadService.Instance,
            EmptyBusinessDatabaseReadService.Instance,
            businessSemantics,
            new FixedRoutingModelResolver(),
            metadata);

        return new IntentRoutingExecutor(
            new FixedMediator(),
            agentBuilder,
            new KeywordManufacturingSceneClassifier(),
            metadata,
            new FixedRuntimeSettingsProvider(),
            NullLogger<IntentRoutingExecutor>.Instance);
    }

    private static ToolRegistration CreateRegistration(AiToolDefinition tool)
    {
        return new ToolRegistration(
            tool.Name,
            tool.ToolName ?? tool.Name,
            "MCP fixture registration",
            ToolProviderType.Mcp,
            ToolRegistrationTargetType.McpServer,
            tool.TargetName ?? PluginName,
            """{"type":"object","properties":{},"additionalProperties":false}""",
            """{"type":"object","properties":{},"additionalProperties":false}""",
            AiToolRiskLevel.Low,
            requiredPermission: null,
            requiresApproval: false,
            isEnabled: true,
            timeoutSeconds: 30,
            ToolAuditLevel.Standard,
            DateTimeOffset.UtcNow);
    }

    private sealed class FixedPluginCatalog(
        string pluginName,
        IReadOnlyCollection<AiToolDefinition> tools) : IAgentPluginCatalog
    {
        private readonly IAgentPlugin plugin = new FixedPlugin(pluginName, tools);

        public AiToolDefinition[] GetTools(params string[] names) =>
            tools.Where(tool => names.Contains(tool.Name, StringComparer.OrdinalIgnoreCase)).ToArray();

        public AiToolDefinition[] GetPluginTools(string name) =>
            string.Equals(name, pluginName, StringComparison.OrdinalIgnoreCase) ? tools.ToArray() : [];

        public AiToolDefinition[] GetAllTools() => tools.ToArray();

        public IAgentPlugin? GetPlugin(string name) =>
            string.Equals(name, pluginName, StringComparison.OrdinalIgnoreCase) ? plugin : null;

        public IAgentPlugin[] GetAllPlugin() => [plugin];
    }

    private sealed class FixedPlugin(
        string name,
        IReadOnlyCollection<AiToolDefinition> tools) : IAgentPlugin
    {
        public string Name => name;
        public string Description => "Agent workflow fixture plugin";
        public IEnumerable<AiToolDefinition> GetTools() => tools;
        public IEnumerable<string> HighRiskTools => [];
        public ChatExposureMode ChatExposureMode => ChatExposureMode.Advisory;
    }

    private sealed class FixedMediator : IMediator
    {
        public Task<TResponse> Send<TResponse>(
            IRequest<TResponse> request,
            CancellationToken cancellationToken = default)
        {
            if (request is AICopilot.AiGatewayService.Queries.Sessions.GetListChatMessagesQuery)
            {
                return Task.FromResult((TResponse)(object)Result.Success(new List<AiChatMessage>()));
            }

            throw new NotSupportedException($"Unexpected request: {request.GetType().FullName}");
        }

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest => throw new NotSupportedException();

        public Task<object?> Send(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(
            IStreamRequest<TResponse> request,
            CancellationToken cancellationToken = default) => EmptyStream<TResponse>();

        public IAsyncEnumerable<object?> CreateStream(
            object request,
            CancellationToken cancellationToken = default) => EmptyStream<object?>();

        public Task Publish(object notification, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task Publish<TNotification>(
            TNotification notification,
            CancellationToken cancellationToken = default)
            where TNotification : INotification => Task.CompletedTask;

        private static async IAsyncEnumerable<T> EmptyStream<T>(
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            yield break;
        }
    }

    private sealed class FixedRoutingModelResolver : IRoutingModelResolver
    {
        public Task<LanguageModel?> ResolveActiveModelAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<LanguageModel?>(null);
    }

    private sealed class FixedRuntimeSettingsProvider : IAgentRuntimeSettingsProvider
    {
        public Task<ChatRuntimeSettingsDto> GetAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new ChatRuntimeSettingsDto(4, 10, 4, 6, 24000));
    }

    private sealed class AllowAllIdentityAccessService : IIdentityAccessService
    {
        public Task<CurrentUserAccess?> GetCurrentUserAccessAsync(
            Guid userId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<CurrentUserAccess?>(new CurrentUserAccess(userId, "fixture", "User", []));

        public Task<IReadOnlyCollection<string>> GetPermissionsAsync(
            string roleName,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyCollection<string>>([]);

        public Task SyncRolePermissionsAsync(
            string roleName,
            IEnumerable<string> permissionCodes,
            CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class EmptyKnowledgeBaseReadService : IKnowledgeBaseReadService
    {
        public static EmptyKnowledgeBaseReadService Instance { get; } = new();

        public Task<IReadOnlyList<KnowledgeBaseDescriptor>> ListAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<KnowledgeBaseDescriptor>>([]);

        public Task<IReadOnlyList<KnowledgeBaseDescriptor>> GetByNamesAsync(
            IReadOnlyCollection<string> names,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<KnowledgeBaseDescriptor>>([]);
    }

    private sealed class EmptyBusinessDatabaseReadService : IBusinessDatabaseReadService
    {
        public static EmptyBusinessDatabaseReadService Instance { get; } = new();

        public Task<IReadOnlyList<BusinessDatabaseDescriptor>> ListEnabledAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<BusinessDatabaseDescriptor>>([]);

        public Task<IReadOnlyList<BusinessDatabaseDescriptor>> ListSelectableAsync(
            DataSourceSelectionMode selectionMode,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<BusinessDatabaseDescriptor>>([]);

        public Task<BusinessDatabaseConnectionInfo?> GetByNameAsync(
            string name,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<BusinessDatabaseConnectionInfo?>(null);
    }
}
