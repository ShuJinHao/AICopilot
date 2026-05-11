using AICopilot.EntityFrameworkCore.ExternalIdentities;
using AICopilot.Services.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AICopilot.EntityFrameworkCore.Configuration.Identity;

public sealed class ExternalIdentityBindingConfiguration : IEntityTypeConfiguration<ExternalIdentityBinding>
{
    public void Configure(EntityTypeBuilder<ExternalIdentityBinding> builder)
    {
        builder.ToTable("external_identity_bindings", "identity");
        builder.HasKey(binding => binding.Id);

        builder.Property(binding => binding.Provider).HasMaxLength(64).IsRequired();
        builder.Property(binding => binding.TenantId).HasMaxLength(128).IsRequired();
        builder.Property(binding => binding.ExternalUserId).HasMaxLength(128).IsRequired();
        builder.Property(binding => binding.EmployeeId).HasMaxLength(128);
        builder.Property(binding => binding.EmployeeNo).HasMaxLength(128);
        builder.Property(binding => binding.DisplayNameSnapshot).HasMaxLength(256);
        builder.Property(binding => binding.DepartmentIdSnapshot).HasMaxLength(128);
        builder.Property(binding => binding.DepartmentNameSnapshot).HasMaxLength(256);
        builder.Property(binding => binding.StatusVersion).HasMaxLength(256);

        builder
            .HasOne(binding => binding.User)
            .WithMany()
            .HasForeignKey(binding => binding.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder
            .HasIndex(binding => new { binding.Provider, binding.TenantId, binding.ExternalUserId })
            .IsUnique();

        builder
            .HasIndex(binding => new { binding.UserId, binding.Provider })
            .IsUnique();
    }
}
