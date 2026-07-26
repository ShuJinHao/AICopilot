using AICopilot.HttpApi.Models;

namespace AICopilot.HttpApi.Infrastructure;

public static class SystemBuildIdentityResolver
{
    public const string SchemaVersion = "aicopilot-build-identity-v1";
    public const string ServiceName = "AICopilot.HttpApi";
    public const string SourceCommitConfigurationKey = "AICOPILOT_SOURCE_SHA";
    public const string ReleaseTagConfigurationKey = "AICOPILOT_RELEASE_TAG";

    public static SystemBuildIdentityResponse Resolve(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        return Resolve(
            configuration[SourceCommitConfigurationKey],
            configuration[ReleaseTagConfigurationKey]);
    }

    public static SystemBuildIdentityResponse Resolve(string? sourceCommit, string? releaseTag)
    {
        var normalizedCommit = sourceCommit?.Trim().ToLowerInvariant();
        var normalizedTag = releaseTag?.Trim().ToLowerInvariant();

        if (!IsFullGitCommit(normalizedCommit) ||
            !string.Equals(normalizedTag, $"sha-{normalizedCommit}", StringComparison.Ordinal))
        {
            return new SystemBuildIdentityResponse(
                SchemaVersion,
                ServiceName,
                null,
                null,
                false);
        }

        return new SystemBuildIdentityResponse(
            SchemaVersion,
            ServiceName,
            normalizedTag,
            normalizedCommit,
            true);
    }

    private static bool IsFullGitCommit(string? value)
    {
        return value is { Length: 40 } &&
               value.All(character =>
                   character is >= '0' and <= '9' or >= 'a' and <= 'f');
    }
}
