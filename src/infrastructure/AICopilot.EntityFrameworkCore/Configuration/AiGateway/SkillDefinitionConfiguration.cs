using AICopilot.Core.AiGateway.Aggregates.Skills;
using AICopilot.Core.AiGateway.Ids;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AICopilot.EntityFrameworkCore.Configuration.AiGateway;

public sealed class SkillDefinitionConfiguration : IEntityTypeConfiguration<SkillDefinition>
{
    public void Configure(EntityTypeBuilder<SkillDefinition> builder)
    {
        builder.ToTable("skill_definitions");

        builder.HasKey(skill => skill.Id);
        builder.Property(skill => skill.Id)
            .HasConversion(id => id.Value, value => new SkillDefinitionId(value))
            .HasColumnName("id");

        builder.Property(skill => skill.RowVersion).IsRowVersion();

        builder.Property(skill => skill.SkillCode)
            .IsRequired()
            .HasMaxLength(120)
            .HasColumnName("skill_code");
        builder.HasIndex(skill => skill.SkillCode).IsUnique();

        builder.Property(skill => skill.DisplayName)
            .IsRequired()
            .HasMaxLength(160)
            .HasColumnName("display_name");

        builder.Property(skill => skill.Description)
            .IsRequired()
            .HasMaxLength(1000)
            .HasColumnName("description");

        builder.Property(skill => skill.AllowedToolCodes)
            .HasColumnType("text[]")
            .IsRequired()
            .HasColumnName("allowed_tool_codes");

        builder.Property(skill => skill.RiskLevel)
            .HasConversion<string>()
            .HasMaxLength(40)
            .IsRequired()
            .HasColumnName("risk_level");

        builder.Property(skill => skill.ApprovalPolicy)
            .IsRequired()
            .HasMaxLength(120)
            .HasColumnName("approval_policy");

        builder.Property(skill => skill.AllowedDataSourceModes)
            .HasColumnType("text[]")
            .IsRequired()
            .HasColumnName("allowed_data_source_modes");

        builder.Property(skill => skill.AllowedKnowledgeScopes)
            .HasColumnType("text[]")
            .IsRequired()
            .HasColumnName("allowed_knowledge_scopes");

        builder.Property(skill => skill.OutputComponentTypes)
            .HasColumnType("text[]")
            .IsRequired()
            .HasColumnName("output_component_types");

        builder.Property(skill => skill.IsEnabled)
            .IsRequired()
            .HasColumnName("is_enabled");

        builder.Property(skill => skill.IsBuiltIn)
            .IsRequired()
            .HasColumnName("is_built_in");

        builder.Property(skill => skill.Version)
            .IsRequired()
            .HasColumnName("version");

        builder.Property(skill => skill.CreatedAt)
            .HasColumnType("timestamp with time zone")
            .HasColumnName("created_at");

        builder.Property(skill => skill.UpdatedAt)
            .HasColumnType("timestamp with time zone")
            .HasColumnName("updated_at");
    }
}
