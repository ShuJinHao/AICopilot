using AICopilot.Core.AiGateway.Aggregates.LanguageModel;

namespace AICopilot.Services.Contracts;

public sealed record LanguageModelConnectivityTestOutcome(
    bool Success,
    string? Error,
    long ElapsedMilliseconds,
    DateTimeOffset CheckedAt);

public interface ILanguageModelConnectivityTester
{
    Task<LanguageModelConnectivityTestOutcome> TestAsync(
        LanguageModel model,
        CancellationToken cancellationToken = default);
}
