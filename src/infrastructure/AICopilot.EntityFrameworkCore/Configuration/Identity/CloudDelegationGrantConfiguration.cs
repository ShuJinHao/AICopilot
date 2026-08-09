using AICopilot.EntityFrameworkCore.CloudDelegations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AICopilot.EntityFrameworkCore.Configuration.Identity;

public sealed class CloudDelegationGrantConfiguration
    : IEntityTypeConfiguration<CloudDelegationGrant>
{
    public void Configure(EntityTypeBuilder<CloudDelegationGrant> builder)
    {
        builder.ToTable("cloud_delegation_grants", "identity");
        builder.HasKey(grant => grant.GrantId);
        builder.Property(grant => grant.Issuer).HasMaxLength(512).IsRequired();
        builder.Property(grant => grant.TenantId).HasMaxLength(128).IsRequired();
        builder.Property(grant => grant.ProtectedToken);
        builder.Property(grant => grant.IssuedStatusVersion).HasMaxLength(256).IsRequired();

        builder.HasOne(grant => grant.AiUser)
            .WithMany()
            .HasForeignKey(grant => grant.AiUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(grant => new { grant.AiUserId, grant.ExpiresAtUtc });
        builder.HasIndex(grant => new { grant.CloudUserId, grant.ExpiresAtUtc });
        builder.HasIndex(grant => grant.ExpiresAtUtc);
        builder.HasIndex(grant => grant.RevokedAtUtc);
    }
}
