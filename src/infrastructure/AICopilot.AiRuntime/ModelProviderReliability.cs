using System.Collections.Concurrent;
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

    public Dictionary<string, ModelEndpointPoolOptions> EndpointPools { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class ModelEndpointPoolOptions
{
    public string Usage { get; set; } = "AnswerPool";

    public string Strategy { get; set; } = "LeastInFlight";

    public List<ModelEndpointOptions> Endpoints { get; set; } = [];
}

public sealed class ModelEndpointOptions
{
    public string EndpointId { get; set; } = string.Empty;

    public string Provider { get; set; } = string.Empty;

    public string BaseUrl { get; set; } = string.Empty;

    public string? ApiKey { get; set; }

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
    ModelEndpointSelection SelectEndpoint(string poolName);

    void RecordStarted(string endpointId);

    void RecordSucceeded(string endpointId, TimeSpan duration);

    void RecordFailed(string endpointId, TimeSpan duration, Exception exception);

    void RecordRateLimited(string endpointId);

    void RecordFallback(string endpointId);

    void RecordStickyStreaming(string endpointId);
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
    private readonly ConcurrentDictionary<string, ProviderCircuitState> providerStates = new(StringComparer.OrdinalIgnoreCase);

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
        providerStates.TryRemove(providerName, out _);
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
        providerStates.AddOrUpdate(
            providerName,
            _ => CreateFailureState(1, threshold, openSeconds),
            (_, state) => CreateFailureState(state.FailureCount + 1, threshold, openSeconds));
    }

    private ProviderCircuitState CreateFailureState(int failureCount, int threshold, int openSeconds)
    {
        return failureCount >= threshold
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

internal sealed class InMemoryModelEndpointPoolScheduler(
    IOptions<ModelProviderReliabilityOptions> options)
    : IModelEndpointPoolScheduler
{
    private readonly ConcurrentDictionary<string, EndpointRuntimeStats> stats = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, long> roundRobinCounters = new(StringComparer.OrdinalIgnoreCase);

    public ModelEndpointSelection SelectEndpoint(string poolName)
    {
        if (!options.Value.EndpointPools.TryGetValue(poolName, out var pool))
        {
            throw new InvalidOperationException($"Model endpoint pool '{poolName}' is not configured.");
        }

        var candidates = pool.Endpoints
            .Where(endpoint => endpoint.IsEnabled)
            .Where(endpoint => !string.IsNullOrWhiteSpace(endpoint.EndpointId))
            .Where(endpoint => !IsCircuitOpen(endpoint.EndpointId))
            .Where(endpoint => GetStats(endpoint.EndpointId).InFlight < Math.Max(1, endpoint.ConcurrencyLimit))
            .ToArray();

        if (candidates.Length == 0)
        {
            throw new InvalidOperationException($"Model endpoint pool '{poolName}' has no healthy endpoint with available concurrency.");
        }

        var selected = string.Equals(pool.Strategy, "WeightedRoundRobin", StringComparison.OrdinalIgnoreCase)
            ? SelectWeightedRoundRobin(poolName, candidates)
            : SelectLeastInFlight(candidates);

        return new ModelEndpointSelection(
            poolName,
            selected.EndpointId,
            selected.Provider,
            selected.BaseUrl,
            !string.IsNullOrWhiteSpace(selected.ApiKey),
            selected.ApiKey);
    }

    public void RecordStarted(string endpointId)
    {
        GetStats(endpointId).RecordStarted();
    }

    public void RecordSucceeded(string endpointId, TimeSpan duration)
    {
        GetStats(endpointId).RecordSucceeded(duration);
    }

    public void RecordFailed(string endpointId, TimeSpan duration, Exception exception)
    {
        var endpointStats = GetStats(endpointId);
        endpointStats.RecordFailed(duration, exception);
        var currentOptions = options.Value;
        if (endpointStats.ConsecutiveFailures >= Math.Max(1, currentOptions.CircuitBreakerFailureThreshold))
        {
            endpointStats.OpenCircuit(TimeSpan.FromSeconds(Math.Max(1, currentOptions.CircuitBreakerOpenSeconds)));
        }
    }

    public void RecordRateLimited(string endpointId)
    {
        GetStats(endpointId).RecordRateLimited();
    }

    public void RecordFallback(string endpointId)
    {
        GetStats(endpointId).RecordFallback();
    }

    public void RecordStickyStreaming(string endpointId)
    {
        GetStats(endpointId).RecordStickyStreaming();
    }

    public ModelPoolSnapshotDto GetSnapshot()
    {
        var pools = options.Value.EndpointPools
            .OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase)
            .Select(item => new ModelPoolDto(
                item.Key,
                item.Value.Usage,
                item.Value.Strategy,
                item.Value.Endpoints
                    .Select(ToEndpointDto)
                    .ToArray()))
            .ToArray();

        return new ModelPoolSnapshotDto(pools);
    }

    private ModelEndpointDto ToEndpointDto(ModelEndpointOptions endpoint)
    {
        var endpointStats = GetStats(endpoint.EndpointId);
        return new ModelEndpointDto(
            endpoint.EndpointId,
            endpoint.Provider,
            endpoint.BaseUrl,
            Math.Max(1, endpoint.ConcurrencyLimit),
            Math.Max(0, endpoint.QueueLimit),
            Math.Max(1, endpoint.TimeoutMs),
            Math.Max(0, endpoint.RpmLimit),
            Math.Max(0, endpoint.TpmLimit),
            Math.Max(1, endpoint.Weight),
            endpoint.Priority,
            endpoint.IsEnabled && !IsCircuitOpen(endpoint.EndpointId),
            IsCircuitOpen(endpoint.EndpointId),
            !string.IsNullOrWhiteSpace(endpoint.ApiKey),
            endpointStats.ToDto());
    }

    private EndpointRuntimeStats GetStats(string endpointId)
    {
        return stats.GetOrAdd(endpointId, _ => new EndpointRuntimeStats(() => DateTimeOffset.UtcNow));
    }

    private bool IsCircuitOpen(string endpointId)
    {
        return GetStats(endpointId).IsCircuitOpen;
    }

    private ModelEndpointOptions SelectLeastInFlight(IReadOnlyCollection<ModelEndpointOptions> candidates)
    {
        return candidates
            .OrderBy(endpoint => GetStats(endpoint.EndpointId).InFlight)
            .ThenBy(endpoint => endpoint.Priority)
            .ThenByDescending(endpoint => endpoint.Weight)
            .ThenBy(endpoint => endpoint.EndpointId, StringComparer.OrdinalIgnoreCase)
            .First();
    }

    private ModelEndpointOptions SelectWeightedRoundRobin(
        string poolName,
        IReadOnlyList<ModelEndpointOptions> candidates)
    {
        var expanded = candidates
            .SelectMany(endpoint => Enumerable.Repeat(endpoint, Math.Max(1, endpoint.Weight)))
            .OrderBy(endpoint => endpoint.Priority)
            .ThenBy(endpoint => endpoint.EndpointId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var index = (int)(roundRobinCounters.AddOrUpdate(poolName, 1, (_, value) => value + 1) % expanded.Length);
        return expanded[index];
    }

    private sealed class EndpointRuntimeStats(Func<DateTimeOffset> utcNow)
    {
        private readonly object gate = new();
        private readonly List<double> durations = [];
        private DateTimeOffset? circuitOpenUntil;

        public int InFlight { get; private set; }

        public int ConsecutiveFailures { get; private set; }

        public long SuccessCount { get; private set; }

        public long FailureCount { get; private set; }

        public long RateLimitCount { get; private set; }

        public long CircuitBreakerOpenCount { get; private set; }

        public long FallbackCount { get; private set; }

        public int StickyStreamingCount { get; private set; }

        public string? LastFailureReason { get; private set; }

        public bool IsCircuitOpen
        {
            get
            {
                lock (gate)
                {
                    return circuitOpenUntil.HasValue && utcNow() < circuitOpenUntil.Value;
                }
            }
        }

        public void RecordStarted()
        {
            lock (gate)
            {
                InFlight++;
            }
        }

        public void RecordSucceeded(TimeSpan duration)
        {
            lock (gate)
            {
                InFlight = Math.Max(0, InFlight - 1);
                SuccessCount++;
                ConsecutiveFailures = 0;
                circuitOpenUntil = null;
                AddDuration(duration);
            }
        }

        public void RecordFailed(TimeSpan duration, Exception exception)
        {
            lock (gate)
            {
                InFlight = Math.Max(0, InFlight - 1);
                FailureCount++;
                ConsecutiveFailures++;
                LastFailureReason = exception.GetType().Name;
                AddDuration(duration);
            }
        }

        public void OpenCircuit(TimeSpan duration)
        {
            lock (gate)
            {
                circuitOpenUntil = utcNow().Add(duration);
                CircuitBreakerOpenCount++;
            }
        }

        public void RecordRateLimited()
        {
            lock (gate)
            {
                RateLimitCount++;
            }
        }

        public void RecordFallback()
        {
            lock (gate)
            {
                FallbackCount++;
            }
        }

        public void RecordStickyStreaming()
        {
            lock (gate)
            {
                StickyStreamingCount++;
            }
        }

        public ModelEndpointStatsDto ToDto()
        {
            lock (gate)
            {
                var snapshot = durations.OrderBy(item => item).ToArray();
                var average = snapshot.Length == 0 ? 0 : snapshot.Average();
                var p95 = snapshot.Length == 0
                    ? 0
                    : snapshot[(int)Math.Floor((snapshot.Length - 1) * 0.95)];

                return new ModelEndpointStatsDto(
                    InFlight,
                    QueueLength: 0,
                    SuccessCount,
                    FailureCount,
                    average,
                    p95,
                    RateLimitCount,
                    CircuitBreakerOpenCount,
                    FallbackCount,
                    StickyStreamingCount,
                    IsCircuitOpen ? "Open" : "Closed",
                    LastFailureReason);
            }
        }

        private void AddDuration(TimeSpan duration)
        {
            durations.Add(Math.Max(0, duration.TotalMilliseconds));
            if (durations.Count > 512)
            {
                durations.RemoveAt(0);
            }
        }
    }
}

internal static class AiFallbackScope
{
    public const string GeneralChat = "GeneralChat";
    public const string RagSummary = "RagSummary";
    public const string DataAnalysisFinalSummary = "DataAnalysisFinalSummary";
    public const string McpToolCall = "McpToolCall";
    public const string ApprovalResume = "ApprovalResume";
    public const string SideEffectingTool = "SideEffectingTool";
    public const string DataAnalysisSqlToolChain = "DataAnalysisSqlToolChain";
}
