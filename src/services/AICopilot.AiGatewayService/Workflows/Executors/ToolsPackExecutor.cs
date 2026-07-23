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

    internal static bool IsRelevant(
        IEnumerable<IntentResult> intentResults,
        AgentIntentRegistrySnapshot registry)
    {
        return intentResults.Any(intent => IsActionIntent(intent, registry));
    }

    public async Task<BranchResult> DiscoverAsync(
        List<IntentResult> intentResults,
        AgentIntentRegistrySnapshot registry,
        CancellationToken ct = default)
    {
        try
        {
            var actionIntents = intentResults
                .Where(intent => IsActionIntent(intent, registry))
                .ToList();

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

    private static bool IsActionIntent(
        IntentResult intent,
        AgentIntentRegistrySnapshot registry)
    {
        return intent.Confidence > 0.8 &&
               AgentIntentRegistryV1.IsIntentClass(
                   registry,
                   intent.Intent,
                   AgentIntentClass.PluginAction);
    }
}
