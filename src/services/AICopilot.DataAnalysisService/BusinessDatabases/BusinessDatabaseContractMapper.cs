using AICopilot.Core.DataAnalysis.Aggregates.BusinessDatabase;
using AICopilot.Services.Contracts;

namespace AICopilot.DataAnalysisService.BusinessDatabases;

internal static class BusinessDatabaseContractMapper
{
    public static DatabaseProviderType ToContractProvider(DbProviderType provider)
    {
        return provider switch
        {
            DbProviderType.PostgreSql => DatabaseProviderType.PostgreSql,
            DbProviderType.SqlServer => DatabaseProviderType.SqlServer,
            DbProviderType.MySql => DatabaseProviderType.MySql,
            _ => throw new NotSupportedException($"Unsupported database provider: {provider}")
        };
    }

    public static DataSourceExternalSystemType ToContractExternalSystemType(
        BusinessDataExternalSystemType externalSystemType)
    {
        return externalSystemType switch
        {
            BusinessDataExternalSystemType.CloudReadOnly => DataSourceExternalSystemType.CloudReadOnly,
            BusinessDataExternalSystemType.NonCloud => DataSourceExternalSystemType.NonCloud,
            BusinessDataExternalSystemType.SimulationBusiness => DataSourceExternalSystemType.SimulationBusiness,
            BusinessDataExternalSystemType.Unknown => DataSourceExternalSystemType.Unknown,
            _ => throw new NotSupportedException($"Unsupported data source external system type: {externalSystemType}")
        };
    }

    public static BusinessDataExternalSystemType ToDomainExternalSystemType(
        DataSourceExternalSystemType externalSystemType)
    {
        return externalSystemType switch
        {
            DataSourceExternalSystemType.CloudReadOnly => BusinessDataExternalSystemType.CloudReadOnly,
            DataSourceExternalSystemType.NonCloud => BusinessDataExternalSystemType.NonCloud,
            DataSourceExternalSystemType.SimulationBusiness => BusinessDataExternalSystemType.SimulationBusiness,
            DataSourceExternalSystemType.Unknown => BusinessDataExternalSystemType.Unknown,
            _ => throw new NotSupportedException($"Unsupported data source external system type: {externalSystemType}")
        };
    }

    public static BusinessDatabaseDescriptor ToDescriptor(BusinessDatabase database)
    {
        return new BusinessDatabaseDescriptor(
            database.Id,
            database.Name,
            database.Description,
            ToContractProvider(database.Provider),
            database.IsEnabled,
            database.IsReadOnly,
            ToContractExternalSystemType(database.ExternalSystemType),
            database.ReadOnlyCredentialVerified,
            database.Category,
            SplitTags(database.Tags),
            database.OwnerDepartment,
            database.BusinessDomain,
            database.SensitivityLevel,
            database.DefaultQueryLimit,
            database.MaxQueryLimit,
            database.IsSelectableInChat,
            database.IsSelectableInAgent);
    }

    public static BusinessDatabaseConnectionInfo ToConnectionInfo(BusinessDatabase database)
    {
        return new BusinessDatabaseConnectionInfo(
            database.Id,
            database.Name,
            database.Description,
            database.ConnectionString,
            ToContractProvider(database.Provider),
            database.IsEnabled,
            database.IsReadOnly,
            ToContractExternalSystemType(database.ExternalSystemType),
            database.ReadOnlyCredentialVerified,
            database.Category,
            SplitTags(database.Tags),
            database.OwnerDepartment,
            database.BusinessDomain,
            database.SensitivityLevel,
            database.DefaultQueryLimit,
            database.MaxQueryLimit,
            database.IsSelectableInChat,
            database.IsSelectableInAgent);
    }

    private static IReadOnlyCollection<string> SplitTags(string tags)
    {
        return string.IsNullOrWhiteSpace(tags)
            ? []
            : tags.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }
}
