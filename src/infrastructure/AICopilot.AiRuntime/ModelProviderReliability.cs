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

    public int PerUserRpmLimit { get; set; }

    public int PerRoleRpmLimit { get; set; }

    public int PerTenantRpmLimit { get; set; }

    public Dictionary<string, ModelEndpointPoolOptions> EndpointPools { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class ModelEndpointPoolOptions
{
    public string Usage { get; set; } = "AnswerPool";

    public string Strategy { get; set; } = "LeastInFlight";

    public int QueueTimeoutMs { get; set; } = 10000;

    public int ModelConcurrencyLimit { get; set; }

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

    void RecordFallback(string endpointId);

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

internal sealed class InMemoryModelEndpointPoolScheduler : IModelEndpointPoolScheduler
{
    private const string RedactedEndpointMarker = "[redacted-endpoint]";
    private readonly object gate = new();
    private readonly IOptions<ModelProviderReliabilityOptions> options;
    private readonly ConcurrentDictionary<string, EndpointRuntimeStats> stats = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, ModelRuntimeStats> modelStats = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, QuotaWindow> userWindows = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, QuotaWindow> roleWindows = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, QuotaWindow> tenantWindows = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, long> roundRobinCounters = new(StringComparer.OrdinalIgnoreCase);
    private readonly Func<DateTimeOffset> utcNow;

    public InMemoryModelEndpointPoolScheduler(IOptions<ModelProviderReliabilityOptions> options)
        : this(options, () => DateTimeOffset.UtcNow)
    {
    }

    internal InMemoryModelEndpointPoolScheduler(
        IOptions<ModelProviderReliabilityOptions> options,
        Func<DateTimeOffset> utcNow)
    {
        this.options = options;
        this.utcNow = utcNow;
    }

    public ModelEndpointLease AcquireEndpoint(
        string poolName,
        AgentRuntimeCreateRequest request,
        ModelProviderExecutionContext context)
    {
        var modelKey = BuildModelKey(request);
        var tokenEstimate = EstimateTokens(request);
        var currentOptions = options.Value;

        if (!currentOptions.EndpointPools.TryGetValue(poolName, out var pool))
        {
            throw new ModelEndpointPoolNotConfiguredException(poolName);
        }

        var queuedEndpointIds = Array.Empty<string>();
        var queued = false;
        var deadline = utcNow().AddMilliseconds(Math.Max(0, pool.QueueTimeoutMs));

        lock (gate)
        {
            try
            {
                while (true)
                {
                    var now = utcNow();
                    var candidates = GetEnabledCandidates(pool)
                        .Where(endpoint => !GetStats(endpoint.EndpointId).IsCircuitOpen(now))
                        .ToArray();

                    if (candidates.Length == 0)
                    {
                        throw new InvalidOperationException($"Model endpoint pool '{poolName}' has no healthy endpoint.");
                    }

                    var available = candidates
                        .Where(endpoint => CanUseEndpoint(endpoint, modelKey, tokenEstimate, request, currentOptions, now))
                        .ToArray();

                    if (available.Length > 0)
                    {
                        if (queued)
                        {
                            DecrementQueue(queuedEndpointIds);
                            queued = false;
                        }

                        var selected = string.Equals(pool.Strategy, "WeightedRoundRobin", StringComparison.OrdinalIgnoreCase)
                            ? SelectWeightedRoundRobin(poolName, available)
                            : SelectLeastInFlight(available);

                        Reserve(selected, modelKey, tokenEstimate, request, currentOptions, now);
                        var selection = ToSelection(poolName, selected);
                        return new ModelEndpointLease(selection, modelKey, tokenEstimate, utcNow, ReleaseLease);
                    }

                    var queueLimit = candidates.Sum(endpoint => Math.Max(0, endpoint.QueueLimit));
                    var currentQueueLength = candidates.Max(endpoint => GetStats(endpoint.EndpointId).QueueLength);
                    if (!queued && (queueLimit <= 0 || currentQueueLength >= queueLimit))
                    {
                        throw new InvalidOperationException($"Model endpoint pool '{poolName}' queue is full.");
                    }

                    if (!queued)
                    {
                        queuedEndpointIds = candidates.Select(endpoint => endpoint.EndpointId).ToArray();
                        IncrementQueue(queuedEndpointIds);
                        queued = true;
                    }

                    var remaining = deadline - now;
                    if (remaining <= TimeSpan.Zero)
                    {
                        throw new TimeoutException($"Model endpoint pool '{poolName}' queue wait timed out.");
                    }

                    Monitor.Wait(gate, remaining);
                }
            }
            catch
            {
                if (queued)
                {
                    DecrementQueue(queuedEndpointIds);
                }

                throw;
            }
        }
    }

    public ModelEndpointSelection SelectEndpoint(string poolName)
    {
        if (!options.Value.EndpointPools.TryGetValue(poolName, out var pool))
        {
            throw new InvalidOperationException($"Model endpoint pool '{poolName}' is not configured.");
        }

        lock (gate)
        {
            var candidates = GetEnabledCandidates(pool)
                .Where(endpoint => !GetStats(endpoint.EndpointId).IsCircuitOpen(utcNow()))
                .Where(endpoint => GetStats(endpoint.EndpointId).InFlight < Math.Max(1, endpoint.ConcurrencyLimit))
                .ToArray();

            if (candidates.Length == 0)
            {
                throw new InvalidOperationException($"Model endpoint pool '{poolName}' has no healthy endpoint with available concurrency.");
            }

            var selected = string.Equals(pool.Strategy, "WeightedRoundRobin", StringComparison.OrdinalIgnoreCase)
                ? SelectWeightedRoundRobin(poolName, candidates)
                : SelectLeastInFlight(candidates);

            return ToSelection(poolName, selected);
        }
    }

    public void RecordStarted(string endpointId)
    {
        lock (gate)
        {
            GetStats(endpointId).RecordStarted();
        }
    }

    public void RecordSucceeded(string endpointId, TimeSpan duration)
    {
        lock (gate)
        {
            GetStats(endpointId).RecordSucceeded(duration);
            Monitor.PulseAll(gate);
        }
    }

    public void RecordFailed(string endpointId, TimeSpan duration, Exception exception)
    {
        lock (gate)
        {
            var endpointStats = GetStats(endpointId);
            endpointStats.RecordFailed(duration, exception);
            var currentOptions = options.Value;
            if (endpointStats.ConsecutiveFailures >= Math.Max(1, currentOptions.CircuitBreakerFailureThreshold))
            {
                endpointStats.OpenCircuit(TimeSpan.FromSeconds(Math.Max(1, currentOptions.CircuitBreakerOpenSeconds)), utcNow());
            }

            Monitor.PulseAll(gate);
        }
    }

    public void RecordRateLimited(string endpointId)
    {
        lock (gate)
        {
            GetStats(endpointId).RecordRateLimited();
        }
    }

    public void RecordFallback(string endpointId)
    {
        lock (gate)
        {
            GetStats(endpointId).RecordFallback();
        }
    }

    public void RecordStickyStreaming(string endpointId)
    {
        lock (gate)
        {
            GetStats(endpointId).RecordStickyStreaming();
        }
    }

    public ModelPoolSnapshotDto GetSnapshot()
    {
        lock (gate)
        {
            var pools = options.Value.EndpointPools
                .OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase)
                .Select(item => new ModelPoolDto(
                    item.Key,
                    item.Value.Usage,
                    item.Value.Strategy,
                    item.Value.Endpoints
                        .Select(endpoint => ToEndpointDto(endpoint, item.Value))
                        .ToArray()))
                .ToArray();

            return new ModelPoolSnapshotDto(pools);
        }
    }

    private void Reserve(
        ModelEndpointOptions endpoint,
        string modelKey,
        int tokenEstimate,
        AgentRuntimeCreateRequest request,
        ModelProviderReliabilityOptions currentOptions,
        DateTimeOffset now)
    {
        GetStats(endpoint.EndpointId).RecordStarted(tokenEstimate, now);
        GetModelStats(modelKey).RecordStarted();
        RecordCallerQuota(request.Caller, currentOptions, now);
    }

    private void ReleaseLease(ModelEndpointLease lease, TimeSpan duration, Exception? exception)
    {
        lock (gate)
        {
            var endpointStats = GetStats(lease.Selection.EndpointId);
            var modelRuntimeStats = GetModelStats(lease.ModelKey);
            modelRuntimeStats.RecordCompleted();

            if (exception is null)
            {
                endpointStats.RecordSucceeded(duration);
            }
            else
            {
                endpointStats.RecordFailed(duration, exception);
                var currentOptions = options.Value;
                if (endpointStats.ConsecutiveFailures >= Math.Max(1, currentOptions.CircuitBreakerFailureThreshold))
                {
                    endpointStats.OpenCircuit(TimeSpan.FromSeconds(Math.Max(1, currentOptions.CircuitBreakerOpenSeconds)), utcNow());
                }
            }

            Monitor.PulseAll(gate);
        }
    }

    private bool CanUseEndpoint(
        ModelEndpointOptions endpoint,
        string modelKey,
        int tokenEstimate,
        AgentRuntimeCreateRequest request,
        ModelProviderReliabilityOptions currentOptions,
        DateTimeOffset now)
    {
        var endpointStats = GetStats(endpoint.EndpointId);
        if (endpointStats.InFlight >= Math.Max(1, endpoint.ConcurrencyLimit))
        {
            return false;
        }

        var pool = FindPool(endpoint.EndpointId);
        if (pool?.ModelConcurrencyLimit > 0 &&
            GetModelStats(modelKey).InFlight >= pool.ModelConcurrencyLimit)
        {
            return false;
        }

        if (!endpointStats.CanReserve(tokenEstimate, Math.Max(0, endpoint.RpmLimit), Math.Max(0, endpoint.TpmLimit), now))
        {
            endpointStats.RecordRateLimited();
            return false;
        }

        return CanUseCallerQuota(request.Caller, currentOptions, now);
    }

    private bool CanUseCallerQuota(
        AgentRuntimeCallerContext? caller,
        ModelProviderReliabilityOptions currentOptions,
        DateTimeOffset now)
    {
        return CanUseQuota(userWindows, BuildUserQuotaKey(caller), currentOptions.PerUserRpmLimit, now)
               && CanUseQuota(roleWindows, BuildRoleQuotaKey(caller), currentOptions.PerRoleRpmLimit, now)
               && CanUseQuota(tenantWindows, BuildTenantQuotaKey(caller), currentOptions.PerTenantRpmLimit, now);
    }

    private void RecordCallerQuota(
        AgentRuntimeCallerContext? caller,
        ModelProviderReliabilityOptions currentOptions,
        DateTimeOffset now)
    {
        RecordQuota(userWindows, BuildUserQuotaKey(caller), currentOptions.PerUserRpmLimit, now);
        RecordQuota(roleWindows, BuildRoleQuotaKey(caller), currentOptions.PerRoleRpmLimit, now);
        RecordQuota(tenantWindows, BuildTenantQuotaKey(caller), currentOptions.PerTenantRpmLimit, now);
    }

    private static bool CanUseQuota(
        ConcurrentDictionary<string, QuotaWindow> windows,
        string key,
        int limit,
        DateTimeOffset now)
    {
        return limit <= 0 || windows.GetOrAdd(key, _ => new QuotaWindow()).CanReserve(limit, now);
    }

    private static void RecordQuota(
        ConcurrentDictionary<string, QuotaWindow> windows,
        string key,
        int limit,
        DateTimeOffset now)
    {
        if (limit <= 0)
        {
            return;
        }

        windows.GetOrAdd(key, _ => new QuotaWindow()).Reserve(now);
    }

    private static string BuildUserQuotaKey(AgentRuntimeCallerContext? caller)
    {
        if (caller?.UserId is { } userId)
        {
            return userId.ToString("N");
        }

        return string.IsNullOrWhiteSpace(caller?.UserName)
            ? "anonymous"
            : caller.UserName.Trim();
    }

    private static string BuildRoleQuotaKey(AgentRuntimeCallerContext? caller)
    {
        return string.IsNullOrWhiteSpace(caller?.Role)
            ? "unknown-role"
            : caller.Role.Trim();
    }

    private static string BuildTenantQuotaKey(AgentRuntimeCallerContext? caller)
    {
        return string.IsNullOrWhiteSpace(caller?.TenantId)
            ? "default-tenant"
            : caller.TenantId.Trim();
    }

    private ModelEndpointPoolOptions? FindPool(string endpointId)
    {
        return options.Value.EndpointPools.Values.FirstOrDefault(pool =>
            pool.Endpoints.Any(endpoint => string.Equals(endpoint.EndpointId, endpointId, StringComparison.OrdinalIgnoreCase)));
    }

    private static IReadOnlyList<ModelEndpointOptions> GetEnabledCandidates(ModelEndpointPoolOptions pool)
    {
        return pool.Endpoints
            .Where(endpoint => endpoint.IsEnabled)
            .Where(endpoint => !string.IsNullOrWhiteSpace(endpoint.EndpointId))
            .ToArray();
    }

    private void IncrementQueue(IEnumerable<string> endpointIds)
    {
        foreach (var endpointId in endpointIds)
        {
            GetStats(endpointId).IncrementQueue();
        }
    }

    private void DecrementQueue(IEnumerable<string> endpointIds)
    {
        foreach (var endpointId in endpointIds)
        {
            GetStats(endpointId).DecrementQueue();
        }
    }

    private ModelEndpointDto ToEndpointDto(ModelEndpointOptions endpoint, ModelEndpointPoolOptions pool)
    {
        var endpointStats = GetStats(endpoint.EndpointId);
        var modelInFlight = pool.ModelConcurrencyLimit <= 0
            ? 0
            : modelStats.Values.Sum(item => item.InFlight);

        return new ModelEndpointDto(
            endpoint.EndpointId,
            endpoint.Provider,
            string.IsNullOrWhiteSpace(endpoint.BaseUrl) ? string.Empty : RedactedEndpointMarker,
            !string.IsNullOrWhiteSpace(endpoint.BaseUrl),
            Math.Max(1, endpoint.ConcurrencyLimit),
            Math.Max(0, endpoint.QueueLimit),
            Math.Max(1, endpoint.TimeoutMs),
            Math.Max(0, endpoint.RpmLimit),
            Math.Max(0, endpoint.TpmLimit),
            Math.Max(1, endpoint.Weight),
            endpoint.Priority,
            endpoint.IsEnabled && !endpointStats.IsCircuitOpen(utcNow()),
            endpointStats.IsCircuitOpen(utcNow()),
            HasConfiguredCredential(endpoint),
            endpointStats.ToDto(modelInFlight, utcNow()));
    }

    private EndpointRuntimeStats GetStats(string endpointId)
    {
        return stats.GetOrAdd(endpointId, _ => new EndpointRuntimeStats());
    }

    private ModelRuntimeStats GetModelStats(string modelKey)
    {
        return modelStats.GetOrAdd(modelKey, _ => new ModelRuntimeStats());
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

    private static string BuildModelKey(AgentRuntimeCreateRequest request)
    {
        return $"{request.Model.Provider}:{request.Model.Name}".ToLowerInvariant();
    }

    private static int EstimateTokens(AgentRuntimeCreateRequest request)
    {
        return Math.Max(1, request.Options.MaxOutputTokens ?? request.Model.Parameters.MaxOutputTokens);
    }

    private ModelEndpointSelection ToSelection(string poolName, ModelEndpointOptions endpoint)
    {
        var apiKey = ResolveApiKey(endpoint);
        return new ModelEndpointSelection(
            poolName,
            endpoint.EndpointId,
            endpoint.Provider,
            endpoint.BaseUrl,
            !string.IsNullOrWhiteSpace(apiKey),
            apiKey);
    }

    private static string? ResolveApiKey(ModelEndpointOptions endpoint)
    {
        if (!string.IsNullOrWhiteSpace(endpoint.ApiKeyEnvironmentVariable))
        {
            var environmentValue = Environment.GetEnvironmentVariable(endpoint.ApiKeyEnvironmentVariable.Trim());
            if (!string.IsNullOrWhiteSpace(environmentValue))
            {
                return environmentValue;
            }
        }

        return string.IsNullOrWhiteSpace(endpoint.ApiKey) ? null : endpoint.ApiKey;
    }

    private static bool HasConfiguredCredential(ModelEndpointOptions endpoint)
    {
        return !string.IsNullOrWhiteSpace(endpoint.ApiKey)
               || !string.IsNullOrWhiteSpace(endpoint.ApiKeyEnvironmentVariable);
    }

    private sealed class EndpointRuntimeStats
    {
        private readonly List<double> durations = [];
        private readonly Queue<DateTimeOffset> requestWindow = new();
        private readonly Queue<TokenWindowItem> tokenWindow = new();
        private DateTimeOffset? circuitOpenUntil;

        public int InFlight { get; private set; }

        public int QueueLength { get; private set; }

        public int ConsecutiveFailures { get; private set; }

        public long SuccessCount { get; private set; }

        public long FailureCount { get; private set; }

        public long RateLimitCount { get; private set; }

        public long CircuitBreakerOpenCount { get; private set; }

        public long FallbackCount { get; private set; }

        public int StickyStreamingCount { get; private set; }

        public string? LastFailureReason { get; private set; }

        public bool IsCircuitOpen(DateTimeOffset now)
        {
            return circuitOpenUntil.HasValue && now < circuitOpenUntil.Value;
        }

        public void RecordStarted()
        {
            InFlight++;
        }

        public void RecordStarted(int tokenEstimate, DateTimeOffset now)
        {
            InFlight++;
            requestWindow.Enqueue(now);
            tokenWindow.Enqueue(new TokenWindowItem(now, tokenEstimate));
        }

        public void RecordSucceeded(TimeSpan duration)
        {
            InFlight = Math.Max(0, InFlight - 1);
            SuccessCount++;
            ConsecutiveFailures = 0;
            circuitOpenUntil = null;
            AddDuration(duration);
        }

        public void RecordFailed(TimeSpan duration, Exception exception)
        {
            InFlight = Math.Max(0, InFlight - 1);
            FailureCount++;
            ConsecutiveFailures++;
            LastFailureReason = exception.GetType().Name;
            AddDuration(duration);
        }

        public void OpenCircuit(TimeSpan duration, DateTimeOffset now)
        {
            circuitOpenUntil = now.Add(duration);
            CircuitBreakerOpenCount++;
        }

        public void RecordRateLimited()
        {
            RateLimitCount++;
        }

        public void RecordFallback()
        {
            FallbackCount++;
        }

        public void RecordStickyStreaming()
        {
            StickyStreamingCount++;
        }

        public void IncrementQueue()
        {
            QueueLength++;
        }

        public void DecrementQueue()
        {
            QueueLength = Math.Max(0, QueueLength - 1);
        }

        public bool CanReserve(int tokenEstimate, int rpmLimit, int tpmLimit, DateTimeOffset now)
        {
            PruneWindows(now);
            var withinRpm = rpmLimit <= 0 || requestWindow.Count < rpmLimit;
            var withinTpm = tpmLimit <= 0 || tokenWindow.Sum(item => item.Tokens) + tokenEstimate <= tpmLimit;
            return withinRpm && withinTpm;
        }

        public ModelEndpointStatsDto ToDto(int modelInFlight, DateTimeOffset now)
        {
            PruneWindows(now);
            var snapshot = durations.OrderBy(item => item).ToArray();
            var average = snapshot.Length == 0 ? 0 : snapshot.Average();
            var p95 = snapshot.Length == 0
                ? 0
                : snapshot[(int)Math.Floor((snapshot.Length - 1) * 0.95)];

            return new ModelEndpointStatsDto(
                InFlight,
                QueueLength,
                modelInFlight,
                SuccessCount,
                FailureCount,
                average,
                p95,
                RateLimitCount,
                CircuitBreakerOpenCount,
                FallbackCount,
                StickyStreamingCount,
                IsCircuitOpen(now) ? "Open" : "Closed",
                LastFailureReason);
        }

        private void AddDuration(TimeSpan duration)
        {
            durations.Add(Math.Max(0, duration.TotalMilliseconds));
            if (durations.Count > 512)
            {
                durations.RemoveAt(0);
            }
        }

        private void PruneWindows(DateTimeOffset now)
        {
            var min = now.AddMinutes(-1);
            while (requestWindow.Count > 0 && requestWindow.Peek() < min)
            {
                requestWindow.Dequeue();
            }

            while (tokenWindow.Count > 0 && tokenWindow.Peek().Timestamp < min)
            {
                tokenWindow.Dequeue();
            }
        }

        private sealed record TokenWindowItem(DateTimeOffset Timestamp, int Tokens);
    }

    private sealed class ModelRuntimeStats
    {
        public int InFlight { get; private set; }

        public void RecordStarted()
        {
            InFlight++;
        }

        public void RecordCompleted()
        {
            InFlight = Math.Max(0, InFlight - 1);
        }
    }

    private sealed class QuotaWindow
    {
        private readonly Queue<DateTimeOffset> requests = new();

        public bool CanReserve(int limit, DateTimeOffset now)
        {
            Prune(now);
            return requests.Count < limit;
        }

        public void Reserve(DateTimeOffset now)
        {
            Prune(now);
            requests.Enqueue(now);
        }

        private void Prune(DateTimeOffset now)
        {
            var min = now.AddMinutes(-1);
            while (requests.Count > 0 && requests.Peek() < min)
            {
                requests.Dequeue();
            }
        }
    }
}

internal sealed class ModelEndpointPoolNotConfiguredException(string poolName)
    : InvalidOperationException($"Model endpoint pool '{poolName}' is not configured.");

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
