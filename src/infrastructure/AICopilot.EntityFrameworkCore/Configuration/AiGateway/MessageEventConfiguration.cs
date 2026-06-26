using AICopilot.Core.AiGateway.Aggregates.AgentTasks;
using AICopilot.Core.AiGateway.Aggregates.Approvals;
using AICopilot.Core.AiGateway.Aggregates.Artifacts;
using AICopilot.Core.AiGateway.Aggregates.Sessions;
using AICopilot.Core.AiGateway.Ids;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AICopilot.EntityFrameworkCore.Configuration.AiGateway;

public sealed class MessageEventConfiguration : IEntityTypeConfiguration<MessageEvent>
{
    public void Configure(EntityTypeBuilder<MessageEvent> builder)
    {
        builder.ToTable("message_events");

        builder.HasKey(item => item.Id);
        builder.Property(item => item.Id)
            .HasConversion(id => id.Value, value => new MessageEventId(value))
            .HasColumnName("id");

        builder.Property<uint>("RowVersion").IsRowVersion();

        builder.Property(item => item.SessionId)
            .HasConversion(id => id.Value, value => new SessionId(value))
            .IsRequired()
            .HasColumnName("session_id");

        builder.Property(item => item.Sequence)
            .IsRequired()
            .HasColumnName("sequence");

        builder.Property(item => item.EventType)
            .HasConversion<string>()
            .HasMaxLength(80)
            .IsRequired()
            .HasColumnName("event_type");

        builder.Property(item => item.CreatedAt)
            .HasColumnType("timestamp with time zone")
            .HasColumnName("created_at");

        builder.Property(item => item.MessageId)
            .HasColumnName("message_id");

        builder.Property(item => item.AgentTaskId)
            .HasConversion(
                id => id.HasValue ? id.Value.Value : (Guid?)null,
                value => value.HasValue ? new AgentTaskId(value.Value) : null)
            .HasColumnName("agent_task_id");

        builder.Property(item => item.AgentStepId)
            .HasConversion(
                id => id.HasValue ? id.Value.Value : (Guid?)null,
                value => value.HasValue ? new AgentStepId(value.Value) : null)
            .HasColumnName("agent_step_id");

        builder.Property(item => item.ApprovalRequestId)
            .HasConversion(
                id => id.HasValue ? id.Value.Value : (Guid?)null,
                value => value.HasValue ? new ApprovalRequestId(value.Value) : null)
            .HasColumnName("approval_request_id");

        builder.Property(item => item.ArtifactWorkspaceId)
            .HasConversion(
                id => id.HasValue ? id.Value.Value : (Guid?)null,
                value => value.HasValue ? new ArtifactWorkspaceId(value.Value) : null)
            .HasColumnName("artifact_workspace_id");

        builder.Property(item => item.ArtifactId)
            .HasConversion(
                id => id.HasValue ? id.Value.Value : (Guid?)null,
                value => value.HasValue ? new ArtifactId(value.Value) : null)
            .HasColumnName("artifact_id");

        builder.Property(item => item.PayloadJson)
            .HasColumnName("payload_json");

        builder.HasOne<Session>()
            .WithMany()
            .HasForeignKey(item => item.SessionId)
            .HasConstraintName("fk_message_events_sessions_session_id")
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(item => item.Message)
            .WithMany()
            .HasForeignKey(item => item.MessageId)
            .HasConstraintName("fk_message_events_messages_message_id")
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<AgentTask>()
            .WithMany()
            .HasForeignKey(item => item.AgentTaskId)
            .HasConstraintName("fk_message_events_agent_tasks_agent_task_id")
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne<ApprovalRequest>()
            .WithMany()
            .HasForeignKey(item => item.ApprovalRequestId)
            .HasConstraintName("fk_message_events_approval_requests_approval_request_id")
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne<ArtifactWorkspace>()
            .WithMany()
            .HasForeignKey(item => item.ArtifactWorkspaceId)
            .HasConstraintName("fk_message_events_artifact_workspaces_artifact_workspace_id")
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne<Artifact>()
            .WithMany()
            .HasForeignKey(item => item.ArtifactId)
            .HasConstraintName("fk_message_events_artifacts_artifact_id")
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(item => new { item.SessionId, item.Sequence })
            .IsUnique()
            .HasDatabaseName("ix_message_events_session_id_sequence");

        builder.HasIndex(item => item.MessageId)
            .HasDatabaseName("ix_message_events_message_id");

        builder.HasIndex(item => item.AgentTaskId)
            .HasDatabaseName("ix_message_events_agent_task_id");

        builder.HasIndex(item => item.ApprovalRequestId)
            .HasDatabaseName("ix_message_events_approval_request_id");

        builder.HasIndex(item => item.ArtifactWorkspaceId)
            .HasDatabaseName("ix_message_events_artifact_workspace_id");

        builder.HasIndex(item => item.ArtifactId)
            .HasDatabaseName("ix_message_events_artifact_id");
    }
}
