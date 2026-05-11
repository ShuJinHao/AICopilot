using AICopilot.Services.Contracts;

namespace AICopilot.EntityFrameworkCore.ExternalIdentities;

public sealed class ExternalIdentityBinding
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public ApplicationUser? User { get; set; }

    public string Provider { get; set; } = string.Empty;

    public string TenantId { get; set; } = string.Empty;

    public string ExternalUserId { get; set; } = string.Empty;

    public string? EmployeeId { get; set; }

    public string? EmployeeNo { get; set; }

    public string? DisplayNameSnapshot { get; set; }

    public string? DepartmentIdSnapshot { get; set; }

    public string? DepartmentNameSnapshot { get; set; }

    public string? StatusVersion { get; set; }

    public bool AccountEnabledSnapshot { get; set; }

    public bool EmployeeActiveSnapshot { get; set; }

    public DateTime LastLoginAtUtc { get; set; }

    public DateTime LastSyncAtUtc { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }
}
