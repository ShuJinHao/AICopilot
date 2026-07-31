using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AICopilot.EntityFrameworkCore;

public static class AgentSessionStateDataProtection
{
    public const string SectionName = "AgentSessionState";
    public const string KeyPathConfigurationName = "DataProtectionKeyPath";
    public const string ApplicationName = "AICopilot.AgentSessions";
    public const string ProductionKeyPath = "/var/lib/aicopilot/data-protection-keys";

    public static string? GetConfiguredKeyPath(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var configured = configuration[$"{SectionName}:{KeyPathConfigurationName}"];
        return string.IsNullOrWhiteSpace(configured)
            ? null
            : Path.GetFullPath(configured.Trim());
    }

    public static void Configure(
        IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var builder = services
            .AddDataProtection()
            .SetApplicationName(ApplicationName);
        var keyPath = GetConfiguredKeyPath(configuration);
        if (keyPath is not null)
        {
            builder.PersistKeysToFileSystem(new DirectoryInfo(keyPath));
        }
    }
}
