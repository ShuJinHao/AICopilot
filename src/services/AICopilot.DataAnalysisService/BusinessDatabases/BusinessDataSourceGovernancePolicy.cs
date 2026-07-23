using AICopilot.Core.DataAnalysis.Aggregates.BusinessDatabase;
using AICopilot.Services.Contracts;

namespace AICopilot.DataAnalysisService.BusinessDatabases;

internal static class BusinessDataSourceGovernancePolicy
{
    public static bool IsSelectableForMode(
        BusinessDatabase database,
        DataSourceSelectionMode selectionMode,
        IBusinessDataSourceProfileRegistry profileRegistry)
    {
        if (!database.IsEnabled || !database.IsReadOnly)
        {
            return false;
        }

        var hasGovernedProfile = HasExecutableGovernedSchema(database, profileRegistry);
        return selectionMode switch
        {
            DataSourceSelectionMode.Agent => database.IsSelectableInAgent && hasGovernedProfile,
            DataSourceSelectionMode.Chat => false,
            DataSourceSelectionMode.TextToSql =>
                database.IsSelectableInChat &&
                database.ExternalSystemType == BusinessDataExternalSystemType.SimulationBusiness &&
                hasGovernedProfile,
            DataSourceSelectionMode.GovernedSql => hasGovernedProfile,
            DataSourceSelectionMode.Query => hasGovernedProfile,
            _ => false
        };
    }

    public static bool HasExecutableGovernedSchema(
        BusinessDatabase database,
        IBusinessDataSourceProfileRegistry profileRegistry)
    {
        return database.IsEnabled &&
               database.IsReadOnly &&
               ResolveSafetySchema(database, profileRegistry) is not null &&
               ValidateReadOnlyCredential(database, profileRegistry) is null;
    }

    public static string ResolveGovernanceStatus(
        BusinessDatabase database,
        IBusinessDataSourceProfileRegistry profileRegistry)
    {
        if (!database.IsEnabled)
        {
            return "Disabled";
        }

        if (!database.IsReadOnly)
        {
            return "BlockedNotReadOnly";
        }

        var credentialError = ValidateReadOnlyCredential(database, profileRegistry);
        if (credentialError is not null)
        {
            return "BlockedUnverifiedReadOnlyCredential";
        }

        return ResolveSafetySchema(database, profileRegistry) is null
            ? "BlockedUntilGovernedSchema"
            : "GovernedSchemaReady";
    }

    public static BusinessQuerySafetySchema? ResolveSafetySchema(
        BusinessDatabase database,
        IBusinessDataSourceProfileRegistry profileRegistry)
    {
        var sourceType = BusinessDatabaseContractMapper.ToContractExternalSystemType(
            database.ExternalSystemType);
        var sourceKey = ResolveProfileKey(database);
        if (!profileRegistry.TryGet(sourceKey, sourceType, out var profile))
        {
            return null;
        }

        return new BusinessQuerySafetySchema(
            profile.QuerySecurity.AllowedTables,
            profile.QuerySecurity.BlockedIdentifierFragments,
            AllowedColumnFragments: profile.QuerySecurity.AllowedColumns.Values
                .SelectMany(columns => columns)
                .ToHashSet(StringComparer.OrdinalIgnoreCase),
            SensitiveColumnFragments: profile.QuerySecurity.BlockedIdentifierFragments,
            AllowedColumns: profile.QuerySecurity.AllowedColumns.Count == 0
                ? null
                : profile.QuerySecurity.AllowedColumns);
    }

    public static string ResolveProfileKey(BusinessDatabase database)
    {
        return BusinessDataSourceProfileKeyResolver.Resolve(
            database.Name,
            BusinessDatabaseContractMapper.ToContractExternalSystemType(
                database.ExternalSystemType));
    }

    public static string? ValidateReadOnlyCredential(
        BusinessDatabase database,
        IBusinessDataSourceProfileRegistry profileRegistry)
    {
        if (!database.IsEnabled)
        {
            return null;
        }

        var sourceType = BusinessDatabaseContractMapper.ToContractExternalSystemType(
            database.ExternalSystemType);
        var sourceKey = ResolveProfileKey(database);
        if (profileRegistry.TryGet(sourceKey, sourceType, out var profile) &&
            profile.IsRealExternalSource &&
            !database.ReadOnlyCredentialVerified)
        {
            return "Real external data source requires a verified readonly credential before execution.";
        }

        if (database.Provider != DbProviderType.PostgreSql &&
            !database.ReadOnlyCredentialVerified)
        {
            return "SQL Server/MySQL data source requires a verified readonly credential before execution.";
        }

        return null;
    }

    public static string ResolveWarningCode(string safetyError)
    {
        if (safetyError.Contains("Wildcard", StringComparison.OrdinalIgnoreCase))
        {
            return "WILDCARD_PROJECTION_REJECTED";
        }

        if (safetyError.Contains("Table", StringComparison.OrdinalIgnoreCase))
        {
            return "TABLE_NOT_ALLOWLISTED";
        }

        if (safetyError.Contains("Sensitive", StringComparison.OrdinalIgnoreCase))
        {
            return "SENSITIVE_FIELD_REJECTED";
        }

        if (safetyError.Contains("DDL", StringComparison.OrdinalIgnoreCase) ||
            safetyError.Contains("DML", StringComparison.OrdinalIgnoreCase))
        {
            return "WRITE_SQL_REJECTED";
        }

        if (safetyError.Contains("Multiple", StringComparison.OrdinalIgnoreCase))
        {
            return "MULTI_STATEMENT_REJECTED";
        }

        if (safetyError.Contains("System catalog", StringComparison.OrdinalIgnoreCase))
        {
            return "SYSTEM_CATALOG_REJECTED";
        }

        return "SQL_GOVERNANCE_REJECTED";
    }
}
