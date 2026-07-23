using AICopilot.Services.Contracts;

namespace AICopilot.AiGatewayService.Workflows.Executors;

internal sealed class BusinessQueryProviderRegistry(
    IEnumerable<IBusinessQueryProvider> providers,
    IBusinessDataSourceProfileRegistry profileRegistry)
    : IBusinessQueryProviderRegistry
{
    private readonly IReadOnlyDictionary<BusinessQueryProviderKey, IBusinessQueryProvider> providers =
        BuildProviders(providers, profileRegistry);

    public IBusinessQueryProvider ResolveRequired(BusinessQueryContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (!context.IsConfirmed)
        {
            throw new InvalidOperationException(
                "A complete confirmed BusinessQueryContext is required before resolving a business query provider.");
        }

        if (context.SourceType == DataSourceExternalSystemType.SimulationBusiness &&
            !context.SourceExplicitlySelected)
        {
            throw new InvalidOperationException(
                "SimulationBusiness requires explicit source selection before provider resolution.");
        }

        if (!providers.TryGetValue(
                BusinessQueryProviderKey.Create(context.SourceKey, context.Capability),
                out var provider) ||
            provider.SourceType != context.SourceType)
        {
            throw new InvalidOperationException(
                $"No governed business query provider is registered for '{context.SourceKey}/{context.SourceType}/{context.Capability}'.");
        }

        return provider;
    }

    private static IReadOnlyDictionary<BusinessQueryProviderKey, IBusinessQueryProvider> BuildProviders(
        IEnumerable<IBusinessQueryProvider> providers,
        IBusinessDataSourceProfileRegistry profileRegistry)
    {
        var registrations = new Dictionary<BusinessQueryProviderKey, IBusinessQueryProvider>();
        foreach (var provider in providers)
        {
            if (string.IsNullOrWhiteSpace(provider.ProviderCode) ||
                string.IsNullOrWhiteSpace(provider.SourceKey) ||
                provider.Capabilities.Count == 0)
            {
                throw new InvalidOperationException(
                    "Business query providers require a provider code, source key, and at least one capability.");
            }

            if (provider.ResultContracts.Keys.Any(capability =>
                    !provider.Capabilities.Contains(capability)) ||
                provider.Capabilities.Any(capability =>
                    !provider.ResultContracts.TryGetValue(capability, out var contract) ||
                    contract.AllowedFields.Count == 0))
            {
                throw new InvalidOperationException(
                    $"Business query provider '{provider.ProviderCode}' must declare a non-empty result contract for exactly every supported capability.");
            }

            if (!profileRegistry.TryGet(
                    provider.SourceKey,
                    provider.SourceType,
                    out var profile) ||
                provider.Capabilities.Any(capability =>
                    !profile.Capabilities.Contains(capability)))
            {
                throw new InvalidOperationException(
                    $"Business query provider '{provider.ProviderCode}' is not covered by a matching source profile and capability set.");
            }

            foreach (var capability in provider.Capabilities)
            {
                if (!profile.TryResolveCapabilityQueryProfile(
                        capability,
                        out var capabilityProfile))
                {
                    throw new InvalidOperationException(
                        $"Business query provider '{provider.ProviderCode}' capability has no matching capability profile.");
                }

                var resultContract = provider.ResultContracts[capability];
                if (capabilityProfile.QuerySecurity.BlockedIdentifierFragments.Any(profileBlocker =>
                        !resultContract.BlockedFieldFragments.Contains(
                            profileBlocker,
                            StringComparer.OrdinalIgnoreCase)))
                {
                    throw new InvalidOperationException(
                        $"Business query provider '{provider.ProviderCode}' result contract does not cover all source-profile sensitive field blockers.");
                }
            }

            foreach (var capability in provider.Capabilities)
            {
                var key = BusinessQueryProviderKey.Create(provider.SourceKey, capability);
                if (!registrations.TryAdd(key, provider))
                {
                    throw new InvalidOperationException(
                        $"Multiple business query providers are registered for '{provider.SourceKey}/{capability}'.");
                }
            }
        }

        return registrations;
    }

    private sealed record BusinessQueryProviderKey(
        string SourceKey,
        BusinessDataCapability Capability)
    {
        public static BusinessQueryProviderKey Create(
            string sourceKey,
            BusinessDataCapability capability)
        {
            return new BusinessQueryProviderKey(
                sourceKey.Trim().ToUpperInvariant(),
                capability);
        }
    }
}

internal sealed class CloudAiReadBusinessQueryProvider(
    ICloudAiReadClient cloudAiReadClient)
    : IBusinessQueryProvider
{
    private static readonly IReadOnlySet<BusinessDataCapability> SupportedCapabilities =
        Enum.GetValues<BusinessDataCapability>().ToHashSet();

    public string ProviderCode => "cloud-ai-read";

    public string SourceKey => StandardBusinessDataSourceProfiles.CloudReadOnly.Code;

    public DataSourceExternalSystemType SourceType => DataSourceExternalSystemType.CloudReadOnly;

    public IReadOnlySet<BusinessDataCapability> Capabilities => SupportedCapabilities;

    public IReadOnlyDictionary<BusinessDataCapability, BusinessQueryResultContract> ResultContracts { get; } =
        BuildResultContracts();

    public async Task<BusinessQueryProviderResult> QueryAsync(
        BusinessQueryContext context,
        CancellationToken cancellationToken = default)
    {
        if (!string.Equals(context.SourceKey, SourceKey, StringComparison.OrdinalIgnoreCase) ||
            context.SourceType != SourceType ||
            !Capabilities.Contains(context.Capability) ||
            context.SemanticPlan is null)
        {
            return BusinessQueryProviderResult.FromOutcome(
                context,
                ProviderCode,
                BusinessQueryOutcome.Unsupported,
                "The Cloud query plugin does not support this source or query shape.");
        }

        if (!cloudAiReadClient.IsEnabled)
        {
            return BusinessQueryProviderResult.FromOutcome(
                context,
                ProviderCode,
                BusinessQueryOutcome.Unavailable,
                "Cloud AiRead is not configured.");
        }

        try
        {
            var result = await cloudAiReadClient.QuerySemanticAsync(
                context.SemanticPlan,
                cancellationToken);
            var rows = result.Rows
                .Select(row => row.ToDictionary(
                    item => item.Key,
                    item => item.Value,
                    StringComparer.OrdinalIgnoreCase))
                .ToArray();
            var rowCount = result.RowCount > 0 ? result.RowCount : rows.Length;
            var outcome = rowCount == 0
                ? BusinessQueryOutcome.Empty
                : BusinessQueryOutcome.Success;

            return new BusinessQueryProviderResult(
                outcome,
                ProviderCode,
                context.SourceKey,
                context.DataSourceId,
                context.SourceType,
                context.Capability,
                rows,
                rowCount,
                result.IsTruncated,
                result.SourcePath,
                result.SourceLabel,
                result.QueriedAtUtc,
                outcome == BusinessQueryOutcome.Empty
                    ? "Cloud AiRead returned no matching business data."
                    : "Cloud AiRead query completed.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (CloudAiReadException ex)
        {
            return BusinessQueryProviderResult.FromOutcome(
                context,
                ProviderCode,
                MapOutcome(ex.Code),
                MapSafeMessage(ex.Code));
        }
    }

    private static BusinessQueryOutcome MapOutcome(string code)
    {
        return code switch
        {
            CloudAiReadProblemCodes.MissingRequiredParameter or
                CloudAiReadProblemCodes.InvalidRequest =>
                BusinessQueryOutcome.NeedClarification,
            CloudAiReadProblemCodes.Unauthorized or
                CloudAiReadProblemCodes.Forbidden or
                CloudAiReadProblemCodes.RequestBlocked =>
                BusinessQueryOutcome.Unauthorized,
            CloudAiReadProblemCodes.NotFound =>
                BusinessQueryOutcome.Empty,
            CloudAiReadProblemCodes.NotConfigured or
                CloudAiReadProblemCodes.RateLimited or
                CloudAiReadProblemCodes.Unavailable =>
                BusinessQueryOutcome.Unavailable,
            _ =>
                BusinessQueryOutcome.Unauthorized
        };
    }

    private static IReadOnlyDictionary<BusinessDataCapability, BusinessQueryResultContract>
        BuildResultContracts()
    {
        var blocked = StandardBusinessDataSourceProfiles.CloudReadOnly.QuerySecurity
            .BlockedIdentifierFragments;
        return new Dictionary<BusinessDataCapability, BusinessQueryResultContract>
        {
            [BusinessDataCapability.Device] = Contract(
                blocked,
                "deviceId", "deviceCode", "deviceName", "processId", "clientCode",
                "primaryIp", "channel", "hostVersion", "hostApiVersion",
                "versionReportedAtUtc", "versionReceivedAtUtc", "softwareStatus",
                "runtimeStatus", "runtimeStartedAtUtc", "lastRuntimeHeartbeatAtUtc",
                "updatedAtUtc"),
            [BusinessDataCapability.DeviceLog] = Contract(
                blocked,
                "logId", "deviceId", "deviceName", "level", "message", "logTime",
                "occurredAt", "receivedAt"),
            [BusinessDataCapability.Capacity] = Contract(
                blocked,
                "deviceId", "date", "time", "hour", "minute", "timeLabel",
                "shiftDate", "shiftCode", "totalCount", "okCount", "ngCount",
                "outputQty", "qualifiedQty", "dayShiftTotal", "nightShiftTotal", "okRate"),
            [BusinessDataCapability.ProductionRecord] = Contract(
                blocked,
                "recordId", "typeKey", "typeName", "deviceId", "deviceName",
                "productCode", "barcode", "result", "completedAt", "occurredAt",
                "receivedAt", "fields", "fieldSchema"),
            [BusinessDataCapability.Process] = Contract(
                blocked,
                "processId", "processCode", "processName"),
            [BusinessDataCapability.ClientRelease] = Contract(
                blocked,
                "releaseId", "componentKind", "componentKey", "displayName", "channel",
                "targetRuntime", "version", "status", "releaseNotes", "createdAtUtc",
                "publishedAtUtc", "deletedAtUtc")
        };
    }

    private static BusinessQueryResultContract Contract(
        IReadOnlySet<string> blocked,
        params string[] fields)
    {
        return new BusinessQueryResultContract(
            fields.ToHashSet(StringComparer.OrdinalIgnoreCase),
            blocked);
    }

    private static string MapSafeMessage(string code)
    {
        return code switch
        {
            CloudAiReadProblemCodes.MissingRequiredParameter or
                CloudAiReadProblemCodes.InvalidRequest =>
                "Cloud AiRead requires additional query conditions.",
            CloudAiReadProblemCodes.Unauthorized or
                CloudAiReadProblemCodes.Forbidden or
                CloudAiReadProblemCodes.RequestBlocked =>
                "Cloud AiRead rejected the current authorization scope.",
            CloudAiReadProblemCodes.NotFound =>
                "Cloud AiRead returned no matching business data.",
            CloudAiReadProblemCodes.NotConfigured or
                CloudAiReadProblemCodes.RateLimited or
                CloudAiReadProblemCodes.Unavailable =>
                "Cloud AiRead is temporarily unavailable.",
            _ =>
                "Cloud AiRead returned an unrecognized protected error; fallback is forbidden."
        };
    }
}

internal static class BusinessDataCapabilityMapper
{
    public static BusinessDataCapability FromSemanticTarget(SemanticQueryTarget target)
    {
        return target switch
        {
            SemanticQueryTarget.Device => BusinessDataCapability.Device,
            SemanticQueryTarget.DeviceLog => BusinessDataCapability.DeviceLog,
            SemanticQueryTarget.Capacity => BusinessDataCapability.Capacity,
            SemanticQueryTarget.ProductionData => BusinessDataCapability.ProductionRecord,
            SemanticQueryTarget.Process => BusinessDataCapability.Process,
            SemanticQueryTarget.ClientRelease => BusinessDataCapability.ClientRelease,
            _ => throw new InvalidOperationException(
                $"Semantic query target '{target}' does not map to a governed business capability.")
        };
    }
}
