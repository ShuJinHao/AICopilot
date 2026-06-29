using AICopilot.Core.DataAnalysis.Aggregates.BusinessDatabase;
using AICopilot.Services.Contracts;

namespace AICopilot.DataAnalysisService.BusinessDatabases;

internal static class BusinessDataSourceGovernancePolicy
{
    public static bool IsSelectableForMode(
        BusinessDatabase database,
        DataSourceSelectionMode selectionMode)
    {
        if (!database.IsEnabled || !database.IsReadOnly)
        {
            return false;
        }

        return selectionMode switch
        {
            DataSourceSelectionMode.Agent => database.IsSelectableInAgent &&
                                             database.ExternalSystemType == BusinessDataExternalSystemType.SimulationBusiness,
            DataSourceSelectionMode.Chat => database.IsSelectableInChat,
            DataSourceSelectionMode.TextToSql => database.IsSelectableInChat &&
                                                 database.ExternalSystemType == BusinessDataExternalSystemType.SimulationBusiness,
            DataSourceSelectionMode.GovernedSql => true,
            DataSourceSelectionMode.Query => true,
            _ => false
        };
    }

    public static bool HasExecutableGovernedSchema(BusinessDatabase database)
    {
        return database.IsEnabled &&
               database.IsReadOnly &&
               ResolveSafetySchema(database) is not null &&
               ValidateReadOnlyCredential(database) is null;
    }

    public static string ResolveGovernanceStatus(BusinessDatabase database)
    {
        if (!database.IsEnabled)
        {
            return "Disabled";
        }

        if (!database.IsReadOnly)
        {
            return "BlockedNotReadOnly";
        }

        var credentialError = ValidateReadOnlyCredential(database);
        if (credentialError is not null)
        {
            return "BlockedUnverifiedReadOnlyCredential";
        }

        return ResolveSafetySchema(database) is null
            ? "BlockedUntilGovernedSchema"
            : "GovernedSchemaReady";
    }

    public static BusinessQuerySafetySchema? ResolveSafetySchema(BusinessDatabase database)
    {
        return database.ExternalSystemType switch
        {
            BusinessDataExternalSystemType.SimulationBusiness => SimulationBusinessQuerySchema.SafetySchema,
            BusinessDataExternalSystemType.CloudReadOnly => CloudReadOnlyBusinessQuerySchema.SafetySchema,
            _ => null
        };
    }

    public static string? ValidateReadOnlyCredential(BusinessDatabase database)
    {
        if (!database.IsEnabled)
        {
            return null;
        }

        if (database.ExternalSystemType == BusinessDataExternalSystemType.CloudReadOnly &&
            !database.ReadOnlyCredentialVerified)
        {
            return "Cloud read-only data source requires a verified readonly credential before execution.";
        }

        if (database.Provider != DbProviderType.PostgreSql && !database.ReadOnlyCredentialVerified)
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
