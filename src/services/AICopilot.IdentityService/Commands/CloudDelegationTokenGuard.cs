using AICopilot.Services.Contracts;

namespace AICopilot.IdentityService.Commands;

internal sealed record ValidatedCloudDelegationToken(
    Guid CloudUserId,
    string AccessToken,
    DateTime ExpiresAtUtc);

internal static class CloudDelegationTokenGuard
{
    public static ValidatedCloudDelegationToken Validate(
        CloudOidcIdentityProfile profile,
        CloudDelegationTokenInput token,
        DateTime utcNow)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(token);

        var evidence = token.Evidence;
        var requiredScopeCount = evidence?.Scopes?.Count(scope =>
            string.Equals(scope?.Trim(), CloudDelegationDefaults.Scope, StringComparison.Ordinal)) ?? 0;
        if (!Guid.TryParse(profile.Subject, out var cloudUserId) ||
            cloudUserId == Guid.Empty ||
            string.IsNullOrWhiteSpace(profile.StatusVersion) ||
            string.IsNullOrWhiteSpace(token.AccessToken) ||
            token.ExpiresAtUtc <= utcNow ||
            evidence is null ||
            !SameIssuer(evidence.Issuer, profile.Issuer) ||
            !string.Equals(evidence.Subject?.Trim(), profile.Subject, StringComparison.Ordinal) ||
            !string.Equals(evidence.DelegatedUserId?.Trim(), profile.Subject, StringComparison.Ordinal) ||
            !string.Equals(evidence.TenantId?.Trim(), profile.TenantId, StringComparison.Ordinal) ||
            !string.Equals(evidence.Audience?.Trim(), CloudDelegationDefaults.Audience, StringComparison.Ordinal) ||
            !string.Equals(evidence.Actor?.Trim(), CloudDelegationDefaults.Actor, StringComparison.Ordinal) ||
            requiredScopeCount != 1)
        {
            throw new InvalidOperationException(
                "Cloud OIDC delegation evidence does not match the current Cloud identity or the fixed AiRead contract.");
        }

        var maximumExpiry = utcNow.AddMinutes(CloudDelegationDefaults.LifetimeMinutes);
        var effectiveExpiry = token.ExpiresAtUtc <= maximumExpiry
            ? token.ExpiresAtUtc
            : maximumExpiry;

        return new ValidatedCloudDelegationToken(
            cloudUserId,
            token.AccessToken,
            effectiveExpiry);
    }

    private static bool SameIssuer(string? left, string? right)
    {
        return !string.IsNullOrWhiteSpace(left) &&
               !string.IsNullOrWhiteSpace(right) &&
               string.Equals(
                   left.Trim().TrimEnd('/'),
                   right.Trim().TrimEnd('/'),
                   StringComparison.Ordinal);
    }
}
