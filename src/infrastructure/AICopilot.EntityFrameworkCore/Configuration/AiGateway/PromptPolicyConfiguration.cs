using AICopilot.Core.AiGateway.Aggregates.PromptPolicy;
using AICopilot.Core.AiGateway.Ids;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AICopilot.EntityFrameworkCore.Configuration.AiGateway;

public sealed class PromptPolicyConfiguration : IEntityTypeConfiguration<PromptPolicy>
{
    public void Configure(EntityTypeBuilder<PromptPolicy> builder)
    {
        builder.ToTable("prompt_policies");

        builder.HasKey(policy => policy.Id);
        builder.Property(policy => policy.Id)
            .HasConversion(id => id.Value, value => new PromptPolicyId(value))
            .HasColumnName("id");

        builder.Property(policy => policy.RowVersion).IsRowVersion();

        builder.Property(policy => policy.Code)
            .IsRequired()
            .HasMaxLength(100)
            .HasColumnName("code");
        builder.HasIndex(policy => policy.Code).IsUnique();

        builder.Property(policy => policy.Name)
            .IsRequired()
            .HasMaxLength(200)
            .HasColumnName("name");

        builder.Property(policy => policy.Usage)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(50)
            .HasColumnName("usage");

        builder.Property(policy => policy.IsEnabled)
            .IsRequired()
            .HasColumnName("is_enabled");

        builder.Property(policy => policy.ActiveVersionNo)
            .HasColumnName("active_version_no");

        builder.Property(policy => policy.CreatedAt)
            .IsRequired()
            .HasColumnName("created_at");

        builder.Property(policy => policy.UpdatedAt)
            .IsRequired()
            .HasColumnName("updated_at");

        builder.OwnsMany(
            policy => policy.Versions,
            versionBuilder =>
            {
                versionBuilder.ToTable("prompt_policy_versions");
                versionBuilder.WithOwner().HasForeignKey("prompt_policy_id");
                versionBuilder.HasKey("Id");

                versionBuilder.Property(version => version.Id)
                    .HasColumnName("id");

                versionBuilder.Property(version => version.VersionNo)
                    .IsRequired()
                    .HasColumnName("version_no");

                versionBuilder.Property(version => version.SystemPrompt)
                    .IsRequired()
                    .HasMaxLength(12000)
                    .HasColumnName("system_prompt");

                versionBuilder.Property(version => version.SafetyConstraints)
                    .HasMaxLength(12000)
                    .HasColumnName("safety_constraints");

                versionBuilder.Property(version => version.ContextInjectionRules)
                    .HasMaxLength(12000)
                    .HasColumnName("context_injection_rules");

                versionBuilder.Property(version => version.OutputFormat)
                    .HasMaxLength(12000)
                    .HasColumnName("output_format");

                versionBuilder.Property(version => version.IsEnabled)
                    .IsRequired()
                    .HasColumnName("is_enabled");

                versionBuilder.Property(version => version.CreatedAt)
                    .IsRequired()
                    .HasColumnName("created_at");

                versionBuilder.HasIndex("prompt_policy_id", nameof(PromptPolicyVersion.VersionNo))
                    .IsUnique();
            });

        builder.Navigation(policy => policy.Versions)
            .UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
