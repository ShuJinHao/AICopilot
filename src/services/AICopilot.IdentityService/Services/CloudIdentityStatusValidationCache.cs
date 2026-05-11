using System.Collections.Concurrent;

namespace AICopilot.IdentityService.Services;

public interface ICloudIdentityStatusValidationCache
{
    bool TryGetSuccess(string tenantId, string cloudUserId, string statusVersion, DateTimeOffset now);

    void StoreSuccess(string tenantId, string cloudUserId, string statusVersion, DateTimeOffset expiresAt);

    void Remove(string tenantId, string cloudUserId);
}

internal sealed class CloudIdentityStatusValidationCache : ICloudIdentityStatusValidationCache
{
    private readonly ConcurrentDictionary<string, DateTimeOffset> _successCache = [];

    public bool TryGetSuccess(
        string tenantId,
        string cloudUserId,
        string statusVersion,
        DateTimeOffset now)
    {
        var key = BuildKey(tenantId, cloudUserId, statusVersion);
        if (!_successCache.TryGetValue(key, out var expiresAt))
        {
            return false;
        }

        if (expiresAt > now)
        {
            return true;
        }

        _successCache.TryRemove(key, out _);
        return false;
    }

    public void StoreSuccess(
        string tenantId,
        string cloudUserId,
        string statusVersion,
        DateTimeOffset expiresAt)
    {
        _successCache[BuildKey(tenantId, cloudUserId, statusVersion)] = expiresAt;
    }

    public void Remove(string tenantId, string cloudUserId)
    {
        var prefix = $"{tenantId}:{cloudUserId}:";
        foreach (var key in _successCache.Keys.Where(key => key.StartsWith(prefix, StringComparison.Ordinal)))
        {
            _successCache.TryRemove(key, out _);
        }
    }

    private static string BuildKey(string tenantId, string cloudUserId, string statusVersion)
    {
        return $"{tenantId}:{cloudUserId}:{statusVersion}";
    }
}
