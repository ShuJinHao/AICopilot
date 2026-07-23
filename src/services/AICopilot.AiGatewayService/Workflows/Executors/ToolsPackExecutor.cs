using AICopilot.AiGatewayService.Approvals;
using AICopilot.AiGatewayService.AgentTasks;
using AICopilot.AiGatewayService.Models;
using AICopilot.SharedKernel.Result;
using Microsoft.Extensions.Logging;

namespace AICopilot.AiGatewayService.Workflows.Executors;

public class ToolsPackExecutor(
    ApprovalToolResolver approvalToolResolver,
    ILogger<ToolsPackExecutor> logger)
{
    public const string ExecutorId = nameof(ToolsPackExecutor);
    private const string ActionIntentPrefix = "Action.";

    internal static bool IsRelevant(IEnumerable<IntentResult> intents, AgentIntentRegistrySnapshot registry) =>
        AgentWorkflowIntentSelector.Any(intents, registry, 0.8, null, AgentIntentClass.PluginAction);

    public async Task<BranchResult> DiscoverAsync(
        List<IntentResult> intentResults,
        AgentIntentRegistrySnapshot registry,
        CancellationToken ct = default)
    {
        try
        {
            var actionIntents = AgentWorkflowIntentSelector.Select(
                intentResults, registry, 0.8, null, AgentIntentClass.PluginAction);

            if (actionIntents.Count == 0)
            {
                return BranchResult.Skipped(BranchType.Tools);
            }

            logger.LogInformation("Matched tool intents: {Intents}", string.Join(", ", actionIntents.Select(i => i.Intent)));

            var pluginNames = actionIntents
                .Select(i => i.Intent.Substring(ActionIntentPrefix.Length))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            var tools = await approvalToolResolver.GetToolsForPluginsAsync(pluginNames, ct);

            logger.LogInformation("Loaded {Count} tool functions.", tools.Length);

            return BranchResult.FromTools(tools);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception e)
        {
            logger.LogError(
                "Failed to load tool pack. ErrorType={ErrorType}; OriginalMessage=hidden_by_security_policy",
                e.GetType().Name);
            return BranchResult.Failed(
                BranchType.Tools,
                AppProblemCodes.ChatStreamFailed,
                "Tool capability discovery failed.");
        }
    }

}
