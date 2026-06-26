using AICopilot.Services.Contracts;
using Microsoft.Extensions.Options;

namespace AICopilot.AiRuntime;

internal sealed class ModelProviderReliabilitySnapshotReader(
    IOptions<ModelProviderReliabilityOptions> options)
    : IModelProviderReliabilitySnapshotReader
{
    public ModelProviderReliabilityDto GetSnapshot()
    {
        var currentOptions = options.Value;
        var fallbackProviders = currentOptions.FallbackProviders
            .OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase)
            .Select(item => new ModelProviderFallbackRouteDto(
                item.Key,
                item.Value
                    .Where(provider => !string.IsNullOrWhiteSpace(provider))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray()))
            .ToArray();

        return new ModelProviderReliabilityDto(
            currentOptions.EnableFallback,
            fallbackProviders,
            Math.Max(1, currentOptions.CircuitBreakerFailureThreshold),
            Math.Max(1, currentOptions.CircuitBreakerOpenSeconds),
            Math.Max(0, currentOptions.MaxOutputTokens),
            [
                AiFallbackScope.GeneralChat,
                AiFallbackScope.RagSummary,
                AiFallbackScope.DataAnalysisFinalSummary
            ],
            [
                AiFallbackScope.McpToolCall,
                AiFallbackScope.ApprovalResume,
                AiFallbackScope.SideEffectingTool,
                AiFallbackScope.DataAnalysisSqlToolChain
            ]);
    }
}
