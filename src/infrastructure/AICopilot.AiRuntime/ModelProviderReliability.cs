using AICopilot.Services.Contracts;

namespace AICopilot.AiRuntime;

public sealed partial class ModelProviderReliabilityOptions
{
    public const string SectionName = "AiRuntime:ProviderReliability";

    public bool EnableFallback { get; set; }

    public Dictionary<string, string[]> FallbackProviders { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public int CircuitBreakerFailureThreshold { get; set; } = 3;

    public int CircuitBreakerOpenSeconds { get; set; } = 60;

    public int MaxOutputTokens { get; set; }

    public int PerUserRpmLimit { get; set; }

    public int PerUserTpmLimit { get; set; }

    public int PerUserConcurrencyLimit { get; set; }

    public int PerRoleRpmLimit { get; set; }

    public int PerRoleTpmLimit { get; set; }

    public int PerRoleConcurrencyLimit { get; set; }

    public Dictionary<string, ModelEndpointPoolOptions> EndpointPools { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class ModelEndpointPoolOptions
{
    public string Usage { get; set; } = "AnswerPool";

    public string Strategy { get; set; } = "LeastInFlight";

    public int QueueTimeoutMs { get; set; } = 10000;

    public int ModelConcurrencyLimit { get; set; }

    public int ModelRpmLimit { get; set; }

    public int ModelTpmLimit { get; set; }

    public List<ModelEndpointOptions> Endpoints { get; set; } = [];
}

public sealed class ModelEndpointOptions
{
    public string EndpointId { get; set; } = string.Empty;

    public string Provider { get; set; } = string.Empty;

    public string BaseUrl { get; set; } = string.Empty;

    public string? ApiKey { get; set; }

    public string? ApiKeyEnvironmentVariable { get; set; }

    public int ConcurrencyLimit { get; set; } = 2;

    public int QueueLimit { get; set; } = 20;

    public int TimeoutMs { get; set; } = 60000;

    public int RpmLimit { get; set; }

    public int TpmLimit { get; set; }

    public int Weight { get; set; } = 1;

    public int Priority { get; set; } = 100;

    public bool IsEnabled { get; set; } = true;
}

public sealed record ModelEndpointSelection(
    string PoolName,
    string EndpointId,
    string Provider,
    string BaseUrl,
    bool HasApiKey,
    string? ApiKey);

public interface IModelEndpointPoolScheduler : IModelPoolSnapshotReader
{
    ModelEndpointLease AcquireEndpoint(
        string poolName,
        AgentRuntimeCreateRequest request,
        ModelProviderExecutionContext context);

    ModelEndpointSelection SelectEndpoint(string poolName);

    void RecordStarted(string endpointId);

    void RecordSucceeded(string endpointId, TimeSpan duration);

    void RecordFailed(string endpointId, TimeSpan duration, Exception exception);

    void RecordRateLimited(string endpointId);

    void RecordStickyStreaming(string endpointId);
}

public sealed class ModelEndpointLease : IDisposable
{
    private readonly Action<ModelEndpointLease, TimeSpan, Exception?> release;
    private readonly DateTimeOffset startedAt;
    private int disposed;

    internal ModelEndpointLease(
        ModelEndpointSelection selection,
        string modelKey,
        int tokenEstimate,
        Func<DateTimeOffset> utcNow,
        Action<ModelEndpointLease, TimeSpan, Exception?> release)
    {
        Selection = selection;
        ModelKey = modelKey;
        TokenEstimate = tokenEstimate;
        this.release = release;
        startedAt = utcNow();
        UtcNow = utcNow;
    }

    public ModelEndpointSelection Selection { get; }

    internal string ModelKey { get; }

    internal int TokenEstimate { get; }

    private Func<DateTimeOffset> UtcNow { get; }

    public void Dispose()
    {
        Complete(null);
    }

    public void CompleteFailure(Exception exception)
    {
        Complete(exception);
    }

    private void Complete(Exception? exception)
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        release(this, UtcNow() - startedAt, exception);
    }
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

internal sealed class ModelEndpointPoolNotConfiguredException(string poolName)
    : InvalidOperationException($"Model endpoint pool '{poolName}' is not configured.");
