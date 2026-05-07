using AICopilot.Services.Contracts;

namespace AICopilot.AiRuntime;

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

internal sealed class DefaultModelFallbackPolicy : IModelFallbackPolicy
{
    public IReadOnlyList<string> GetFallbackProviders(
        AgentRuntimeCreateRequest request,
        ModelProviderExecutionContext context)
    {
        return context.IsHighRiskToolChain ? [] : [];
    }
}

internal sealed class InMemoryModelCircuitBreaker : IModelCircuitBreaker
{
    private readonly HashSet<string> openProviders = new(StringComparer.OrdinalIgnoreCase);

    public bool CanAttempt(string providerName)
    {
        return !openProviders.Contains(providerName);
    }

    public void RecordSuccess(string providerName)
    {
        openProviders.Remove(providerName);
    }

    public void RecordFailure(string providerName, Exception exception)
    {
        if (string.IsNullOrWhiteSpace(providerName))
        {
            return;
        }

        openProviders.Add(providerName);
    }
}

internal sealed class NoOpModelCostBudgetPolicy : IModelCostBudgetPolicy
{
    public void EnsureWithinBudget(AgentRuntimeCreateRequest request, ModelProviderExecutionContext context)
    {
    }
}
