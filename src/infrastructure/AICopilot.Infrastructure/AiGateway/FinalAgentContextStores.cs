using System.Text.Json;
using AICopilot.Services.Contracts;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;

namespace AICopilot.Infrastructure.AiGateway;

public sealed class MemoryCacheFinalAgentContextStore(IMemoryCache memoryCache) : IFinalAgentContextStore
{
    private static readonly MemoryCacheEntryOptions CacheOptions = new()
    {
        SlidingExpiration = TimeSpan.FromMinutes(10),
        AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(30)
    };

    public Task<StoredFinalAgentContext?> GetAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        memoryCache.TryGetValue(sessionId, out StoredFinalAgentContext? storedContext);
        return Task.FromResult(storedContext);
    }

    public Task SetAsync(Guid sessionId, StoredFinalAgentContext context, CancellationToken cancellationToken = default)
    {
        memoryCache.Set(sessionId, context, CacheOptions);
        return Task.CompletedTask;
    }

    public Task RemoveAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        memoryCache.Remove(sessionId);
        return Task.CompletedTask;
    }
}

public sealed class RedisFinalAgentContextStore(IDistributedCache distributedCache) : IFinalAgentContextStore
{
    private static readonly TimeSpan SlidingExpiration = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan AbsoluteExpiration = TimeSpan.FromMinutes(30);
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public async Task<StoredFinalAgentContext?> GetAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        var cacheKey = BuildCacheKey(sessionId);
        var payload = await distributedCache.GetStringAsync(cacheKey, cancellationToken);
        if (string.IsNullOrWhiteSpace(payload))
        {
            return null;
        }

        return JsonSerializer.Deserialize<StoredFinalAgentContext>(payload, SerializerOptions);
    }

    public async Task SetAsync(Guid sessionId, StoredFinalAgentContext context, CancellationToken cancellationToken = default)
    {
        var cacheKey = BuildCacheKey(sessionId);
        var payload = JsonSerializer.Serialize(context, SerializerOptions);
        var options = new DistributedCacheEntryOptions
        {
            SlidingExpiration = SlidingExpiration,
            AbsoluteExpirationRelativeToNow = AbsoluteExpiration
        };

        await distributedCache.SetStringAsync(cacheKey, payload, options, cancellationToken);
    }

    public async Task RemoveAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        await distributedCache.RemoveAsync(BuildCacheKey(sessionId), cancellationToken);
    }

    private static string BuildCacheKey(Guid sessionId)
    {
        return $"final-agent-context:{sessionId:N}";
    }
}
