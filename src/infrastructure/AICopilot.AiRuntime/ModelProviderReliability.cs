using AICopilot.Services.Contracts;
using Microsoft.Extensions.Options;

namespace AICopilot.AiRuntime;

public sealed class ModelProviderReliabilityOptions
{
    public const string SectionName = "AiRuntime:ProviderReliability";

    public bool EnableFallback { get; set; }

    public Dictionary<string, string[]> FallbackProviders { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public int CircuitBreakerFailureThreshold { get; set; } = 3;

    public int CircuitBreakerOpenSeconds { get; set; } = 60;

    public int MaxOutputTokens { get; set; }
}

public sealed record ModelProviderExecutionContext(
    string RequestedProvider,
    bool HasTools,
    bool HasMcpTools,
    bool HasApprovalTools,
    bool HasSideEffectingTools,
    bool HasDataAnalysisSqlToolChain)
{
    public bool IsHighRiskToolChain =>
        HasMcpTools || HasApprovalTools || HasSideEffectingTools || HasDataAnalysisSqlToolChain;
}

public interface IModelProviderHealth
{
    bool IsHealthy(string providerName);
}

public interface IModelFallbackPolicy
{
    IReadOnlyList<string> GetFallbackProviders(
        AgentRuntimeCreateRequest request,
        ModelProviderExecutionContext context);
}

public interface IModelCircuitBreaker
{
    bool CanAttempt(string providerName);

    void RecordSuccess(string providerName);

    void RecordFailure(string providerName, Exception exception);
}

public interface IModelCostBudgetPolicy
{
    void EnsureWithinBudget(AgentRuntimeCreateRequest request, ModelProviderExecutionContext context);
}

internal sealed class AlwaysHealthyModelProviderHealth : IModelProviderHealth
{
    public bool IsHealthy(string providerName)
    {
        return !string.IsNullOrWhiteSpace(providerName);
    }
}

internal sealed class DefaultModelFallbackPolicy(IOptions<ModelProviderReliabilityOptions> options) : IModelFallbackPolicy
{
    public IReadOnlyList<string> GetFallbackProviders(
        AgentRuntimeCreateRequest request,
        ModelProviderExecutionContext context)
    {
        var currentOptions = options.Value;
        if (!currentOptions.EnableFallback || context.IsHighRiskToolChain)
        {
            return [];
        }

        if (!TryGetFallbackProviders(currentOptions, context.RequestedProvider, out var fallbackProviders))
        {
            return [];
        }

        return fallbackProviders
            .Where(provider => !string.IsNullOrWhiteSpace(provider))
            .Where(provider => !string.Equals(provider, context.RequestedProvider, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static bool TryGetFallbackProviders(
        ModelProviderReliabilityOptions options,
        string requestedProvider,
        out string[] fallbackProviders)
    {
        if (options.FallbackProviders.TryGetValue(requestedProvider, out fallbackProviders!))
        {
            return true;
        }

        return options.FallbackProviders.TryGetValue("*", out fallbackProviders!);
    }
}

internal sealed class InMemoryModelCircuitBreaker : IModelCircuitBreaker
{
    private readonly IOptions<ModelProviderReliabilityOptions> options;
    private readonly Func<DateTimeOffset> utcNow;
    private readonly Dictionary<string, ProviderCircuitState> providerStates = new(StringComparer.OrdinalIgnoreCase);

    public InMemoryModelCircuitBreaker(IOptions<ModelProviderReliabilityOptions> options)
        : this(options, () => DateTimeOffset.UtcNow)
    {
    }

    internal InMemoryModelCircuitBreaker(
        IOptions<ModelProviderReliabilityOptions> options,
        Func<DateTimeOffset> utcNow)
    {
        this.options = options;
        this.utcNow = utcNow;
    }

    public bool CanAttempt(string providerName)
    {
        if (!providerStates.TryGetValue(providerName, out var state) || state.OpenedUntil is null)
        {
            return true;
        }

        return utcNow() >= state.OpenedUntil.Value;
    }

    public void RecordSuccess(string providerName)
    {
        providerStates.Remove(providerName);
    }

    public void RecordFailure(string providerName, Exception exception)
    {
        if (string.IsNullOrWhiteSpace(providerName))
        {
            return;
        }

        var currentOptions = options.Value;
        var threshold = Math.Max(1, currentOptions.CircuitBreakerFailureThreshold);
        var openSeconds = Math.Max(1, currentOptions.CircuitBreakerOpenSeconds);
        var previousFailures = providerStates.TryGetValue(providerName, out var state) ? state.FailureCount : 0;
        var failureCount = previousFailures + 1;

        providerStates[providerName] = failureCount >= threshold
            ? new ProviderCircuitState(failureCount, utcNow().AddSeconds(openSeconds))
            : new ProviderCircuitState(failureCount, null);
    }

    private sealed record ProviderCircuitState(int FailureCount, DateTimeOffset? OpenedUntil);
}

internal sealed class ConfiguredModelCostBudgetPolicy(IOptions<ModelProviderReliabilityOptions> options) : IModelCostBudgetPolicy
{
    public void EnsureWithinBudget(AgentRuntimeCreateRequest request, ModelProviderExecutionContext context)
    {
        var maxOutputTokens = options.Value.MaxOutputTokens;
        if (maxOutputTokens <= 0 || request.Options.MaxOutputTokens is null)
        {
            return;
        }

        if (request.Options.MaxOutputTokens.Value > maxOutputTokens)
        {
            throw new InvalidOperationException(
                $"Requested MaxOutputTokens {request.Options.MaxOutputTokens.Value} exceeds configured provider budget {maxOutputTokens}.");
        }
    }
}
