namespace AICopilot.Services.Contracts;

public static class ExternalIdentityProviders
{
    public const string Cloud = "Cloud";
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
