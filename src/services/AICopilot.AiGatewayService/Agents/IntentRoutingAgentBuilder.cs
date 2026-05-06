using System.Text;
using AICopilot.AgentPlugin;
using AICopilot.AiGatewayService.BusinessSemantics;
using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Ai;

namespace AICopilot.AiGatewayService.Agents;

public class IntentRoutingAgentBuilder
{
    private const string AgentName = "IntentRoutingAgent";

    private readonly ChatAgentFactory _agentFactory;
    private readonly IKnowledgeBaseReadService _knowledgeBaseReadService;
    private readonly IBusinessDatabaseReadService _businessDatabaseReadService;
    private readonly IntentRoutingPromptComposer _promptComposer;
    private readonly IAgentPluginCatalog _pluginCatalog;

    public IntentRoutingAgentBuilder(
        ChatAgentFactory agentFactory,
        IAgentPluginCatalog pluginCatalog,
        IKnowledgeBaseReadService knowledgeBaseReadService,
        IBusinessDatabaseReadService businessDatabaseReadService,
        IntentRoutingPromptComposer promptComposer)
    {
        _agentFactory = agentFactory;
        _pluginCatalog = pluginCatalog;
        _knowledgeBaseReadService = knowledgeBaseReadService;
        _businessDatabaseReadService = businessDatabaseReadService;
        _promptComposer = promptComposer;
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
        builder.AppendLine("  Routing rule: if the user requests a control action, fall back to General.Chat and explain that the assistant only supports observation, diagnosis, suggestion, and knowledge answers.");

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
        intents.Append(GetToolIntentList());
        intents.Append(await GetKnowledgeIntentListAsync());
        intents.Append(GetBusinessPolicyIntentList());
        intents.Append(await GetDataAnalysisIntentListAsync());

        var agent = await _agentFactory.CreateAgentAsync(
            AgentName,
            systemPrompt =>
            {
                return systemPrompt
                    .Replace("{{$IntentList}}", intents.ToString());
            });

        return agent;
    }
}
