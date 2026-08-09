using AICopilot.Core.DataAnalysis.Aggregates.BusinessDatabase;
using AICopilot.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.EntityFrameworkCore;

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
        ValidateOptions(configuration, options);

        var persistedCloudSources = await dataAnalysisDbContext.BusinessDatabases
            .Where(database =>
                database.ExternalSystemType == BusinessDataExternalSystemType.CloudReadOnly)
            .ToListAsync(cancellationToken);
        if (persistedCloudSources.Count == 0)
        {
            return;
        }

        foreach (var database in persistedCloudSources)
        {
            database.RetireAndClearConnectionMaterial();
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

        throw new InvalidOperationException(
            "DataAnalysis CloudReadOnly direct database mode is temporarily closed and cannot be enabled by configuration.");
    }

    private static string? GetConnectionString(
        IConfiguration configuration,
        IConfigurationSection section)
    {
        return section["ConnectionString"]
               ?? configuration.GetConnectionString("cloud-platform-readonly")
               ?? configuration["DATA_ANALYSIS_CLOUD_READONLY_CONNECTION_STRING"];
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
