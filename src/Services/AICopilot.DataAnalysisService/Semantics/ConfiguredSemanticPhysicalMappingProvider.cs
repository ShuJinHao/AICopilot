using AICopilot.Services.Contracts;
using Microsoft.Extensions.Configuration;

namespace AICopilot.DataAnalysisService.Semantics;

public sealed class ConfiguredSemanticPhysicalMappingProvider : ISemanticPhysicalMappingProvider
{
    public const string DefaultDatabaseName = "DeviceSemanticReadonly";
    public const string DefaultDeviceSourceName = "device_master_cloud_sim_view";
    public const string DefaultDeviceLogSourceName = "device_log_cloud_sim_view";
    public const string DefaultRecipeSourceName = "recipe_cloud_sim_view";
    public const string DefaultCapacitySourceName = "capacity_cloud_sim_view";
    public const string DefaultProductionDataSourceName = "production_data_cloud_sim_view";

    private readonly IReadOnlyDictionary<SemanticQueryTarget, SemanticPhysicalMapping> _mappings;

    public ConfiguredSemanticPhysicalMappingProvider(IConfiguration configuration)
    {
        _mappings = BuildMappings(configuration);
    }

    public bool TryGetMapping(SemanticQueryTarget target, out SemanticPhysicalMapping mapping)
    {
        return _mappings.TryGetValue(target, out mapping!);
    }

    private static IReadOnlyDictionary<SemanticQueryTarget, SemanticPhysicalMapping> BuildMappings(IConfiguration configuration)
    {
        var sharedDatabaseName = GetValue(
            configuration,
            "SemanticMappings:DatabaseName",
            "AICopilot:SemanticMappings:DatabaseName",
            DefaultDatabaseName);

        var defaults = new[]
        {
            new SemanticMappingDefaults(
                SemanticQueryTarget.Device,
                "Device",
                DefaultDeviceSourceName,
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["deviceId"] = "device_id",
                    ["deviceCode"] = "device_code",
                    ["deviceName"] = "device_name",
                    ["status"] = "status",
                    ["lineName"] = "line_name",
                    ["updatedAt"] = "updated_at"
                },
                ["deviceId", "deviceCode", "deviceName", "status", "lineName", "updatedAt"],
                ["deviceId", "deviceCode", "deviceName", "status", "lineName"],
                ["deviceCode", "deviceName", "updatedAt"],
                new SemanticSort("deviceCode", SemanticSortDirection.Asc)),
            new SemanticMappingDefaults(
                SemanticQueryTarget.DeviceLog,
                "DeviceLog",
                DefaultDeviceLogSourceName,
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["logId"] = "log_id",
                    ["deviceId"] = "device_id",
                    ["deviceCode"] = "device_code",
                    ["level"] = "log_level",
                    ["message"] = "log_message",
                    ["source"] = "log_source",
                    ["occurredAt"] = "occurred_at"
                },
                ["logId", "deviceId", "deviceCode", "level", "message", "source", "occurredAt"],
                ["deviceId", "deviceCode", "level", "source"],
                ["occurredAt", "level"],
                new SemanticSort("occurredAt", SemanticSortDirection.Desc)),
            new SemanticMappingDefaults(
                SemanticQueryTarget.Recipe,
                "Recipe",
                DefaultRecipeSourceName,
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["recipeId"] = "recipe_id",
                    ["recipeName"] = "recipe_name",
                    ["deviceId"] = "device_id",
                    ["deviceCode"] = "device_code",
                    ["processName"] = "process_name",
                    ["version"] = "version",
                    ["isActive"] = "is_active",
                    ["updatedAt"] = "updated_at"
                },
                ["recipeId", "recipeName", "deviceId", "deviceCode", "processName", "version", "isActive", "updatedAt"],
                ["recipeId", "recipeName", "deviceId", "deviceCode", "processName", "version", "isActive"],
                ["recipeName", "version", "updatedAt", "processName"],
                new SemanticSort("version", SemanticSortDirection.Desc)),
            new SemanticMappingDefaults(
                SemanticQueryTarget.Capacity,
                "Capacity",
                DefaultCapacitySourceName,
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["recordId"] = "record_id",
                    ["deviceId"] = "device_id",
                    ["deviceCode"] = "device_code",
                    ["processName"] = "process_name",
                    ["shiftDate"] = "shift_date",
                    ["outputQty"] = "output_qty",
                    ["qualifiedQty"] = "qualified_qty",
                    ["occurredAt"] = "occurred_at"
                },
                ["recordId", "deviceId", "deviceCode", "processName", "shiftDate", "outputQty", "qualifiedQty", "occurredAt"],
                ["recordId", "deviceId", "deviceCode", "processName", "shiftDate"],
                ["shiftDate", "occurredAt", "outputQty", "qualifiedQty", "deviceCode", "processName"],
                new SemanticSort("occurredAt", SemanticSortDirection.Desc)),
            new SemanticMappingDefaults(
                SemanticQueryTarget.ProductionData,
                "ProductionData",
                DefaultProductionDataSourceName,
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["recordId"] = "record_id",
                    ["deviceId"] = "device_id",
                    ["deviceCode"] = "device_code",
                    ["processName"] = "process_name",
                    ["barcode"] = "barcode",
                    ["stationName"] = "station_name",
                    ["result"] = "result",
                    ["occurredAt"] = "occurred_at"
                },
                ["recordId", "deviceId", "deviceCode", "processName", "barcode", "stationName", "result", "occurredAt"],
                ["recordId", "deviceId", "deviceCode", "processName", "barcode", "stationName", "result"],
                ["occurredAt", "deviceCode", "processName", "stationName", "result"],
                new SemanticSort("occurredAt", SemanticSortDirection.Desc))
        };

        return defaults.ToDictionary(
            item => item.Target,
            item => BuildMapping(configuration, sharedDatabaseName, item));
    }

    private static SemanticPhysicalMapping BuildMapping(
        IConfiguration configuration,
        string sharedDatabaseName,
        SemanticMappingDefaults defaults)
    {
        var databaseName = GetValue(
            configuration,
            $"SemanticMappings:{defaults.SectionName}:DatabaseName",
            $"AICopilot:SemanticMappings:{defaults.SectionName}:DatabaseName",
            sharedDatabaseName);

        var sourceName = GetValue(
            configuration,
            $"SemanticMappings:{defaults.SectionName}:SourceName",
            $"AICopilot:SemanticMappings:{defaults.SectionName}:SourceName",
            defaults.SourceName);

        var fromClause = GetOptionalValue(
            configuration,
            $"SemanticMappings:{defaults.SectionName}:FromClause",
            $"AICopilot:SemanticMappings:{defaults.SectionName}:FromClause");

        var provider = GetProvider(
            configuration,
            $"SemanticMappings:{defaults.SectionName}:Provider",
            $"AICopilot:SemanticMappings:{defaults.SectionName}:Provider",
            DatabaseProviderType.PostgreSql);

        var fieldMappings = GetFieldMappings(
            configuration,
            $"SemanticMappings:{defaults.SectionName}:FieldMappings",
            $"AICopilot:SemanticMappings:{defaults.SectionName}:FieldMappings",
            defaults.FieldMappings);

        var allowedProjectionFields = GetList(
            configuration,
            $"SemanticMappings:{defaults.SectionName}:AllowedProjectionFields",
            $"AICopilot:SemanticMappings:{defaults.SectionName}:AllowedProjectionFields",
            defaults.AllowedProjectionFields);
        var allowedFilterFields = GetList(
            configuration,
            $"SemanticMappings:{defaults.SectionName}:AllowedFilterFields",
            $"AICopilot:SemanticMappings:{defaults.SectionName}:AllowedFilterFields",
            defaults.AllowedFilterFields);
        var allowedSortFields = GetList(
            configuration,
            $"SemanticMappings:{defaults.SectionName}:AllowedSortFields",
            $"AICopilot:SemanticMappings:{defaults.SectionName}:AllowedSortFields",
            defaults.AllowedSortFields);

        var defaultSort = GetDefaultSort(
            configuration,
            $"SemanticMappings:{defaults.SectionName}:DefaultSort",
            $"AICopilot:SemanticMappings:{defaults.SectionName}:DefaultSort",
            defaults.DefaultSort);
        var defaultFilters = GetDefaultFilters(
            configuration,
            $"SemanticMappings:{defaults.SectionName}:DefaultFilters",
            $"AICopilot:SemanticMappings:{defaults.SectionName}:DefaultFilters");

        return new SemanticPhysicalMapping(
            defaults.Target,
            provider,
            sourceName,
            fieldMappings,
            allowedProjectionFields,
            allowedFilterFields,
            allowedSortFields,
            databaseName,
            fromClause,
            defaultSort,
            defaultFilters);
    }

    private static string GetValue(
        IConfiguration configuration,
        string primaryKey,
        string secondaryKey,
        string fallback)
    {
        return configuration[primaryKey]
               ?? configuration[secondaryKey]
               ?? fallback;
    }

    private static string? GetOptionalValue(
        IConfiguration configuration,
        string primaryKey,
        string secondaryKey)
    {
        return configuration[primaryKey]
               ?? configuration[secondaryKey];
    }

    private static DatabaseProviderType GetProvider(
        IConfiguration configuration,
        string primaryKey,
        string secondaryKey,
        DatabaseProviderType fallback)
    {
        var rawValue = GetOptionalValue(configuration, primaryKey, secondaryKey);
        return Enum.TryParse<DatabaseProviderType>(rawValue, true, out var provider)
            ? provider
            : fallback;
    }

    private static IReadOnlyDictionary<string, string> GetFieldMappings(
        IConfiguration configuration,
        string primarySectionPath,
        string secondarySectionPath,
        IReadOnlyDictionary<string, string> fallback)
    {
        var configuredMappings = ReadDictionary(configuration, primarySectionPath);
        if (configuredMappings.Count == 0)
        {
            configuredMappings = ReadDictionary(configuration, secondarySectionPath);
        }

        return configuredMappings.Count == 0
            ? fallback
            : new Dictionary<string, string>(configuredMappings, StringComparer.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<string> GetList(
        IConfiguration configuration,
        string primaryKey,
        string secondaryKey,
        IReadOnlyList<string> fallback)
    {
        var primaryValues = ReadList(configuration, primaryKey);
        if (primaryValues.Count != 0)
        {
            return primaryValues;
        }

        var secondaryValues = ReadList(configuration, secondaryKey);
        return secondaryValues.Count != 0
            ? secondaryValues
            : fallback;
    }

    private static SemanticSort? GetDefaultSort(
        IConfiguration configuration,
        string primarySectionPath,
        string secondarySectionPath,
        SemanticSort? fallback)
    {
        var sort = ReadSort(configuration, primarySectionPath);
        if (sort != null)
        {
            return sort;
        }

        return ReadSort(configuration, secondarySectionPath) ?? fallback;
    }

    private static IReadOnlyList<SemanticFilter> GetDefaultFilters(
        IConfiguration configuration,
        string primarySectionPath,
        string secondarySectionPath)
    {
        var filters = ReadFilters(configuration, primarySectionPath);
        if (filters.Count != 0)
        {
            return filters;
        }

        return ReadFilters(configuration, secondarySectionPath);
    }

    private static Dictionary<string, string> ReadDictionary(IConfiguration configuration, string sectionPath)
    {
        return configuration.GetSection(sectionPath)
            .GetChildren()
            .Where(child => !string.IsNullOrWhiteSpace(child.Key) && !string.IsNullOrWhiteSpace(child.Value))
            .ToDictionary(
                child => child.Key,
                child => child.Value!,
                StringComparer.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<string> ReadList(IConfiguration configuration, string keyOrSectionPath)
    {
        var scalarValue = configuration[keyOrSectionPath];
        if (!string.IsNullOrWhiteSpace(scalarValue))
        {
            return scalarValue.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        }

        return configuration.GetSection(keyOrSectionPath)
            .GetChildren()
            .Select(child => child.Value)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Cast<string>()
            .ToArray();
    }

    private static SemanticSort? ReadSort(IConfiguration configuration, string sectionPath)
    {
        var section = configuration.GetSection(sectionPath);
        var field = section["Field"];
        if (string.IsNullOrWhiteSpace(field))
        {
            return null;
        }

        var directionValue = section["Direction"];
        var direction = Enum.TryParse<SemanticSortDirection>(directionValue, true, out var parsedDirection)
            ? parsedDirection
            : SemanticSortDirection.Asc;

        return new SemanticSort(field, direction);
    }

    private static IReadOnlyList<SemanticFilter> ReadFilters(IConfiguration configuration, string sectionPath)
    {
        var filters = new List<SemanticFilter>();
        foreach (var child in configuration.GetSection(sectionPath).GetChildren())
        {
            var field = child["Field"];
            var value = child["Value"];
            if (string.IsNullOrWhiteSpace(field) || string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            var operatorValue = child["Operator"];
            var filterOperator = Enum.TryParse<SemanticFilterOperator>(operatorValue, true, out var parsedOperator)
                ? parsedOperator
                : SemanticFilterOperator.Equal;

            filters.Add(new SemanticFilter(field, filterOperator, value));
        }

        return filters;
    }

    private sealed record SemanticMappingDefaults(
        SemanticQueryTarget Target,
        string SectionName,
        string SourceName,
        IReadOnlyDictionary<string, string> FieldMappings,
        IReadOnlyList<string> AllowedProjectionFields,
        IReadOnlyList<string> AllowedFilterFields,
        IReadOnlyList<string> AllowedSortFields,
        SemanticSort DefaultSort);
}


