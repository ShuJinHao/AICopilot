using AICopilot.Core.AiGateway.Aggregates.ApprovalPolicy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AICopilot.EntityFrameworkCore.Configuration.AiGateway;

public class ApprovalPolicyConfiguration : IEntityTypeConfiguration<ApprovalPolicy>
{
    public void Configure(EntityTypeBuilder<ApprovalPolicy> builder)
    {
        builder.ToTable("approval_policies");

        builder.HasKey(policy => policy.Id);
        builder.Property(policy => policy.Id).HasColumnName("id");

        builder.Property(policy => policy.RowVersion).IsRowVersion();

        builder.Property(policy => policy.Name)
            .IsRequired()
            .HasMaxLength(200)
            .HasColumnName("name");

        builder.HasIndex(policy => policy.Name)
            .IsUnique();

        builder.Property(policy => policy.Description)
            .HasMaxLength(1000)
            .HasColumnName("description");

        builder.Property(policy => policy.TargetType)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(50)
            .HasColumnName("target_type");

        builder.Property(policy => policy.TargetName)
            .IsRequired()
            .HasMaxLength(200)
            .HasColumnName("target_name");

        builder.Property(policy => policy.IsEnabled)
            .IsRequired()
            .HasColumnName("is_enabled");

        builder.Property(policy => policy.RequiresOnsiteAttestation)
            .IsRequired()
            .HasColumnName("requires_onsite_attestation");

        builder.PrimitiveCollection(policy => policy.ToolNames)
            .IsRequired()
            .HasColumnName("tool_names");
    }
}
