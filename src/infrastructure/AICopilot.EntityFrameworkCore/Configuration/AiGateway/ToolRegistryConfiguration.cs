using AICopilot.Core.AiGateway.Aggregates.Tools;
using AICopilot.Core.AiGateway.Ids;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AICopilot.EntityFrameworkCore.Configuration.AiGateway;

public sealed class ToolRegistrationConfiguration : IEntityTypeConfiguration<ToolRegistration>
{
    public void Configure(EntityTypeBuilder<ToolRegistration> builder)
    {
        builder.ToTable("tool_registrations");

        builder.HasKey(tool => tool.Id);
        builder.Property(tool => tool.Id)
            .HasConversion(id => id.Value, value => new ToolRegistrationId(value))
            .HasColumnName("id");

        builder.Property(tool => tool.RowVersion).IsRowVersion();

        builder.Property(tool => tool.ToolCode)
            .IsRequired()
            .HasMaxLength(160)
            .HasColumnName("tool_code");
        builder.HasIndex(tool => tool.ToolCode).IsUnique();

        builder.Property(tool => tool.DisplayName)
            .IsRequired()
            .HasMaxLength(160)
            .HasColumnName("display_name");

        builder.Property(tool => tool.Description)
            .IsRequired()
            .HasMaxLength(1000)
            .HasColumnName("description");

        builder.Property(tool => tool.ProviderType)
            .HasConversion<string>()
            .HasMaxLength(40)
            .IsRequired()
            .HasColumnName("provider_type");

        builder.Property(tool => tool.TargetType)
            .HasConversion<string>()
            .HasMaxLength(40)
            .IsRequired()
            .HasColumnName("target_type");

        builder.Property(tool => tool.TargetName)
            .IsRequired()
            .HasMaxLength(200)
            .HasColumnName("target_name");

        builder.Property(tool => tool.InputSchemaJson)
            .IsRequired()
            .HasColumnType("jsonb")
            .HasColumnName("input_schema_json");

        builder.Property(tool => tool.OutputSchemaJson)
            .IsRequired()
            .HasColumnType("jsonb")
            .HasColumnName("output_schema_json");

        builder.Property(tool => tool.RiskLevel)
            .HasConversion<string>()
            .HasMaxLength(40)
            .IsRequired()
            .HasColumnName("risk_level");

        builder.Property(tool => tool.RequiredPermission)
            .HasMaxLength(160)
            .HasColumnName("required_permission");

        builder.Property(tool => tool.RequiresApproval)
            .IsRequired()
            .HasColumnName("requires_approval");

        builder.Property(tool => tool.IsEnabled)
            .IsRequired()
            .HasColumnName("is_enabled");

        builder.Property(tool => tool.TimeoutSeconds)
            .IsRequired()
            .HasColumnName("timeout_seconds");

        builder.Property(tool => tool.AuditLevel)
            .HasConversion<string>()
            .HasMaxLength(40)
            .IsRequired()
            .HasColumnName("audit_level");

        builder.Property(tool => tool.CreatedAt)
            .HasColumnType("timestamp with time zone")
            .HasColumnName("created_at");

        builder.Property(tool => tool.UpdatedAt)
            .HasColumnType("timestamp with time zone")
            .HasColumnName("updated_at");
    }
}

public sealed class ToolExecutionRecordConfiguration : IEntityTypeConfiguration<ToolExecutionRecord>
{
    public void Configure(EntityTypeBuilder<ToolExecutionRecord> builder)
    {
        builder.ToTable("tool_execution_records");

        builder.HasKey(record => record.Id);
        builder.Property(record => record.Id)
            .HasConversion(id => id.Value, value => new ToolExecutionRecordId(value))
            .HasColumnName("id");

        builder.Property(record => record.TaskId)
            .HasConversion(id => id.Value, value => new AgentTaskId(value))
            .IsRequired()
            .HasColumnName("task_id");

        builder.Property(record => record.StepId)
            .HasConversion(id => id.Value, value => new AgentStepId(value))
            .IsRequired()
            .HasColumnName("step_id");

        builder.Property(record => record.RunAttemptId)
            .HasConversion(
                id => id.HasValue ? id.Value.Value : (Guid?)null,
                value => value.HasValue ? new AgentTaskRunAttemptId(value.Value) : null)
            .HasColumnName("run_attempt_id");

        builder.Property(record => record.ToolCode)
            .IsRequired()
            .HasMaxLength(160)
            .HasColumnName("tool_code");

        builder.Property(record => record.InputSummary)
            .HasMaxLength(2000)
            .HasColumnName("input_summary");

        builder.Property(record => record.OutputSummary)
            .HasMaxLength(4000)
            .HasColumnName("output_summary");

        builder.Property(record => record.Status)
            .HasConversion<string>()
            .HasMaxLength(40)
            .IsRequired()
            .HasColumnName("status");

        builder.Property(record => record.StartedAt)
            .HasColumnType("timestamp with time zone")
            .HasColumnName("started_at");

        builder.Property(record => record.CompletedAt)
            .HasColumnType("timestamp with time zone")
            .HasColumnName("completed_at");

        builder.Property(record => record.DurationMs)
            .HasColumnName("duration_ms");

        builder.Property(record => record.ErrorCode)
            .HasMaxLength(120)
            .HasColumnName("error_code");

        builder.Property(record => record.ErrorMessage)
            .HasMaxLength(2000)
            .HasColumnName("error_message");

        builder.Property(record => record.ArtifactId)
            .HasMaxLength(80)
            .HasColumnName("artifact_id");

        builder.Property(record => record.AuditMetadata)
            .HasMaxLength(4000)
            .HasColumnName("audit_metadata");

        builder.HasIndex(record => record.TaskId)
            .HasDatabaseName("ix_tool_execution_records_task_id");

        builder.HasIndex(record => new { record.TaskId, record.StepId })
            .HasDatabaseName("ix_tool_execution_records_task_step");

        builder.HasIndex(record => record.RunAttemptId)
            .HasDatabaseName("ix_tool_execution_records_run_attempt_id");

        builder.HasIndex(record => record.ToolCode)
            .HasDatabaseName("ix_tool_execution_records_tool_code");
    }
}
