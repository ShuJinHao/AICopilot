using AICopilot.AiGatewayService.Approvals;
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

    public static bool IsRelevant(IEnumerable<IntentResult> intentResults)
    {
        return intentResults.Any(IsActionIntent);
    }

    public async Task<BranchResult> DiscoverAsync(
        List<IntentResult> intentResults,
        CancellationToken ct = default)
    {
        try
        {
            var actionIntents = intentResults
                .Where(IsActionIntent)
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

    private static bool IsActionIntent(IntentResult intent)
    {
        return intent.Intent.StartsWith(ActionIntentPrefix, StringComparison.OrdinalIgnoreCase)
               && intent.Confidence > 0.8;
    }
}
