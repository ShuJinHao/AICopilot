using AICopilot.Core.DataAnalysis.Aggregates.BusinessDatabase;
using AICopilot.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace AICopilot.MigrationWorkApp;

internal static class MigrationWorkerCloudReadOnlySeeder
{
    internal const string SectionPath = "DataAnalysis:CloudReadOnly";
    internal const string DefaultDatabaseName = "DeviceSemanticReadonly";
    private const string DefaultDescription = "真实 Cloud 只读业务数据源，包含设备、日志、产能和过站记录。";

    public static async Task EnsureSourceAsync(
        IConfiguration configuration,
        DataAnalysisDbContext dataAnalysisDbContext,
        CancellationToken cancellationToken)
    {
        var options = ResolveOptions(configuration);
        if (!options.Enabled)
        {
            return;
        }

        ValidateOptions(configuration, options);

        var database = await dataAnalysisDbContext.BusinessDatabases
            .SingleOrDefaultAsync(item => item.Name == options.DatabaseName, cancellationToken);

        if (database is null)
        {
            dataAnalysisDbContext.BusinessDatabases.Add(CreateBusinessDatabase(options));
        }
        else
        {
            UpdateBusinessDatabase(database, options);
        }

        await dataAnalysisDbContext.SaveChangesAsync(cancellationToken);
    }

    internal static CloudReadOnlyBusinessDatabaseOptions ResolveOptions(IConfiguration configuration)
    {
        var section = configuration.GetSection(SectionPath);
        return new CloudReadOnlyBusinessDatabaseOptions(
            Enabled: GetBool(section, "Enabled", fallback: false),
            DatabaseName: GetValue(section, "DatabaseName", DefaultDatabaseName),
            Description: GetValue(section, "Description", DefaultDescription),
            ConnectionString: GetConnectionString(configuration, section),
            ReadOnlyCredentialVerified: GetBool(section, "ReadOnlyCredentialVerified", fallback: false),
            DefaultQueryLimit: GetInt(section, "DefaultQueryLimit", 200),
            MaxQueryLimit: GetInt(section, "MaxQueryLimit", 1000));
    }

    internal static void ValidateOptions(
        IConfiguration configuration,
        CloudReadOnlyBusinessDatabaseOptions options)
    {
        if (!options.Enabled)
        {
            return;
        }

        if (IsSimulationSeedEnabled(configuration))
        {
            throw new InvalidOperationException(
                "DataAnalysis CloudReadOnly direct database mode cannot be enabled while CloudReadonly Simulation seeding is enabled.");
        }

        if (string.IsNullOrWhiteSpace(options.ConnectionString))
        {
            throw new InvalidOperationException(
                "DataAnalysis:CloudReadOnly:ConnectionString is required when DataAnalysis:CloudReadOnly:Enabled=true.");
        }

        if (!options.ReadOnlyCredentialVerified)
        {
            throw new InvalidOperationException(
                "DataAnalysis:CloudReadOnly:ReadOnlyCredentialVerified must be true before enabling the real Cloud readonly database source.");
        }
    }

    internal static BusinessDatabase CreateBusinessDatabase(CloudReadOnlyBusinessDatabaseOptions options)
    {
        return new BusinessDatabase(
            options.DatabaseName,
            options.Description,
            options.ConnectionString!,
            DbProviderType.PostgreSql,
            isReadOnly: true,
            externalSystemType: BusinessDataExternalSystemType.CloudReadOnly,
            readOnlyCredentialVerified: true,
            isEnabled: true,
            category: "CloudReadonly",
            tags: ["cloud-readonly", "direct-db", "semantic"],
            ownerDepartment: "AICopilot",
            businessDomain: "Manufacturing",
            sensitivityLevel: "Internal",
            defaultQueryLimit: options.DefaultQueryLimit,
            maxQueryLimit: options.MaxQueryLimit,
            isSelectableInChat: true,
            isSelectableInAgent: true);
    }

    private static void UpdateBusinessDatabase(
        BusinessDatabase database,
        CloudReadOnlyBusinessDatabaseOptions options)
    {
        database.UpdateInfo(options.DatabaseName, options.Description);
        database.UpdateConnection(options.ConnectionString!, DbProviderType.PostgreSql);
        database.UpdateSettings(
            isEnabled: true,
            isReadOnly: true,
            externalSystemType: BusinessDataExternalSystemType.CloudReadOnly,
            readOnlyCredentialVerified: true);
        database.UpdateGovernance(
            category: "CloudReadonly",
            tags: ["cloud-readonly", "direct-db", "semantic"],
            ownerDepartment: "AICopilot",
            businessDomain: "Manufacturing",
            sensitivityLevel: "Internal",
            defaultQueryLimit: options.DefaultQueryLimit,
            maxQueryLimit: options.MaxQueryLimit,
            isSelectableInChat: true,
            isSelectableInAgent: true);
    }

    private static string? GetConnectionString(
        IConfiguration configuration,
        IConfigurationSection section)
    {
        return section["ConnectionString"]
               ?? configuration.GetConnectionString("cloud-platform-readonly")
               ?? configuration["DATA_ANALYSIS_CLOUD_READONLY_CONNECTION_STRING"];
    }

    private static bool IsSimulationSeedEnabled(IConfiguration configuration)
    {
        var mode = configuration["CloudReadonly:Mode"];
        var enabled = configuration["CloudReadonly:Simulation:Enabled"];
        return string.Equals(mode, "Simulation", StringComparison.OrdinalIgnoreCase)
               && bool.TryParse(enabled, out var parsed)
               && parsed;
    }

    private static string GetValue(
        IConfigurationSection section,
        string key,
        string fallback)
    {
        return string.IsNullOrWhiteSpace(section[key])
            ? fallback
            : section[key]!.Trim();
    }

    private static bool GetBool(
        IConfigurationSection section,
        string key,
        bool fallback)
    {
        return bool.TryParse(section[key], out var parsed)
            ? parsed
            : fallback;
    }

    private static int GetInt(
        IConfigurationSection section,
        string key,
        int fallback)
    {
        return int.TryParse(section[key], out var parsed) && parsed > 0
            ? parsed
            : fallback;
    }
}

internal sealed record CloudReadOnlyBusinessDatabaseOptions(
    bool Enabled,
    string DatabaseName,
    string Description,
    string? ConnectionString,
    bool ReadOnlyCredentialVerified,
    int DefaultQueryLimit,
    int MaxQueryLimit);
