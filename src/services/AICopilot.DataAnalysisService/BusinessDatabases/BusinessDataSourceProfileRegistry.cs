using System.Collections.Concurrent;
using System.Security.Cryptography;
using AICopilot.Services.Contracts;

namespace AICopilot.DataAnalysisService.BusinessDatabases;

internal static class BuiltInBusinessDataSourceProfiles
{
    private static readonly IReadOnlySet<BusinessDataCapability> SimulationCapabilities =
        new HashSet<BusinessDataCapability>
        {
            BusinessDataCapability.Device,
            BusinessDataCapability.DeviceLog,
            BusinessDataCapability.Capacity,
            BusinessDataCapability.ProductionRecord,
            BusinessDataCapability.Process
        };

    public static readonly BusinessDataSourceProfile CloudReadOnly =
        StandardBusinessDataSourceProfiles.CloudReadOnly;

    public static readonly BusinessDataSourceProfile SimulationBusiness = new(
        BusinessDataSourceProfileKeyResolver.SimulationBusiness,
        DataSourceExternalSystemType.SimulationBusiness,
        DatabaseProviderType.PostgreSql,
        IsRealExternalSource: false,
        RequiresExplicitSelection: true,
        SupportsTextToSqlFallback: false,
        SimulationCapabilities,
        BusinessQuerySecurityProfile.TableOnly(
            new HashSet<string>(["public"], StringComparer.OrdinalIgnoreCase),
            SimulationBusinessQuerySchema.AllowedTables,
            SimulationBusinessQuerySchema.AllowedColumns,
            SimulationBusinessQuerySchema.BlockedFieldFragments.ToHashSet(StringComparer.OrdinalIgnoreCase)));

}

internal sealed class CloudReadOnlyBusinessDataSourceProfileProvider
    : IBusinessDataSourceProfileProvider
{
    public BusinessDataSourceProfile Profile => BuiltInBusinessDataSourceProfiles.CloudReadOnly;
}

internal sealed class SimulationBusinessDataSourceProfileProvider
    : IBusinessDataSourceProfileProvider
{
    public BusinessDataSourceProfile Profile => BuiltInBusinessDataSourceProfiles.SimulationBusiness;
}

internal sealed class BusinessDataSourceProfileRegistry(
    IEnumerable<IBusinessDataSourceProfileProvider> providers)
    : IBusinessDataSourceProfileRegistry
{
    private readonly IReadOnlyDictionary<string, BusinessDataSourceProfile> profiles =
        BuildProfiles(providers);

    public IReadOnlyCollection<BusinessDataSourceProfile> GetAll()
    {
        return profiles.Values
            .OrderBy(profile => profile.Code, StringComparer.Ordinal)
            .ToArray();
    }

    public bool TryGet(
        string sourceKey,
        DataSourceExternalSystemType expectedSourceType,
        out BusinessDataSourceProfile profile)
    {
        return profiles.TryGetValue(sourceKey, out profile!) &&
               profile.SourceType == expectedSourceType;
    }

    public BusinessDataSourceProfile GetRequired(
        string sourceKey,
        DataSourceExternalSystemType expectedSourceType)
    {
        return TryGet(sourceKey, expectedSourceType, out var profile)
            ? profile
            : throw new InvalidOperationException(
                $"No governed business data-source profile is registered for '{sourceKey}/{expectedSourceType}'.");
    }

    private static IReadOnlyDictionary<string, BusinessDataSourceProfile> BuildProfiles(
        IEnumerable<IBusinessDataSourceProfileProvider> providers)
    {
        var profiles = new Dictionary<string, BusinessDataSourceProfile>(StringComparer.OrdinalIgnoreCase);
        foreach (var provider in providers)
        {
            var profile = provider.Profile;
            if (string.IsNullOrWhiteSpace(profile.Code))
            {
                throw new InvalidOperationException(
                    "Governed business data-source profile code cannot be empty.");
            }

            profile.QuerySecurity.EnsureComplete();
            if (profile.Capabilities.Count == 0)
            {
                throw new InvalidOperationException(
                    $"Governed business data-source profile '{profile.Code}' must declare at least one capability.");
            }

            if (!profiles.TryAdd(profile.Code, profile))
            {
                throw new InvalidOperationException(
                    $"Multiple governed business data-source profiles are registered for '{profile.Code}'.");
            }
        }

        return profiles;
    }
}

internal sealed class BusinessQueryContextStore(
    TimeProvider? timeProvider = null,
    TimeSpan? timeToLive = null) : IBusinessQueryContextStore
{
    private static readonly TimeSpan DefaultTimeToLive = TimeSpan.FromMinutes(30);
    private readonly ConcurrentDictionary<Guid, BusinessQueryContext> confirmedContexts = new();
    private readonly ConcurrentDictionary<Guid, PendingBusinessQueryConfirmation> pendingConfirmations = new();
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;
    private readonly TimeSpan ttl = timeToLive ?? DefaultTimeToLive;

    public BusinessQueryContext Resolve(BusinessQueryContext requested)
    {
        if (!confirmedContexts.TryGetValue(requested.TaskId, out var confirmed))
        {
            return requested;
        }

        if (confirmed.ConfirmedAtUtc is not { } confirmedAt ||
            clock.GetUtcNow() - confirmedAt > ttl)
        {
            confirmedContexts.TryRemove(requested.TaskId, out _);
            return requested;
        }

        if (requested.SourceExplicitlySelected &&
            !confirmed.HasSameTaskAndSource(requested))
        {
            return requested;
        }

        var sourceBoundRequest = requested with
        {
            SourceKey = confirmed.SourceKey,
            DataSourceId = confirmed.DataSourceId,
            SourceType = confirmed.SourceType,
            SourceExplicitlySelected = confirmed.SourceExplicitlySelected,
            Confirmation = requested.Confirmation with { Source = true }
        };

        if (confirmed.CanReuseFor(sourceBoundRequest))
        {
            return sourceBoundRequest with
            {
                Capability = confirmed.Capability,
                Confirmation = confirmed.Confirmation,
                ConfirmedAtUtc = confirmed.ConfirmedAtUtc
            };
        }

        return sourceBoundRequest with
        {
            Confirmation = sourceBoundRequest.Confirmation with
            {
                Capability = sourceBoundRequest.Confirmation.Capability ||
                             confirmed.HasSameCapability(sourceBoundRequest),
                BusinessObject = sourceBoundRequest.Confirmation.BusinessObject ||
                                 confirmed.HasSameBusinessObjectScope(sourceBoundRequest),
                TimeRange = sourceBoundRequest.Confirmation.TimeRange ||
                            confirmed.HasSameTimeRange(sourceBoundRequest),
                Filters = sourceBoundRequest.Confirmation.Filters ||
                          confirmed.HasSameFilters(sourceBoundRequest)
            },
            ConfirmedAtUtc = null
        };
    }

    public void Remember(BusinessQueryContext context)
    {
        if (!context.IsConfirmed ||
            context.ConfirmedAtUtc is null ||
            context.TaskId == Guid.Empty)
        {
            return;
        }

        confirmedContexts[context.TaskId] = context;
    }

    public BusinessQueryConfirmationChallenge BeginConfirmation(BusinessQueryContext requested)
    {
        if (requested.TaskId == Guid.Empty || requested.SemanticPlan is null)
        {
            throw new InvalidOperationException(
                "A pending business query confirmation requires a task id and semantic scope.");
        }

        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(16))
            .ToLowerInvariant();
        var expiresAtUtc = clock.GetUtcNow() + ttl;
        pendingConfirmations[requested.TaskId] = new PendingBusinessQueryConfirmation(
            token,
            requested,
            expiresAtUtc);
        return new BusinessQueryConfirmationChallenge(token, expiresAtUtc);
    }

    public bool TryConfirmPending(
        Guid taskId,
        string userMessage,
        out BusinessQueryContext confirmed)
    {
        confirmed = null!;
        if (taskId == Guid.Empty ||
            string.IsNullOrWhiteSpace(userMessage) ||
            !pendingConfirmations.TryGetValue(taskId, out var pending))
        {
            return false;
        }

        if (clock.GetUtcNow() > pending.ExpiresAtUtc)
        {
            pendingConfirmations.TryRemove(taskId, out _);
            return false;
        }

        var normalized = userMessage.Trim();
        if (!string.Equals(
                normalized,
                $"确认查询 {pending.Token}",
                StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(
                normalized,
                $"confirm query {pending.Token}",
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!pendingConfirmations.TryRemove(taskId, out var consumed))
        {
            return false;
        }

        confirmed = consumed.Context with
        {
            SourceExplicitlySelected = true,
            Confirmation = BusinessQueryConfirmation.Complete
        };
        confirmed = confirmed.Confirm(clock.GetUtcNow());
        Remember(confirmed);
        return true;
    }

    private sealed record PendingBusinessQueryConfirmation(
        string Token,
        BusinessQueryContext Context,
        DateTimeOffset ExpiresAtUtc);
}
