namespace AICopilot.Services.Contracts;

public sealed class CloudIdentityStatusOptions
{
    public const string SectionName = "CloudIdentityStatus";

    public bool Enabled { get; init; }

    public string BaseUrl { get; init; } = string.Empty;

    public string StatusEndpointPath { get; init; } = "/api/v1/ai/identity/users/{cloudUserId}/status";

    public string ServiceAccountToken { get; init; } = string.Empty;

    public int RefreshIntervalSeconds { get; init; } = 60;

    public int TimeoutSeconds { get; init; } = 5;

    public string FailureMode { get; init; } = "RejectWhenUnverified";

    public bool IsConfigured()
    {
        return Enabled;
    }

    public void EnsureValid()
    {
        EnsureValid(
            environmentName: "Production",
            cloudOidcEnabled: false,
            enabledWasExplicitlyConfigured: true);
    }

    public void EnsureValid(
        string environmentName,
        bool cloudOidcEnabled,
        bool enabledWasExplicitlyConfigured)
    {
        if (!string.Equals(environmentName, "Development", StringComparison.OrdinalIgnoreCase) &&
            cloudOidcEnabled &&
            !enabledWasExplicitlyConfigured)
        {
            throw new InvalidOperationException(
                "CloudIdentityStatus:Enabled must be explicitly configured outside Development when CloudOidc is enabled.");
        }

        EnsureRuntimeOptions();
    }

    private void EnsureRuntimeOptions()
    {
        if (!Enabled)
        {
            return;
        }

        if (!Uri.TryCreate(BaseUrl, UriKind.Absolute, out _))
        {
            throw new InvalidOperationException("CloudIdentityStatus:BaseUrl must be an absolute URL when enabled.");
        }

        if (string.IsNullOrWhiteSpace(StatusEndpointPath) ||
            !StatusEndpointPath.Contains("{cloudUserId}", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("CloudIdentityStatus:StatusEndpointPath must contain '{cloudUserId}'.");
        }

        if (string.IsNullOrWhiteSpace(ServiceAccountToken))
        {
            throw new InvalidOperationException("CloudIdentityStatus:ServiceAccountToken is required when enabled.");
        }

        if (RefreshIntervalSeconds is < 5 or > 3600)
        {
            throw new InvalidOperationException("CloudIdentityStatus:RefreshIntervalSeconds must be between 5 and 3600.");
        }

        if (TimeoutSeconds is < 1 or > 30)
        {
            throw new InvalidOperationException("CloudIdentityStatus:TimeoutSeconds must be between 1 and 30.");
        }

        if (!string.Equals(FailureMode, "RejectWhenUnverified", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("CloudIdentityStatus:FailureMode currently only supports RejectWhenUnverified.");
        }
    }
}

public sealed record CloudIdentityStatusSnapshot(
    string CloudUserId,
    string TenantId,
    bool AccountEnabled,
    bool EmployeeActive,
    string StatusVersion,
    DateTime IssuedAtUtc);

public enum CloudIdentityStatusCheckOutcome
{
    Succeeded,
    NotFound,
    Unavailable
}

public sealed record CloudIdentityStatusCheckResult(
    CloudIdentityStatusCheckOutcome Outcome,
    CloudIdentityStatusSnapshot? Status,
    string? FailureReason)
{
    public static CloudIdentityStatusCheckResult Succeeded(CloudIdentityStatusSnapshot status)
    {
        return new CloudIdentityStatusCheckResult(
            CloudIdentityStatusCheckOutcome.Succeeded,
            status,
            null);
    }

    public static CloudIdentityStatusCheckResult NotFound(string? failureReason)
    {
        return new CloudIdentityStatusCheckResult(
            CloudIdentityStatusCheckOutcome.NotFound,
            null,
            failureReason);
    }

    public static CloudIdentityStatusCheckResult Unavailable(string? failureReason)
    {
        return new CloudIdentityStatusCheckResult(
            CloudIdentityStatusCheckOutcome.Unavailable,
            null,
            failureReason);
    }
}

public interface ICloudIdentityStatusClient
{
    Task<CloudIdentityStatusCheckResult> GetStatusAsync(
        string cloudUserId,
        string tenantId,
        CancellationToken cancellationToken = default);
}

public sealed record CloudIdentityStatusValidationResult(
    bool IsValid,
    string? FailureCode,
    string? FailureMessage)
{
    public static CloudIdentityStatusValidationResult Valid()
    {
        return new CloudIdentityStatusValidationResult(true, null, null);
    }

    public static CloudIdentityStatusValidationResult Failure(string failureCode, string failureMessage)
    {
        return new CloudIdentityStatusValidationResult(false, failureCode, failureMessage);
    }
}

public interface ICloudIdentityStatusValidator
{
    Task<CloudIdentityStatusValidationResult> ValidateAsync(
        ApplicationUser user,
        System.Security.Claims.ClaimsPrincipal principal,
        CancellationToken cancellationToken = default);
}
