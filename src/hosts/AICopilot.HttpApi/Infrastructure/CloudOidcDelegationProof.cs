using System.Text.Json;
using AICopilot.Services.Contracts;
using Microsoft.AspNetCore.Authentication;

namespace AICopilot.HttpApi.Infrastructure;

internal static class CloudOidcDelegationProof
{
    private const string UserInfoSubjectKey = "AICopilot.CloudOidc.UserInfoSubject";
    private const string UserInfoTenantKey = "AICopilot.CloudOidc.UserInfoTenant";
    private const string GrantedScopesKey = "AICopilot.CloudOidc.GrantedScopes";

    public static void CaptureGrantedScopes(
        AuthenticationProperties properties,
        string? grantedScope,
        IReadOnlyCollection<string> requestedScopes)
    {
        ArgumentNullException.ThrowIfNull(properties);
        ArgumentNullException.ThrowIfNull(requestedScopes);

        // RFC 6749 permits the token response to omit scope when it is identical
        // to the authorization request. Persist the effective protocol result in
        // the encrypted, short-lived external cookie either way.
        var effectiveScope = string.IsNullOrWhiteSpace(grantedScope)
            ? string.Join(' ', requestedScopes)
            : grantedScope;
        properties.Items[GrantedScopesKey] = effectiveScope;
    }

    public static bool TryCaptureUserInfo(
        JsonDocument userInfo,
        AuthenticationProperties properties,
        out string error)
    {
        ArgumentNullException.ThrowIfNull(userInfo);
        ArgumentNullException.ThrowIfNull(properties);

        if (!TryReadUniqueString(userInfo.RootElement, "sub", out var subject))
        {
            error = "Cloud OIDC userinfo did not return one valid subject for the delegated access token.";
            return false;
        }

        var tenantId = TryReadUniqueString(userInfo.RootElement, "tenant_id", out var tenant)
            ? tenant
            : CloudOidcIdentityProfile.DefaultTenantId;
        properties.Items[UserInfoSubjectKey] = subject;
        properties.Items[UserInfoTenantKey] = tenantId;
        error = string.Empty;
        return true;
    }

    public static bool TryCreateEvidence(
        AuthenticationProperties properties,
        CloudOidcOptions options,
        CloudDelegationTokenContractProof contractProof,
        out CloudDelegationTokenEvidence evidence)
    {
        ArgumentNullException.ThrowIfNull(properties);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(contractProof);

        properties.Items.TryGetValue(UserInfoSubjectKey, out var subject);
        properties.Items.TryGetValue(UserInfoTenantKey, out var tenantId);
        properties.Items.TryGetValue(GrantedScopesKey, out var grantedScopes);
        var scopes = (grantedScopes ?? string.Empty)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (string.IsNullOrWhiteSpace(subject) ||
            string.IsNullOrWhiteSpace(tenantId) ||
            scopes.Length == 0 ||
            !string.Equals(
                contractProof.Audience,
                CloudDelegationDefaults.Audience,
                StringComparison.Ordinal) ||
            !string.Equals(
                contractProof.Actor,
                CloudDelegationDefaults.Actor,
                StringComparison.Ordinal))
        {
            evidence = default!;
            return false;
        }

        evidence = new CloudDelegationTokenEvidence(
            options.Issuer.TrimEnd('/'),
            subject,
            subject,
            tenantId,
            contractProof.Audience,
            contractProof.Actor,
            scopes);
        return true;
    }

    private static bool TryReadUniqueString(
        JsonElement root,
        string propertyName,
        out string value)
    {
        value = string.Empty;
        if (root.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        var matches = root.EnumerateObject()
            .Where(property => string.Equals(property.Name, propertyName, StringComparison.Ordinal))
            .ToArray();
        if (matches.Length != 1 || matches[0].Value.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        value = matches[0].Value.GetString()?.Trim() ?? string.Empty;
        return !string.IsNullOrWhiteSpace(value);
    }
}
