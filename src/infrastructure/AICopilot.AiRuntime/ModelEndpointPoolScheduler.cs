using System.Collections.Concurrent;
using AICopilot.Services.Contracts;
using Microsoft.Extensions.Options;

namespace AICopilot.AiRuntime;

internal sealed class InMemoryModelEndpointPoolScheduler : IModelEndpointPoolScheduler
{
    private const string RedactedEndpointMarker = "[redacted-endpoint]";
    private readonly object gate = new();
    private readonly IOptions<ModelProviderReliabilityOptions> options;
    private readonly ISecretProtector secretProtector;
    private readonly ConcurrentDictionary<string, EndpointRuntimeStats> stats = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, ModelRuntimeStats> modelStats = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, QuotaWindow> userWindows = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, QuotaWindow> roleWindows = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, QuotaWindow> tenantWindows = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, long> roundRobinCounters = new(StringComparer.OrdinalIgnoreCase);
    private readonly Func<DateTimeOffset> utcNow;

    public InMemoryModelEndpointPoolScheduler(
        IOptions<ModelProviderReliabilityOptions> options,
        ISecretProtector secretProtector)
        : this(options, secretProtector, () => DateTimeOffset.UtcNow)
    {
    }

    internal InMemoryModelEndpointPoolScheduler(
        IOptions<ModelProviderReliabilityOptions> options,
        ISecretProtector secretProtector,
        Func<DateTimeOffset> utcNow)
    {
        this.options = options;
        this.secretProtector = secretProtector;
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

    private string? ResolveApiKey(ModelEndpointOptions endpoint)
    {
        if (!string.IsNullOrWhiteSpace(endpoint.ApiKeyEnvironmentVariable))
        {
            var environmentVariable = endpoint.ApiKeyEnvironmentVariable.Trim();
            var environmentValue = Environment.GetEnvironmentVariable(environmentVariable);
            if (!string.IsNullOrWhiteSpace(environmentValue))
            {
                return RequireProtectedEndpointApiKey(
                    endpoint.EndpointId,
                    environmentValue,
                    $"environment variable '{environmentVariable}'");
            }
        }

        return string.IsNullOrWhiteSpace(endpoint.ApiKey)
            ? null
            : RequireProtectedEndpointApiKey(
                endpoint.EndpointId,
                endpoint.ApiKey,
                "AiRuntime:ProviderReliability endpoint ApiKey");
    }

    private string RequireProtectedEndpointApiKey(
        string endpointId,
        string apiKey,
        string source)
    {
        var trimmed = apiKey.Trim();
        if (!secretProtector.IsProtected(trimmed))
        {
            throw new InvalidOperationException(
                $"Model endpoint pool '{endpointId}' API key from {source} must be stored as an encrypted 'encv2:' secret before runtime use.");
        }

        return trimmed;
    }

    private static bool HasConfiguredCredential(ModelEndpointOptions endpoint)
    {
        return !string.IsNullOrWhiteSpace(endpoint.ApiKey)
               || !string.IsNullOrWhiteSpace(endpoint.ApiKeyEnvironmentVariable);
    }
}
