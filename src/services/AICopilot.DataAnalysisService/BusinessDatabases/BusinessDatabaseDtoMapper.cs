using AICopilot.Core.DataAnalysis.Aggregates.BusinessDatabase;
using AICopilot.Services.Contracts;

namespace AICopilot.DataAnalysisService.BusinessDatabases;

internal static class BusinessDatabaseDtoMapper
{
    public static BusinessDatabaseDto Map(BusinessDatabase db)
    {
        return new BusinessDatabaseDto
        {
            Id = db.Id,
            Name = db.Name,
            Description = db.Description,
            Provider = db.Provider,
            IsEnabled = db.IsEnabled,
            IsReadOnly = db.IsReadOnly,
            ExternalSystemType = BusinessDatabaseContractMapper.ToContractExternalSystemType(db.ExternalSystemType),
            ReadOnlyCredentialVerified = db.ReadOnlyCredentialVerified,
            CreatedAt = db.CreatedAt,
            HasConnectionString = !string.IsNullOrEmpty(db.ConnectionString),
            ConnectionStringMasked = string.IsNullOrEmpty(db.ConnectionString) ? null : "******",
            Category = db.Category,
            Tags = SplitTags(db.Tags),
            OwnerDepartment = db.OwnerDepartment,
            BusinessDomain = db.BusinessDomain,
            SensitivityLevel = db.SensitivityLevel,
            DefaultQueryLimit = db.DefaultQueryLimit,
            MaxQueryLimit = db.MaxQueryLimit,
            IsSelectableInChat = db.IsSelectableInChat,
            IsSelectableInAgent = db.IsSelectableInAgent,
            IsSimulation = db.ExternalSystemType == BusinessDataExternalSystemType.SimulationBusiness,
            SourceLabel = db.ExternalSystemType == BusinessDataExternalSystemType.SimulationBusiness
                ? BusinessQueryResultMapper.SimulationSourceLabel
                : db.Name,
            IsGovernedQueryEnabled = BusinessDataSourceGovernancePolicy.HasExecutableGovernedSchema(db),
            GovernanceStatus = BusinessDataSourceGovernancePolicy.ResolveGovernanceStatus(db)
        };
    }

    private static IReadOnlyCollection<string> SplitTags(string tags)
    {
        return string.IsNullOrWhiteSpace(tags)
            ? []
            : tags.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }
}

internal static class DataSourcePermissionGrantDtoMapper
{
    public static DataSourcePermissionGrantDto Map(DataSourcePermissionGrant grant)
    {
        return new DataSourcePermissionGrantDto(
            grant.Id,
            grant.DataSourceId,
            grant.TargetType.ToString(),
            grant.TargetValue,
            grant.CanQuery,
            grant.CanSchemaView,
            grant.IsEnabled,
            grant.CreatedAt,
            grant.UpdatedAt);
    }
}
