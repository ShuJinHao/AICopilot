using AICopilot.EntityFrameworkCore.AuditLogs;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AICopilot.EntityFrameworkCore.Configuration.Audit;

public sealed class AuditLogEntryConfiguration : IEntityTypeConfiguration<AuditLogEntry>
{
    public void Configure(EntityTypeBuilder<AuditLogEntry> builder)
    {
        builder.ToTable("audit_logs");

        builder.HasKey(entry => entry.Id);

        builder.Property(entry => entry.Id)
            .HasColumnName("id");

        builder.Property(entry => entry.ActionGroup)
            .HasColumnName("action_group")
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(entry => entry.ActionCode)
            .HasColumnName("action_code")
            .HasMaxLength(128)
            .IsRequired();

        builder.Property(entry => entry.TargetType)
            .HasColumnName("target_type")
            .HasMaxLength(128)
            .IsRequired();

        builder.Property(entry => entry.TargetId)
            .HasColumnName("target_id")
            .HasMaxLength(128);

        builder.Property(entry => entry.TargetName)
            .HasColumnName("target_name")
            .HasMaxLength(256)
            .IsRequired();

        builder.Property(entry => entry.OperatorUserId)
            .HasColumnName("operator_user_id")
            .HasMaxLength(128);

        builder.Property(entry => entry.OperatorUserName)
            .HasColumnName("operator_user_name")
            .HasMaxLength(256)
            .IsRequired();

        builder.Property(entry => entry.OperatorRoleName)
            .HasColumnName("operator_role_name")
            .HasMaxLength(128);

        builder.Property(entry => entry.Result)
            .HasColumnName("result")
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(entry => entry.Summary)
            .HasColumnName("summary")
            .HasMaxLength(1000)
            .IsRequired();

        builder.Property(entry => entry.ChangedFields)
            .HasColumnName("changed_fields")
            .HasColumnType("text[]")
            .IsRequired();

        builder.Property(entry => entry.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        builder.HasIndex(entry => entry.CreatedAt)
            .HasDatabaseName("ix_audit_logs_created_at");

        builder.HasIndex(entry => new { entry.ActionGroup, entry.CreatedAt })
            .HasDatabaseName("ix_audit_logs_action_group_created_at");
    }
}
