using AICopilot.AiGatewayService.Approvals;
using AICopilot.Services.Contracts;

namespace AICopilot.AiGatewayService.Agents;

public class DataAnalysisAgentBuilder(
    ChatAgentFactory agentFactory,
    ISqlDialectInstructionProvider sqlDialectInstructionProvider,
    ApprovalToolResolver approvalToolResolver)
{
    private const string TemplateName = "DataAnalysisAgent";

    /// <summary>
    /// 构建针对特定数据库优化的 DBA Agent
    /// </summary>
    public async Task<ScopedRuntimeAgent> BuildAsync(BusinessDatabaseConnectionInfo database)
    {
        var dialectInstructions = sqlDialectInstructionProvider.GetInstructions(database.Provider);
        var providerName = database.Provider.ToString();
        var tools = await approvalToolResolver.GetToolsForPluginsAsync([DataAnalysisPluginNames.DataAnalysisPlugin]);

        var agent = await agentFactory.CreateAgentAsync(TemplateName, systemPrompt =>
        {
            return systemPrompt
                .Replace("{{$DbProvider}}", providerName)
                .Replace("{{$DatabaseName}}", database.Name)
                .Replace("{{$DialectInstructions}}", dialectInstructions);
        }, options =>
        {
            options.Tools = tools;
        }, false);

        return agent;
    }
}
