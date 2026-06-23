using System.Text;
using AICopilot.AgentPlugin;
using AICopilot.AiGatewayService.BusinessSemantics;
using AICopilot.AiGatewayService.RoutingModels;
using AICopilot.Core.AiGateway.Aggregates.Skills;
using AICopilot.Core.AiGateway.Specifications.Skills;
using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Ai;
using AICopilot.SharedKernel.Repository;

namespace AICopilot.AiGatewayService.Agents;

public class IntentRoutingAgentBuilder
{
    private const string AgentName = "IntentRoutingAgent";

    private readonly ChatAgentFactory _agentFactory;
    private readonly IKnowledgeBaseReadService _knowledgeBaseReadService;
    private readonly IBusinessDatabaseReadService _businessDatabaseReadService;
    private readonly IntentRoutingPromptComposer _promptComposer;
    private readonly IAgentPluginCatalog _pluginCatalog;
    private readonly IRoutingModelResolver _routingModelResolver;
    private readonly IChatExecutionMetadataAccessor _executionMetadataAccessor;
    private readonly IReadRepository<SkillDefinition> _skillRepository;

    public IntentRoutingAgentBuilder(
        ChatAgentFactory agentFactory,
        IAgentPluginCatalog pluginCatalog,
        IKnowledgeBaseReadService knowledgeBaseReadService,
        IBusinessDatabaseReadService businessDatabaseReadService,
        IntentRoutingPromptComposer promptComposer,
        IRoutingModelResolver routingModelResolver,
        IChatExecutionMetadataAccessor executionMetadataAccessor,
        IReadRepository<SkillDefinition> skillRepository)
    {
        _agentFactory = agentFactory;
        _pluginCatalog = pluginCatalog;
        _knowledgeBaseReadService = knowledgeBaseReadService;
        _businessDatabaseReadService = businessDatabaseReadService;
        _promptComposer = promptComposer;
        _routingModelResolver = routingModelResolver;
        _executionMetadataAccessor = executionMetadataAccessor;
        _skillRepository = skillRepository;
    }

    private async Task<string> GetSkillIntentListAsync()
    {
        var skills = await _skillRepository.ListAsync(new EnabledSkillDefinitionsSpec());
        if (skills.Count == 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        builder.AppendLine("Available Skills:");
        foreach (var skill in skills)
        {
            var outputs = skill.OutputComponentTypes.Length == 0
                ? "text"
                : string.Join(",", skill.OutputComponentTypes);
            var dataModes = skill.AllowedDataSourceModes.Length == 0
                ? "none"
                : string.Join(",", skill.AllowedDataSourceModes);
            var knowledgeScopes = skill.AllowedKnowledgeScopes.Length == 0
                ? "none"
                : string.Join(",", skill.AllowedKnowledgeScopes);
            builder.AppendLine(
                $"- Skill.{skill.SkillCode}: {skill.DisplayName}. {skill.Description} risk={skill.RiskLevel}; approval={skill.ApprovalPolicy}; data={dataModes}; knowledge={knowledgeScopes}; outputs={outputs}.");
        }

        builder.AppendLine("  Routing rule: choose only one enabled Skill that best fits the user goal. If no Skill fits, choose General.Chat.");
        builder.AppendLine("  Routing rule: Skill selection narrows allowed tools; it never expands ToolRegistry or Cloud readonly safety policy.");
        return builder.ToString();
    }

    private string GetToolIntentList()
    {
        var builder = new StringBuilder();
        builder.AppendLine("- General.Chat: 闲聊、打招呼、知识解释、诊断建议类自由问答，或无法归类的问题。");

        foreach (var plugin in _pluginCatalog.GetAllPlugin().Where(plugin => plugin.ChatExposureMode.CanExposeInChat()))
        {
            builder.AppendLine($"- Action.{plugin.Name}: {plugin.Description}");
        }

        builder.AppendLine("  Routing rule: restart, reboot, shutdown, write parameter, recipe download, PLC write, state change, or any control request must not be routed to Action intents.");
        builder.AppendLine("  Routing rule: Cloud business mutations such as modifying recipes, disabling devices, backfilling capacity, deleting logs, uploading production data, approving, dispatching, or submitting must not be routed to Action intents.");
        builder.AppendLine("  Routing rule: if the user requests a control or Cloud write action, fall back to General.Chat and explain that the assistant only supports observation, diagnosis, suggestion, and knowledge answers.");

        return builder.ToString();
    }

    private async Task<string> GetKnowledgeIntentListAsync()
    {
        var builder = new StringBuilder();
        var knowledgeBases = await _knowledgeBaseReadService.ListAsync();

        foreach (var knowledgeBase in knowledgeBases.OrderBy(knowledgeBase => knowledgeBase.Name))
        {
            builder.AppendLine($"- Knowledge.{knowledgeBase.Name}: {knowledgeBase.Description}");
        }

        return builder.ToString();
    }

    private string GetBusinessPolicyIntentList()
    {
        return _promptComposer.BuildBusinessPolicyIntentSection();
    }

    private async Task<string> GetDataAnalysisIntentListAsync()
    {
        var builder = new StringBuilder();
        builder.Append(_promptComposer.BuildStructuredIntentSection());

        var businessDatabases = await _businessDatabaseReadService.ListEnabledAsync();

        foreach (var businessDatabase in businessDatabases)
        {
            builder.AppendLine($"- Analysis.{businessDatabase.Name}: {businessDatabase.Description}");
        }

        return builder.ToString();
    }

    public async Task<ScopedRuntimeAgent> BuildAsync()
    {
        var intents = new StringBuilder();
        intents.Append(await GetSkillIntentListAsync());
        intents.Append(GetToolIntentList());
        intents.Append(await GetKnowledgeIntentListAsync());
        intents.Append(GetBusinessPolicyIntentList());
        intents.Append(await GetDataAnalysisIntentListAsync());

        string ComposeInstructions(string systemPrompt)
        {
            return systemPrompt.Replace("{{$IntentList}}", intents.ToString());
        }

        var activeRoutingModel = await _routingModelResolver.ResolveActiveModelAsync();
        var agent = activeRoutingModel is null
            ? await _agentFactory.CreateAgentAsync(AgentName, ComposeInstructions)
            : await _agentFactory.CreateAgentAsync(AgentName, activeRoutingModel, ComposeInstructions);

        if (activeRoutingModel is not null)
        {
            _executionMetadataAccessor.SetRoutingModel(activeRoutingModel);
        }

        return agent;
    }
}
