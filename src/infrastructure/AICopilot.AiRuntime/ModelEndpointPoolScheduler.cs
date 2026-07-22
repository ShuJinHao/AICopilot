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
        if (!options.Value.EndpointPools.TryGetValue(poolName, out var pool))
        {
            throw new ModelEndpointPoolNotConfiguredException(poolName);
        }

        lock (gate)
        {
            var now = utcNow();
            var candidates = GetEnabledCandidates(pool)
                .Where(endpoint => !GetStats(endpoint.EndpointId).IsCircuitOpen(now))
                .ToArray();
            if (candidates.Length == 0)
            {
                throw new InvalidOperationException($"Model endpoint pool '{poolName}' has no healthy endpoint.");
            }

            var selected = string.Equals(pool.Strategy, "WeightedRoundRobin", StringComparison.OrdinalIgnoreCase)
                ? SelectWeightedRoundRobin(poolName, candidates)
                : SelectLeastInFlight(candidates);
            ReserveLocalTelemetry(selected, modelKey, tokenEstimate, now);
            var selection = ToSelection(poolName, selected);
            return new ModelEndpointLease(selection, modelKey, tokenEstimate, utcNow, ReleaseLease);
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
                .ToArray();

            if (candidates.Length == 0)
            {
                throw new InvalidOperationException($"Model endpoint pool '{poolName}' has no healthy endpoint.");
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

    private void ReserveLocalTelemetry(
        ModelEndpointOptions endpoint,
        string modelKey,
        int tokenEstimate,
        DateTimeOffset now)
    {
        GetStats(endpoint.EndpointId).RecordStarted(tokenEstimate, now);
        GetModelStats(modelKey).RecordStarted();
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

    private static IReadOnlyList<ModelEndpointOptions> GetEnabledCandidates(ModelEndpointPoolOptions pool)
    {
        return pool.Endpoints
            .Where(endpoint => endpoint.IsEnabled)
            .Where(endpoint => !string.IsNullOrWhiteSpace(endpoint.EndpointId))
            .ToArray();
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
