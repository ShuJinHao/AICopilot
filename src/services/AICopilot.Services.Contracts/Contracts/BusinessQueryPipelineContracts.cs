using System.Collections;
using System.Text.Json;

namespace AICopilot.Services.Contracts;

public enum BusinessDataCapability
{
    Device = 1,
    DeviceLog = 2,
    Capacity = 3,
    ProductionRecord = 4,
    Process = 5,
    ClientRelease = 6
}

public enum BusinessQueryOutcome
{
    Success = 1,
    Empty = 2,
    NeedClarification = 3,
    Unsupported = 4,
    Unavailable = 5,
    Unauthorized = 6
}

public sealed record BusinessQuerySecurityProfile(
    IReadOnlySet<string> AllowedTables,
    IReadOnlyDictionary<string, IReadOnlySet<string>> AllowedColumns,
    IReadOnlySet<string> BlockedIdentifierFragments,
    IReadOnlySet<string> AllowedSchemas)
{
    public static BusinessQuerySecurityProfile TableOnly(
        IReadOnlySet<string> allowedSchemas,
        IReadOnlySet<string> allowedTables,
        IReadOnlyDictionary<string, IReadOnlySet<string>> allowedColumns,
        IReadOnlySet<string> blockedIdentifierFragments)
    {
        return new BusinessQuerySecurityProfile(
            allowedTables,
            allowedColumns,
            blockedIdentifierFragments,
            allowedSchemas);
    }

    public void EnsureComplete()
    {
        if (AllowedSchemas.Count == 0 ||
            AllowedTables.Count == 0 ||
            AllowedColumns.Count == 0 ||
            AllowedTables.Any(table =>
                string.IsNullOrWhiteSpace(table) ||
                !AllowedColumns.TryGetValue(table, out var columns) ||
                columns.Count == 0) ||
            AllowedColumns.Keys.Any(table => !AllowedTables.Contains(table)))
        {
            throw new InvalidOperationException(
                "A governed business query security profile requires explicit schemas, tables, and columns.");
        }
    }
}

public sealed record BusinessTextToSqlJoinHint(
    string LeftTable,
    string LeftColumn,
    string RightTable,
    string RightColumn);

public sealed record BusinessTextToSqlProfile(
    string Dialect,
    string PromptTaskCode,
    IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> ColumnTypes,
    IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> ColumnValueHints,
    IReadOnlyList<BusinessTextToSqlJoinHint> JoinHints);

public sealed record BusinessDataSourceProfile(
    string Code,
    DataSourceExternalSystemType SourceType,
    DatabaseProviderType DatabaseProvider,
    bool IsRealExternalSource,
    bool RequiresExplicitSelection,
    bool SupportsTextToSqlFallback,
    IReadOnlySet<BusinessDataCapability> Capabilities,
    BusinessQuerySecurityProfile QuerySecurity,
    BusinessTextToSqlProfile? TextToSql = null,
    IReadOnlyDictionary<BusinessDataCapability, BusinessDataCapabilityQueryProfile>? CapabilityQueryProfiles = null)
{
    public bool TryResolveCapabilityQueryProfile(
        BusinessDataCapability capability,
        out BusinessDataSourceProfile profile)
    {
        profile = null!;
        if (!Capabilities.Contains(capability))
        {
            return false;
        }

        if (CapabilityQueryProfiles?.TryGetValue(capability, out var capabilityProfile) == true)
        {
            profile = this with
            {
                SupportsTextToSqlFallback = capabilityProfile.SupportsTextToSqlFallback,
                Capabilities = new HashSet<BusinessDataCapability> { capability },
                QuerySecurity = capabilityProfile.QuerySecurity,
                TextToSql = capabilityProfile.TextToSql,
                CapabilityQueryProfiles = null
            };
            return true;
        }

        if (Capabilities.Count == 1)
        {
            profile = this;
            return true;
        }

        return false;
    }
}

public sealed record BusinessDataCapabilityQueryProfile(
    bool SupportsTextToSqlFallback,
    BusinessQuerySecurityProfile QuerySecurity,
    BusinessTextToSqlProfile? TextToSql);

public static class StandardBusinessDataSourceProfiles
{
    private static readonly IReadOnlyDictionary<BusinessDataCapability, IReadOnlySet<string>>
        CloudCapabilityTables =
            new Dictionary<BusinessDataCapability, IReadOnlySet<string>>
            {
                [BusinessDataCapability.Device] = Tables("devices", "mfg_processes"),
                [BusinessDataCapability.DeviceLog] = Tables("device_logs", "devices", "mfg_processes"),
                [BusinessDataCapability.Capacity] = Tables("hourly_capacity", "devices", "mfg_processes"),
                [BusinessDataCapability.ProductionRecord] = Tables("pass_station_records", "devices", "mfg_processes"),
                [BusinessDataCapability.Process] = Tables("mfg_processes")
            };

    public static readonly BusinessDataSourceProfile CloudReadOnly = new(
        "cloud-readonly",
        DataSourceExternalSystemType.CloudReadOnly,
        DatabaseProviderType.PostgreSql,
        IsRealExternalSource: true,
        RequiresExplicitSelection: false,
        SupportsTextToSqlFallback: true,
        Enum.GetValues<BusinessDataCapability>().ToHashSet(),
        new BusinessQuerySecurityProfile(
            CloudReadOnlyGovernedSchema.AllowedTables,
            CloudReadOnlyGovernedSchema.AllowedColumns,
            CloudReadOnlyGovernedSchema.BlockedFieldFragments.ToHashSet(StringComparer.OrdinalIgnoreCase),
            new HashSet<string>(["public"], StringComparer.OrdinalIgnoreCase)),
        new BusinessTextToSqlProfile(
            "PostgreSQL",
            "governed-business-readonly-text-to-sql",
            CloudReadOnlyGovernedSchema.AllowedColumnTypes,
            CloudReadOnlyGovernedSchema.AllowedColumnValueHints,
            CloudReadOnlyGovernedSchema.JoinHints
                .Select(hint => new BusinessTextToSqlJoinHint(
                    hint.LeftTable,
                    hint.LeftColumn,
                    hint.RightTable,
                    hint.RightColumn))
                .ToArray()),
        BuildCloudCapabilityQueryProfiles());

    private static IReadOnlyDictionary<BusinessDataCapability, BusinessDataCapabilityQueryProfile>
        BuildCloudCapabilityQueryProfiles()
    {
        var profiles = CloudCapabilityTables.ToDictionary(
            pair => pair.Key,
            pair => CreateCloudCapabilityQueryProfile(pair.Value));
        profiles[BusinessDataCapability.ClientRelease] =
            new BusinessDataCapabilityQueryProfile(
                SupportsTextToSqlFallback: false,
                new BusinessQuerySecurityProfile(
                    CloudReadOnlyGovernedSchema.AllowedTables,
                    CloudReadOnlyGovernedSchema.AllowedColumns,
                    CloudReadOnlyGovernedSchema.BlockedFieldFragments.ToHashSet(
                        StringComparer.OrdinalIgnoreCase),
                    new HashSet<string>(["public"], StringComparer.OrdinalIgnoreCase)),
                TextToSql: null);
        return profiles;
    }

    private static BusinessDataCapabilityQueryProfile CreateCloudCapabilityQueryProfile(
        IReadOnlySet<string> tables)
    {
        var columns = CloudReadOnlyGovernedSchema.AllowedColumns
            .Where(pair => tables.Contains(pair.Key))
            .ToDictionary(
                pair => pair.Key,
                pair => pair.Value,
                StringComparer.OrdinalIgnoreCase);
        var columnTypes = CloudReadOnlyGovernedSchema.AllowedColumnTypes
            .Where(pair => tables.Contains(pair.Key))
            .ToDictionary(
                pair => pair.Key,
                pair => pair.Value,
                StringComparer.OrdinalIgnoreCase);
        var valueHints = CloudReadOnlyGovernedSchema.AllowedColumnValueHints
            .Where(pair => tables.Contains(pair.Key))
            .ToDictionary(
                pair => pair.Key,
                pair => pair.Value,
                StringComparer.OrdinalIgnoreCase);
        var joins = CloudReadOnlyGovernedSchema.JoinHints
            .Where(hint => tables.Contains(hint.LeftTable) && tables.Contains(hint.RightTable))
            .Select(hint => new BusinessTextToSqlJoinHint(
                hint.LeftTable,
                hint.LeftColumn,
                hint.RightTable,
                hint.RightColumn))
            .ToArray();

        return new BusinessDataCapabilityQueryProfile(
            SupportsTextToSqlFallback: true,
            new BusinessQuerySecurityProfile(
                tables,
                columns,
                CloudReadOnlyGovernedSchema.BlockedFieldFragments.ToHashSet(StringComparer.OrdinalIgnoreCase),
                new HashSet<string>(["public"], StringComparer.OrdinalIgnoreCase)),
            new BusinessTextToSqlProfile(
                "PostgreSQL",
                "governed-business-readonly-text-to-sql",
                columnTypes,
                valueHints,
                joins));
    }

    private static IReadOnlySet<string> Tables(params string[] tables)
    {
        return tables.ToHashSet(StringComparer.OrdinalIgnoreCase);
    }
}

public static class BusinessDataSourceProfileKeyResolver
{
    public const string SimulationBusiness = "simulation-business";

    public static string Resolve(
        string sourceName,
        DataSourceExternalSystemType sourceType)
    {
        return sourceType switch
        {
            DataSourceExternalSystemType.CloudReadOnly =>
                StandardBusinessDataSourceProfiles.CloudReadOnly.Code,
            DataSourceExternalSystemType.SimulationBusiness => SimulationBusiness,
            _ => string.IsNullOrWhiteSpace(sourceName)
                ? throw new InvalidOperationException(
                    "Non-built-in business data source requires a non-empty profile key/name.")
                : sourceName.Trim()
        };
    }

    public static string Resolve(BusinessDatabaseDescriptor source) =>
        Resolve(source.Name, source.ExternalSystemType);
}

public interface IBusinessDataSourceProfileProvider
{
    BusinessDataSourceProfile Profile { get; }
}

public interface IBusinessDataSourceProfileRegistry
{
    IReadOnlyCollection<BusinessDataSourceProfile> GetAll();

    bool TryGet(
        string sourceKey,
        DataSourceExternalSystemType expectedSourceType,
        out BusinessDataSourceProfile profile);

    BusinessDataSourceProfile GetRequired(
        string sourceKey,
        DataSourceExternalSystemType expectedSourceType);
}

public sealed record BusinessQueryConfirmation(
    bool Source,
    bool Capability,
    bool BusinessObject,
    bool TimeRange,
    bool Filters)
{
    public bool IsComplete =>
        Source &&
        Capability &&
        BusinessObject &&
        TimeRange &&
        Filters;

    public IReadOnlyList<string> MissingFields()
    {
        var missing = new List<string>(5);
        if (!Source)
        {
            missing.Add("source");
        }

        if (!Capability)
        {
            missing.Add("capability");
        }

        if (!BusinessObject)
        {
            missing.Add("businessObject");
        }

        if (!TimeRange)
        {
            missing.Add("timeRange");
        }

        if (!Filters)
        {
            missing.Add("filters");
        }

        return missing;
    }

    public static BusinessQueryConfirmation Complete { get; } =
        new(true, true, true, true, true);
}

public static class BusinessQueryConfirmationPolicy
{
    public static BusinessQueryConfirmation FromSemanticPlan(
        bool sourceConfirmed,
        bool capabilityConfirmed,
        bool confidenceConfirmed,
        SemanticQueryPlan? semanticPlan,
        bool businessObjectConfirmed = false,
        bool timeRangeConfirmed = false,
        bool filtersConfirmed = false)
    {
        if (!sourceConfirmed || !confidenceConfirmed || semanticPlan is null)
        {
            return new BusinessQueryConfirmation(
                sourceConfirmed,
                capabilityConfirmed && confidenceConfirmed,
                BusinessObject: false,
                TimeRange: false,
                Filters: false);
        }

        return new BusinessQueryConfirmation(
            Source: true,
            Capability: capabilityConfirmed,
            BusinessObject: businessObjectConfirmed,
            TimeRange: timeRangeConfirmed,
            Filters: filtersConfirmed);
    }
}

public sealed record BusinessQueryContext(
    Guid TaskId,
    string SourceKey,
    Guid? DataSourceId,
    DataSourceExternalSystemType SourceType,
    BusinessDataCapability Capability,
    string Question,
    bool SourceExplicitlySelected,
    BusinessQueryConfirmation Confirmation,
    SemanticQueryPlan? SemanticPlan = null,
    DateTimeOffset? ConfirmedAtUtc = null)
{
    public bool IsConfirmed => Confirmation.IsComplete;

    public BusinessQueryContext Confirm(DateTimeOffset? confirmedAtUtc = null)
    {
        if (!Confirmation.IsComplete)
        {
            throw new InvalidOperationException(
                $"Business query context cannot be confirmed while required scope fields are missing: {string.Join(",", Confirmation.MissingFields())}.");
        }

        return this with
        {
            ConfirmedAtUtc = confirmedAtUtc ?? DateTimeOffset.UtcNow
        };
    }

    public bool CanReuseFor(BusinessQueryContext requested)
    {
        if (!IsConfirmed ||
            TaskId == Guid.Empty ||
            TaskId != requested.TaskId ||
            !string.Equals(SourceKey, requested.SourceKey, StringComparison.OrdinalIgnoreCase) ||
            DataSourceId != requested.DataSourceId ||
            SourceType != requested.SourceType ||
            Capability != requested.Capability)
        {
            return false;
        }

        return HasSameCapability(requested) &&
               HasSameBusinessObjectScope(requested) &&
               HasSameTimeRange(requested) &&
               HasSameFilters(requested);
    }

    public bool HasSameTaskAndSource(BusinessQueryContext requested)
    {
        return IsConfirmed &&
               TaskId != Guid.Empty &&
               TaskId == requested.TaskId &&
               string.Equals(SourceKey, requested.SourceKey, StringComparison.OrdinalIgnoreCase) &&
               DataSourceId == requested.DataSourceId &&
               SourceType == requested.SourceType;
    }

    public bool HasSameCapability(BusinessQueryContext requested)
    {
        return Capability == requested.Capability &&
               ScopeMatches(SemanticPlan?.Target, requested.SemanticPlan?.Target);
    }

    public bool HasSameBusinessObjectScope(BusinessQueryContext requested)
    {
        return ScopeMatches(
            ResolveBusinessObjectFilters(SemanticPlan),
            ResolveBusinessObjectFilters(requested.SemanticPlan));
    }

    public bool HasSameTimeRange(BusinessQueryContext requested)
    {
        return ScopeMatches(SemanticPlan?.TimeRange, requested.SemanticPlan?.TimeRange);
    }

    public bool HasSameFilters(BusinessQueryContext requested)
    {
        return ScopeMatches(SemanticPlan?.Filters, requested.SemanticPlan?.Filters);
    }

    private static IReadOnlyList<SemanticFilter> ResolveBusinessObjectFilters(
        SemanticQueryPlan? plan)
    {
        if (plan is null)
        {
            return [];
        }

        return plan.Filters
            .Where(filter => filter.Field is
                "deviceId" or
                "deviceCode" or
                "processId" or
                "processCode" or
                "processName" or
                "recordId" or
                "barcode" or
                "componentKey" or
                "channel")
            .OrderBy(filter => filter.Field, StringComparer.Ordinal)
            .ThenBy(filter => filter.Operator)
            .ThenBy(filter => filter.Value, StringComparer.Ordinal)
            .ToArray();
    }

    private static bool ScopeMatches<T>(T? confirmed, T? requested)
    {
        return string.Equals(
            System.Text.Json.JsonSerializer.Serialize(confirmed),
            System.Text.Json.JsonSerializer.Serialize(requested),
            StringComparison.Ordinal);
    }
}

public sealed record BusinessQueryProviderResult(
    BusinessQueryOutcome Outcome,
    string ProviderCode,
    string SourceKey,
    Guid? DataSourceId,
    DataSourceExternalSystemType SourceType,
    BusinessDataCapability Capability,
    IReadOnlyList<Dictionary<string, object?>> Rows,
    int RowCount,
    bool IsTruncated,
    string SourcePath,
    string SourceLabel,
    DateTimeOffset? QueriedAtUtc,
    string SafeMessage)
{
    public static BusinessQueryProviderResult FromOutcome(
        BusinessQueryContext context,
        string providerCode,
        BusinessQueryOutcome outcome,
        string safeMessage,
        string sourcePath = "",
        string sourceLabel = "")
    {
        return new BusinessQueryProviderResult(
            outcome,
            providerCode,
            context.SourceKey,
            context.DataSourceId,
            context.SourceType,
            context.Capability,
            [],
            0,
            false,
            sourcePath,
            sourceLabel,
            null,
            safeMessage);
    }
}

public interface IBusinessQueryProvider
{
    string ProviderCode { get; }

    string SourceKey { get; }

    DataSourceExternalSystemType SourceType { get; }

    IReadOnlySet<BusinessDataCapability> Capabilities { get; }

    IReadOnlyDictionary<BusinessDataCapability, BusinessQueryResultContract> ResultContracts { get; }

    Task<BusinessQueryProviderResult> QueryAsync(
        BusinessQueryContext context,
        CancellationToken cancellationToken = default);
}

public sealed record BusinessQueryResultContract(
    IReadOnlySet<string> AllowedFields,
    IReadOnlySet<string> BlockedFieldFragments);

public interface IBusinessQueryProviderRegistry
{
    IBusinessQueryProvider ResolveRequired(BusinessQueryContext context);
}

public interface IBusinessQueryContextStore
{
    BusinessQueryContext Resolve(BusinessQueryContext requested);

    void Remember(BusinessQueryContext context);

    BusinessQueryConfirmationChallenge BeginConfirmation(BusinessQueryContext requested);

    bool TryConfirmPending(
        Guid taskId,
        string userMessage,
        out BusinessQueryContext confirmed);

}

public sealed record BusinessQueryConfirmationChallenge(
    string Token,
    DateTimeOffset ExpiresAtUtc);

public sealed record BusinessQueryFallbackDecision(
    bool IsEligible,
    bool RequiresModelDecision,
    string ReasonCode)
{
    public static BusinessQueryFallbackDecision Denied(string reasonCode)
    {
        return new BusinessQueryFallbackDecision(false, false, reasonCode);
    }

    public static BusinessQueryFallbackDecision Eligible(string reasonCode)
    {
        return new BusinessQueryFallbackDecision(true, true, reasonCode);
    }
}

public static class BusinessQueryFallbackPolicy
{
    public static BusinessQueryFallbackDecision EvaluateSameSourceTextToSql(
        BusinessQueryContext context,
        BusinessQueryProviderResult pluginResult,
        BusinessDataSourceProfile profile)
    {
        if (!profile.SupportsTextToSqlFallback)
        {
            return BusinessQueryFallbackDecision.Denied("profile_fallback_disabled");
        }

        if (!context.IsConfirmed)
        {
            return BusinessQueryFallbackDecision.Denied("query_context_not_confirmed");
        }

        if (!string.Equals(context.SourceKey, profile.Code, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(context.SourceKey, pluginResult.SourceKey, StringComparison.OrdinalIgnoreCase) ||
            context.DataSourceId != pluginResult.DataSourceId ||
            context.SourceType != pluginResult.SourceType ||
            context.SourceType != profile.SourceType ||
            context.Capability != pluginResult.Capability ||
            !profile.Capabilities.Contains(context.Capability))
        {
            return BusinessQueryFallbackDecision.Denied("cross_source_fallback_forbidden");
        }

        if (profile.RequiresExplicitSelection && !context.SourceExplicitlySelected)
        {
            return BusinessQueryFallbackDecision.Denied("explicit_source_selection_required");
        }

        if (!profile.TryResolveCapabilityQueryProfile(context.Capability, out var capabilityProfile) ||
            !capabilityProfile.SupportsTextToSqlFallback ||
            capabilityProfile.TextToSql is null)
        {
            return BusinessQueryFallbackDecision.Denied("capability_fallback_disabled");
        }

        return pluginResult.Outcome switch
        {
            BusinessQueryOutcome.Unsupported =>
                BusinessQueryFallbackDecision.Eligible("plugin_unsupported_same_source"),
            BusinessQueryOutcome.Unavailable =>
                BusinessQueryFallbackDecision.Eligible("plugin_unavailable_same_source"),
            BusinessQueryOutcome.Unauthorized =>
                BusinessQueryFallbackDecision.Denied("unauthorized_fallback_forbidden"),
            BusinessQueryOutcome.Empty =>
                BusinessQueryFallbackDecision.Denied("empty_is_terminal"),
            BusinessQueryOutcome.NeedClarification =>
                BusinessQueryFallbackDecision.Denied("clarification_required"),
            BusinessQueryOutcome.Success =>
                BusinessQueryFallbackDecision.Denied("plugin_succeeded"),
            _ =>
                BusinessQueryFallbackDecision.Denied("unsupported_outcome")
        };
    }
}

public static class BusinessQueryProviderResultContract
{
    public static void EnsureMatches(
        BusinessQueryContext context,
        IBusinessQueryProvider provider,
        BusinessQueryProviderResult result)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(result);

        if (!string.Equals(result.ProviderCode, provider.ProviderCode, StringComparison.Ordinal) ||
            !string.Equals(result.SourceKey, context.SourceKey, StringComparison.OrdinalIgnoreCase) ||
            result.DataSourceId != context.DataSourceId ||
            result.SourceType != context.SourceType ||
            result.SourceType != provider.SourceType ||
            result.Capability != context.Capability ||
            !provider.Capabilities.Contains(result.Capability))
        {
            throw new InvalidOperationException(
                "Business query provider returned a result outside the confirmed source and capability context.");
        }

        if (result.RowCount < 0 ||
            result.Rows.Count > result.RowCount ||
            result.Outcome == BusinessQueryOutcome.Success &&
            (result.RowCount == 0 || result.Rows.Count == 0) ||
            result.Outcome == BusinessQueryOutcome.Empty &&
            (result.RowCount != 0 || result.Rows.Count != 0) ||
            result.Outcome is not (BusinessQueryOutcome.Success or BusinessQueryOutcome.Empty) &&
            (result.RowCount != 0 || result.Rows.Count != 0))
        {
            throw new InvalidOperationException(
                "Business query provider returned an invalid structured outcome payload.");
        }

        if (result.Rows.Count == 0)
        {
            return;
        }

        if (!provider.ResultContracts.TryGetValue(
                result.Capability,
                out var resultContract) ||
            resultContract.AllowedFields.Count == 0)
        {
            throw new InvalidOperationException(
                "Business query provider has no declared result contract for the confirmed capability.");
        }

        foreach (var field in result.Rows.SelectMany(row => row.Keys))
        {
            if (!resultContract.AllowedFields.Contains(field) ||
                IsBlocked(field, resultContract.BlockedFieldFragments))
            {
                throw new InvalidOperationException(
                    "Business query provider returned a field outside its declared result contract.");
            }
        }

        if (result.Rows
            .SelectMany(row => row.Values)
            .Any(value => !IsSafeNestedValue(
                value,
                resultContract.BlockedFieldFragments)))
        {
            throw new InvalidOperationException(
                "Business query provider returned a blocked or unsupported nested result value.");
        }
    }

    private static bool IsSafeNestedValue(
        object? value,
        IReadOnlySet<string> blockedFieldFragments)
    {
        if (value is null ||
            value is string or
                bool or
                byte or sbyte or
                short or ushort or
                int or uint or
                long or ulong or
                float or double or decimal or
                DateTime or DateTimeOffset or
                DateOnly or TimeOnly or
                Guid or
                Enum)
        {
            return true;
        }

        if (value is JsonDocument document)
        {
            return IsSafeJsonElement(document.RootElement, blockedFieldFragments);
        }

        if (value is JsonElement element)
        {
            return IsSafeJsonElement(element, blockedFieldFragments);
        }

        if (value is IReadOnlyDictionary<string, object?> readOnlyDictionary)
        {
            return readOnlyDictionary.All(pair =>
                !IsBlocked(pair.Key, blockedFieldFragments) &&
                IsSafeNestedValue(pair.Value, blockedFieldFragments));
        }

        if (value is IDictionary dictionary)
        {
            foreach (DictionaryEntry entry in dictionary)
            {
                if (entry.Key is not string key ||
                    IsBlocked(key, blockedFieldFragments) ||
                    !IsSafeNestedValue(entry.Value, blockedFieldFragments))
                {
                    return false;
                }
            }

            return true;
        }

        if (value is IEnumerable sequence)
        {
            foreach (var item in sequence)
            {
                if (!IsSafeNestedValue(item, blockedFieldFragments))
                {
                    return false;
                }
            }

            return true;
        }

        try
        {
            return IsSafeJsonElement(
                JsonSerializer.SerializeToElement(value, value.GetType()),
                blockedFieldFragments);
        }
        catch
        {
            return false;
        }
    }

    private static bool IsSafeJsonElement(
        JsonElement element,
        IReadOnlySet<string> blockedFieldFragments)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Null or
                JsonValueKind.True or
                JsonValueKind.False or
                JsonValueKind.Number or
                JsonValueKind.String => true,
            JsonValueKind.Array => element.EnumerateArray()
                .All(item => IsSafeJsonElement(item, blockedFieldFragments)),
            JsonValueKind.Object => element.EnumerateObject()
                .All(property =>
                    !IsBlocked(property.Name, blockedFieldFragments) &&
                    IsSafeJsonElement(property.Value, blockedFieldFragments)),
            _ => false
        };
    }

    private static bool IsBlocked(
        string field,
        IReadOnlySet<string> blockedFieldFragments)
    {
        var normalized = field.Replace("_", string.Empty, StringComparison.Ordinal)
            .Replace("-", string.Empty, StringComparison.Ordinal);
        return blockedFieldFragments.Any(fragment =>
            normalized.Contains(
                fragment.Replace("_", string.Empty, StringComparison.Ordinal)
                    .Replace("-", string.Empty, StringComparison.Ordinal),
                StringComparison.OrdinalIgnoreCase));
    }
}
