using AICopilot.AiGatewayService.Approvals;
using AICopilot.AiGatewayService.Models;
using Microsoft.Extensions.Logging;

namespace AICopilot.AiGatewayService.Workflows.Executors;

public class ToolsPackExecutor(
    ApprovalToolResolver approvalToolResolver,
    ILogger<ToolsPackExecutor> logger)
{
    public const string ExecutorId = nameof(ToolsPackExecutor);
    private const string ActionIntentPrefix = "Action.";

    public async Task<BranchResult> ExecuteAsync(
        List<IntentResult> intentResults,
        CancellationToken ct = default)
    {
        try
        {
            var actionIntents = intentResults
                .Where(i => i.Intent.StartsWith(ActionIntentPrefix, StringComparison.OrdinalIgnoreCase)
                            && i.Confidence > 0.8)
                .ToList();

            if (actionIntents.Count == 0)
            {
                return BranchResult.FromTools([]);
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
        catch (Exception e)
        {
            logger.LogError(e, "Failed to load tool pack.");
            return BranchResult.FromTools([]);
        }
    }
}
