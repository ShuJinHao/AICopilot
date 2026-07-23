using AICopilot.AgentPlugin;
using AICopilot.AiGatewayService.AgentTasks;
using AICopilot.AiGatewayService.BusinessSemantics;
using AICopilot.AiGatewayService.RoutingModels;
using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Ai;

namespace AICopilot.AiGatewayService.Agents;

public interface IAgentRoutingConfigurationSnapshotReader
{
    Task<RuntimeAgentConfigurationSnapshot> ReadCurrentAsync(
        CancellationToken cancellationToken = default);
}

public class IntentRoutingAgentBuilder : IAgentRoutingConfigurationSnapshotReader
{
    private const string AgentName = "IntentRoutingAgent";
    internal const int RoutingMaxOutputTokens = 512;
    internal const float RoutingTemperature = 0;

    private readonly ConfiguredAgentRuntimeFactory _agentFactory;
    private readonly IKnowledgeBaseReadService _knowledgeBaseReadService;
    private readonly IBusinessDatabaseReadService _businessDatabaseReadService;
    private readonly IBusinessSemanticsCatalog _businessSemanticsCatalog;
    private readonly IAgentPluginCatalog _pluginCatalog;
    private readonly IRoutingModelResolver _routingModelResolver;
    private readonly IAgentExecutionMetadataAccessor _executionMetadataAccessor;

    public IntentRoutingAgentBuilder(
        ConfiguredAgentRuntimeFactory agentFactory,
        IAgentPluginCatalog pluginCatalog,
        IKnowledgeBaseReadService knowledgeBaseReadService,
        IBusinessDatabaseReadService businessDatabaseReadService,
        IBusinessSemanticsCatalog businessSemanticsCatalog,
        IRoutingModelResolver routingModelResolver,
        IAgentExecutionMetadataAccessor executionMetadataAccessor)
    {
        _agentFactory = agentFactory;
        _pluginCatalog = pluginCatalog;
        _knowledgeBaseReadService = knowledgeBaseReadService;
        _businessDatabaseReadService = businessDatabaseReadService;
        _businessSemanticsCatalog = businessSemanticsCatalog;
        _routingModelResolver = routingModelResolver;
        _executionMetadataAccessor = executionMetadataAccessor;
    }

    public async Task<ScopedRuntimeAgent> BuildAsync()
    {
        var registry = await ReadRegistrySnapshotAsync();
        return await BuildAsync(registry);
    }

    internal async Task<ScopedRuntimeAgent> BuildAsync(AgentIntentRegistrySnapshot registry)
    {
        string ComposeInstructions(string systemPrompt) =>
            ComposeRoutingInstructions(systemPrompt, registry.PromptInventory);
        var activeRoutingModel = await _routingModelResolver.ResolveActiveModelAsync();
        var agent = activeRoutingModel is null
            ? await _agentFactory.CreateAgentAsync(AgentName, ComposeInstructions, ConfigureRoutingOptions)
            : await _agentFactory.CreateAgentAsync(AgentName, activeRoutingModel, ComposeInstructions, ConfigureRoutingOptions);

        if (agent.ConfigurationSnapshot is not { } snapshot)
        {
            await agent.DisposeAsync();
            throw new InvalidOperationException("Intent routing runtime did not expose its authoritative configuration snapshot.");
        }

        _executionMetadataAccessor.SetRoutingConfiguration(snapshot);
        return agent;
    }

    public async Task<RuntimeAgentConfigurationSnapshot> ReadCurrentAsync(
        CancellationToken cancellationToken = default)
    {
        var registry = await ReadRegistrySnapshotAsync(cancellationToken);
        string ComposeInstructions(string systemPrompt) =>
            ComposeRoutingInstructions(systemPrompt, registry.PromptInventory);
        var activeRoutingModel = await _routingModelResolver.ResolveActiveModelAsync();
        return await _agentFactory.ReadConfigurationSnapshotAsync(
            AgentName,
            activeRoutingModel,
            ComposeInstructions,
            ConfigureRoutingOptions,
            cancellationToken);
    }

    internal async Task<AgentIntentRegistrySnapshot> ReadRegistrySnapshotAsync(
        CancellationToken cancellationToken = default)
    {
        var definitions = new List<AgentIntentRegistryPromptDefinition>
        {
            new(
                "General.Chat",
                "闲聊、打招呼、知识解释、诊断建议类自由问答，或无法归类的问题。")
        };

        var exposedPlugins = _pluginCatalog.GetAllPlugin()
            .Where(plugin => plugin.ChatExposureMode.CanExposeInChat())
            .ToArray();
        EnsureDynamicCodesDoNotShadowFrozenRegistry(
            exposedPlugins.Select(plugin => $"Action.{plugin.Name}"));
        definitions.AddRange(exposedPlugins
            .Select(plugin => new AgentIntentRegistryPromptDefinition(
                $"Action.{plugin.Name}",
                NormalizeDescription(plugin.Description, "Registered read-only plugin capability."),
                AllowedToolCodes: (plugin.GetTools() ?? [])
                    .Select(tool => tool.Name)
                    .Where(name => !string.IsNullOrWhiteSpace(name))
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(name => name, StringComparer.Ordinal)
                    .ToArray())));

        var knowledgeBases = await _knowledgeBaseReadService.ListAsync(cancellationToken);
        EnsureDynamicCodesDoNotShadowFrozenRegistry(
            knowledgeBases.Select(knowledgeBase => $"Knowledge.{knowledgeBase.Name}"));
        definitions.AddRange(knowledgeBases.Select(knowledgeBase =>
            new AgentIntentRegistryPromptDefinition(
                $"Knowledge.{knowledgeBase.Name}",
                NormalizeDescription(knowledgeBase.Description, "Registered knowledge-base retrieval capability."))));

        var policyIntents = _businessSemanticsCatalog.GetPolicyIntents();
        EnsureFrozenCodesMatchClass(policyIntents.Select(descriptor => descriptor.Policy.Intent), AgentIntentClass.Policy);
        definitions.AddRange(policyIntents.Select(descriptor =>
            new AgentIntentRegistryPromptDefinition(
                descriptor.Policy.Intent,
                NormalizeDescription(descriptor.Policy.Description, "Registered business-policy capability."),
                descriptor.Policy.ExampleQuestions.FirstOrDefault())));

        var structuredIntents = _businessSemanticsCatalog.GetStructuredIntents();
        EnsureFrozenStructuredCodes(structuredIntents.Select(descriptor => descriptor.Intent.Intent));
        definitions.AddRange(structuredIntents.Select(descriptor =>
            new AgentIntentRegistryPromptDefinition(
                descriptor.Intent.Intent,
                NormalizeDescription(descriptor.Intent.Description, "Registered typed read-only Cloud capability."),
                descriptor.ExampleQuestions.FirstOrDefault(),
                descriptor.QueryJsonExample)));

        var businessDatabases = await _businessDatabaseReadService.ListEnabledAsync(cancellationToken);
        var selectableDatabases = businessDatabases
            .Where(database => database.IsEnabled && database.IsReadOnly && database.IsSelectableInChat)
            .ToArray();
        EnsureDynamicCodesDoNotShadowFrozenRegistry(
            selectableDatabases.Select(database => $"Analysis.{database.Name}"));
        definitions.AddRange(selectableDatabases
            .Select(database => new AgentIntentRegistryPromptDefinition(
                $"Analysis.{database.Name}",
                NormalizeDescription(database.Description, "Registered governed read-only data source."))));

        var guidance = new List<string>
        {
            "restart, reboot, shutdown, write parameter, recipe download, PLC write, state change, or any control request must not be routed to Action intents.",
            "Cloud business mutations such as modifying recipes, disabling devices, backfilling capacity, deleting logs, uploading production data, approving, dispatching, or submitting must not be routed to Action intents.",
            "if the user requests a control or Cloud write action, fall back to General.Chat and explain that the assistant only supports observation, diagnosis, suggestion, and knowledge answers."
        };
        AppendGuidance(guidance, _businessSemanticsCatalog.PolicyRoutingGuidance);
        AppendGuidance(guidance, _businessSemanticsCatalog.StructuredRoutingGuidance);
        return AgentIntentRegistryV1.CreateRoutingSnapshot(definitions, guidance);
    }

    private static string ComposeRoutingInstructions(string systemPrompt, string intentInventory)
    {
        return systemPrompt.Replace("{{$IntentList}}", intentInventory, StringComparison.Ordinal);
    }

    private static void ConfigureRoutingOptions(AiChatOptions options)
    {
        options.MaxOutputTokens = RoutingMaxOutputTokens;
        options.Temperature = RoutingTemperature;
        options.Tools = [];
    }

    private static string NormalizeDescription(string? value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

    private static void AppendGuidance(List<string> target, RoutingGuidance guidance)
    {
        target.AddRange(guidance.Rules);
        target.AddRange(guidance.PriorityRules.Select(rule => $"Priority: {rule}"));
        target.AddRange(guidance.Notes);
    }

    private static void EnsureDynamicCodesDoNotShadowFrozenRegistry(IEnumerable<string> intentCodes)
    {
        var collision = intentCodes.FirstOrDefault(code =>
            AgentIntentRegistryV1.TryGetDescriptor(code, out _));
        if (collision is not null)
        {
            throw new InvalidOperationException(
                $"Dynamic intent '{collision}' shadows a frozen IntentRegistry definition.");
        }
    }

    private static void EnsureFrozenCodesMatchClass(
        IEnumerable<string> intentCodes,
        AgentIntentClass expectedClass)
    {
        var invalid = intentCodes.FirstOrDefault(code =>
            !AgentIntentRegistryV1.TryGetDescriptor(code, out var descriptor) ||
            descriptor.IntentClass != expectedClass);
        if (invalid is not null)
        {
            throw new InvalidOperationException(
                $"Intent '{invalid}' is missing its versioned IntentRegistry mapping.");
        }
    }

    private static void EnsureFrozenStructuredCodes(IEnumerable<string> intentCodes)
    {
        var invalid = intentCodes.FirstOrDefault(code =>
            !AgentIntentRegistryV1.TryGetDescriptor(code, out var descriptor) ||
            descriptor.IntentClass is not (AgentIntentClass.CloudOnly or AgentIntentClass.KnownButUnavailable));
        if (invalid is not null)
        {
            throw new InvalidOperationException(
                $"Structured intent '{invalid}' is missing its versioned read-only IntentRegistry mapping.");
        }
    }
}
