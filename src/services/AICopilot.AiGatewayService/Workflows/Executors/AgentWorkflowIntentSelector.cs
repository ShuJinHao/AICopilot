using AICopilot.AiGatewayService.AgentTasks;
using AICopilot.AiGatewayService.Models;

namespace AICopilot.AiGatewayService.Workflows.Executors;

internal static class AgentWorkflowIntentSelector
{
    public static bool Any(
        IEnumerable<IntentResult> intents,
        AgentIntentRegistrySnapshot registry,
        double minimumConfidence,
        string? excludedIntent,
        params AgentIntentClass[] intentClasses)
    {
        return intents.Any(intent => Matches(
            intent, registry, minimumConfidence, excludedIntent, intentClasses));
    }

    public static List<IntentResult> Select(
        IEnumerable<IntentResult> intents,
        AgentIntentRegistrySnapshot registry,
        double minimumConfidence,
        string? excludedIntent,
        params AgentIntentClass[] intentClasses)
    {
        return intents
            .Where(intent => Matches(
                intent, registry, minimumConfidence, excludedIntent, intentClasses))
            .ToList();
    }

    private static bool Matches(
        IntentResult intent,
        AgentIntentRegistrySnapshot registry,
        double minimumConfidence,
        string? excludedIntent,
        IReadOnlyCollection<AgentIntentClass> intentClasses)
    {
        return intent.Confidence > minimumConfidence &&
               registry.TryGet(intent.Intent, out var descriptor) &&
               intentClasses.Contains(descriptor.IntentClass) &&
               !string.Equals(intent.Intent, excludedIntent, StringComparison.Ordinal);
    }
}
