using AICopilot.Services.Contracts;

namespace AICopilot.EntityFrameworkCore.CloudDelegations;

public sealed class CloudDelegationGrant
{
    public Guid GrantId { get; set; }

    public Guid AiUserId { get; set; }

    public ApplicationUser? AiUser { get; set; }

    public Guid CloudUserId { get; set; }

    public string Issuer { get; set; } = string.Empty;

    public string TenantId { get; set; } = string.Empty;

    public string? ProtectedToken { get; set; }

    public DateTime ExpiresAtUtc { get; set; }

    public string IssuedStatusVersion { get; set; } = string.Empty;

    public DateTime? RevokedAtUtc { get; set; }

    public DateTime CreatedAtUtc { get; set; }
}
