using AICopilot.Core.AiGateway.Aggregates.LanguageModel;

namespace AICopilot.AiGatewayService.Commands.LanguageModels;

internal static class LanguageModelCommandMapper
{
    public static int ResolveContextWindowTokens(int? contextWindowTokens, int? maxTokens)
    {
        return contextWindowTokens ?? maxTokens ?? 2048;
    }

    public static LanguageModelUsage ParseUsages(IReadOnlyList<string>? usages)
    {
        if (usages is null || usages.Count == 0)
        {
            return LanguageModelUsage.Chat;
        }

        return LanguageModelUsage.Chat;
    }
}
