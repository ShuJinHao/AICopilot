namespace AICopilot.Services.Contracts;

public static class ExternalIdentityProviders
{
    public const string Cloud = "Cloud";
}

public static class CloudDelegationDefaults
{
    public const string Scope = "iiot.ai.read";
    public const string Audience = "iiot-cloud-ai-read";
    public const string Actor = "ai-delegated-user";
    public const int LifetimeMinutes = 30;
}

public static class ExternalIdentityJwtClaimTypes
{
    public const string IdentityProvider = "identity_provider";
    public const string CloudIssuer = "cloud_issuer";
    public const string CloudTenantId = "cloud_tenant_id";
    public const string CloudUserId = "cloud_user_id";
    public const string CloudEmployeeId = "cloud_employee_id";
    public const string CloudEmployeeNo = "cloud_employee_no";
    public const string CloudDepartmentId = "cloud_department_id";
    public const string CloudDepartmentName = "cloud_department_name";
    public const string CloudStatusVersion = "cloud_status_version";
    public const string CloudDelegationId = "cloud_delegation_id";
}

public sealed record CloudOidcIdentityProfile(
    string Issuer,
    string Subject,
    string TenantId,
    string PreferredUserName,
    string? DisplayName,
    string? EmployeeId,
    string? EmployeeNo,
    string? DepartmentId,
    string? DepartmentName,
    string? StatusVersion,
    bool AccountEnabled,
    bool EmployeeActive)
{
    public const string DefaultTenantId = "default";
}

public sealed class CloudOidcCanonicalAdminOptions
{
    public const string SectionName = "CloudOidc";
    public const string RequiredEmployeeNo = "101650";
    public const string EmergencyAdminConflictReasonCode =
        "EMERGENCY_ADMIN_CANONICAL_CLOUD_ADMIN_CONFLICT";

    public string CanonicalAdminEmployeeNo { get; init; } = RequiredEmployeeNo;

    public void EnsureValid()
    {
        if (!string.Equals(
                CanonicalAdminEmployeeNo?.Trim(),
                RequiredEmployeeNo,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"CloudOidc:CanonicalAdminEmployeeNo must be the fixed non-secret business value {RequiredEmployeeNo}.");
        }
    }
}

public sealed record CloudDelegationTokenEvidence(
    string Issuer,
    string Subject,
    string DelegatedUserId,
    string TenantId,
    string Audience,
    string Actor,
    IReadOnlyList<string> Scopes);

public sealed record CloudDelegationTokenInput(
    string AccessToken,
    DateTime ExpiresAtUtc,
    CloudDelegationTokenEvidence Evidence);

public sealed record CreateCloudDelegationGrantRequest(
    Guid GrantId,
    Guid AiUserId,
    Guid CloudUserId,
    string Issuer,
    string TenantId,
    string AccessToken,
    DateTime ExpiresAtUtc,
    string IssuedStatusVersion,
    DateTime CreatedAtUtc);

public sealed record CloudDelegationGrantSnapshot(
    Guid GrantId,
    Guid AiUserId,
    Guid CloudUserId,
    string Issuer,
    string TenantId,
    DateTime ExpiresAtUtc,
    string IssuedStatusVersion,
    DateTime? RevokedAtUtc,
    DateTime CreatedAtUtc);

public sealed record CloudDelegationAccessToken(
    Guid GrantId,
    Guid AiUserId,
    Guid CloudUserId,
    string AccessToken,
    DateTime ExpiresAtUtc);

public sealed record CloudDelegationRevocationResult(
    bool Found,
    bool AlreadyRevoked);

public sealed record CloudDelegationPurgeResult(
    bool LockAcquired,
    int ClearedTokenCount,
    int DeletedMetadataCount);

public interface ICloudDelegationGrantStore
{
    Task<CloudDelegationGrantSnapshot> CreateAsync(
        CreateCloudDelegationGrantRequest request,
        CancellationToken cancellationToken = default);

    Task<CloudDelegationAccessToken?> ResolveAsync(
        Guid grantId,
        Guid aiUserId,
        DateTime utcNow,
        CancellationToken cancellationToken = default);

    Task<CloudDelegationRevocationResult> RevokeCurrentAsync(
        Guid grantId,
        Guid aiUserId,
        DateTime utcNow,
        CancellationToken cancellationToken = default);

    Task<CloudDelegationPurgeResult> PurgeExpiredAsync(
        DateTime utcNow,
        int batchSize,
        CancellationToken cancellationToken = default);
}

public interface ICloudDelegationAccessTokenProvider
{
    Task<string> GetCurrentTokenAsync(CancellationToken cancellationToken = default);
}

public sealed record ExternalIdentityBindingSnapshot(
    Guid Id,
    Guid UserId,
    string Provider,
    string TenantId,
    string ExternalUserId,
    string? EmployeeId,
    string? EmployeeNo,
    string? DisplayNameSnapshot,
    string? DepartmentIdSnapshot,
    string? DepartmentNameSnapshot,
    string? StatusVersion,
    bool AccountEnabledSnapshot,
    bool EmployeeActiveSnapshot,
    DateTime LastLoginAtUtc,
    DateTime LastSyncAtUtc);

public sealed record CreateExternalIdentityBindingRequest(
    Guid UserId,
    string Provider,
    string TenantId,
    string ExternalUserId,
    string? EmployeeId,
    string? EmployeeNo,
    string? DisplayNameSnapshot,
    string? DepartmentIdSnapshot,
    string? DepartmentNameSnapshot,
    string? StatusVersion,
    bool AccountEnabledSnapshot,
    bool EmployeeActiveSnapshot,
    DateTime NowUtc);

public sealed record UpdateExternalIdentityBindingSnapshotRequest(
    Guid BindingId,
    string? EmployeeId,
    string? EmployeeNo,
    string? DisplayNameSnapshot,
    string? DepartmentIdSnapshot,
    string? DepartmentNameSnapshot,
    string? StatusVersion,
    bool AccountEnabledSnapshot,
    bool EmployeeActiveSnapshot,
    DateTime NowUtc);

public interface IExternalIdentityBindingStore
{
    Task<ExternalIdentityBindingSnapshot?> FindByExternalIdentityAsync(
        string provider,
        string tenantId,
        string externalUserId,
        CancellationToken cancellationToken = default);

    Task<ExternalIdentityBindingSnapshot?> FindByUserProviderAsync(
        Guid userId,
        string provider,
        CancellationToken cancellationToken = default);

    Task<ExternalIdentityBindingSnapshot> CreateAsync(
        CreateExternalIdentityBindingRequest request,
        CancellationToken cancellationToken = default);

    Task UpdateSnapshotAsync(
        UpdateExternalIdentityBindingSnapshotRequest request,
        CancellationToken cancellationToken = default);
}

public interface IIdentityUserFreshReadStore
{
    Task<ApplicationUser?> FindByNormalizedUserNameAsync(
        string normalizedUserName,
        CancellationToken cancellationToken = default);

    Task<ApplicationUser?> FindByIdAsync(
        Guid userId,
        CancellationToken cancellationToken = default);

    Task<ApplicationUser?> InitializeSecurityStampIfMissingAsync(
        Guid userId,
        string securityStamp,
        string concurrencyStamp,
        CancellationToken cancellationToken = default);
}

public interface IExternalIdentityBindingInvariantGuard
{
    Task AcquireAsync(
        ExternalIdentityBindingInvariantScope scope,
        CancellationToken cancellationToken = default);
}

public sealed record ExternalIdentityBindingInvariantScope(
    string Provider,
    string TenantId,
    string ExternalUserId,
    string NormalizedUserName,
    IReadOnlyCollection<Guid> KnownUserIds);

public enum ExternalIdentityInvariantConflictKind
{
    NormalizedUserName,
    ExternalIdentity,
    UserProvider
}

public sealed class ExternalIdentityInvariantConflictException(
    ExternalIdentityInvariantConflictKind conflictKind,
    Exception innerException)
    : Exception("A known external identity uniqueness invariant was violated.", innerException)
{
    public ExternalIdentityInvariantConflictKind ConflictKind { get; } = conflictKind;
}
