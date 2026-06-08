using System.Net;

namespace AICopilot.HttpApi.Infrastructure;

public sealed class CloudOidcOptions
{
    public const string SectionName = "CloudOidc";
    public const string AllowIntranetHttpOidcEnvironmentVariable = "ALLOW_INTRANET_HTTP_OIDC";
    private const string HostCookiePrefix = "__Host-";

    public bool Enabled { get; init; }

    public string Issuer { get; init; } = string.Empty;

    public bool AllowIntranetHttpOidc { get; init; }

    public string ClientId { get; init; } = "aicopilot";

    public string CallbackPath { get; init; } = "/api/identity/cloud-oidc/callback";

    public string FrontendCompletionPath { get; init; } = "/cloud-login/complete";

    public bool RequireHttpsMetadata { get; init; } = true;

    public int ExternalCookieLifetimeMinutes { get; init; } = 5;

    public string ExternalCookieName { get; init; } = "__Host-AICopilot-CloudOidc-External";

    public string[] Scopes { get; init; } = ["openid", "profile"];

    public bool IsConfigured()
    {
        return Enabled
            && !string.IsNullOrWhiteSpace(Issuer)
            && !string.IsNullOrWhiteSpace(ClientId)
            && !string.IsNullOrWhiteSpace(CallbackPath)
            && !string.IsNullOrWhiteSpace(FrontendCompletionPath);
    }

    public void EnsureValid()
    {
        EnsureValid("Production");
    }

    public void EnsureValid(string environmentName)
    {
        if (!Enabled)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(Issuer))
        {
            throw new InvalidOperationException("CloudOidc:Issuer is required when CloudOidc is enabled.");
        }

        if (string.IsNullOrWhiteSpace(ClientId))
        {
            throw new InvalidOperationException("CloudOidc:ClientId is required when CloudOidc is enabled.");
        }

        if (string.IsNullOrWhiteSpace(CallbackPath))
        {
            throw new InvalidOperationException("CloudOidc:CallbackPath is required when CloudOidc is enabled.");
        }

        if (string.IsNullOrWhiteSpace(FrontendCompletionPath))
        {
            throw new InvalidOperationException("CloudOidc:FrontendCompletionPath is required when CloudOidc is enabled.");
        }

        var issuer = ParseHttpUri(Issuer);
        var isDevelopment = string.Equals(environmentName, "Development", StringComparison.OrdinalIgnoreCase);
        if (issuer.Scheme == Uri.UriSchemeHttps)
        {
            if (!RequireHttpsMetadata)
            {
                throw new InvalidOperationException(
                    "CloudOidc:RequireHttpsMetadata must be true when CloudOidc:Issuer uses HTTPS.");
            }

            return;
        }

        if (isDevelopment && issuer.IsLoopback)
        {
            if (RequireHttpsMetadata)
            {
                throw new InvalidOperationException(
                    "CloudOidc:RequireHttpsMetadata must be false when Development uses an HTTP loopback issuer.");
            }

            return;
        }

        if (AllowIntranetHttpOidc && IsAllowedIntranetHttpHost(issuer))
        {
            return;
        }

        throw new InvalidOperationException(
            "CloudOidc:Issuer must use HTTPS outside Development loopback endpoints or explicit intranet OIDC endpoints.");
    }

    public string GetEffectiveExternalCookieName()
    {
        var cookieName = ExternalCookieName.Trim();
        return AllowIntranetHttpOidc && cookieName.StartsWith(HostCookiePrefix, StringComparison.Ordinal)
            ? cookieName[HostCookiePrefix.Length..]
            : cookieName;
    }

    public bool GetEffectiveRequireHttpsMetadata()
    {
        var issuer = ParseHttpUri(Issuer);
        return AllowIntranetHttpOidc && issuer.Scheme == Uri.UriSchemeHttp && IsAllowedIntranetHttpHost(issuer)
            ? false
            : RequireHttpsMetadata;
    }

    private static Uri ParseHttpUri(string issuer)
    {
        if (!Uri.TryCreate(issuer, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            throw new InvalidOperationException("CloudOidc:Issuer must be an absolute http/https URI.");
        }

        return uri;
    }

    private static bool IsAllowedIntranetHttpHost(Uri uri)
    {
        if (uri.IsLoopback)
        {
            return true;
        }

        if (!IPAddress.TryParse(uri.Host, out var address))
        {
            return false;
        }

        var bytes = address.GetAddressBytes();
        return bytes.Length == 4 &&
            (bytes[0] == 10 ||
             (bytes[0] == 192 && bytes[1] == 168) ||
             (bytes[0] == 172 && bytes[1] is >= 16 and <= 31));
    }
}
