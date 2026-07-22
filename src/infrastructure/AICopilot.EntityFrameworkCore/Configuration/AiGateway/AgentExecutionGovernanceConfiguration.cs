using AICopilot.Core.AiGateway.Ids;
using AICopilot.Core.AiGateway.Runtime.AgentExecution;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AICopilot.EntityFrameworkCore.Configuration.AiGateway;

public sealed class AgentNodeReconciliationDecisionConfiguration
    : IEntityTypeConfiguration<AgentNodeReconciliationDecision>
{
    public void Configure(EntityTypeBuilder<AgentNodeReconciliationDecision> builder)
    {
        builder.ToTable("agent_node_reconciliation_decisions");
        builder.HasKey(decision => decision.Id);
        builder.Property(decision => decision.Id)
            .HasConversion(id => id.Value, value => new AgentNodeReconciliationDecisionId(value))
            .HasColumnName("id");
        builder.Property<uint>("RowVersion").IsRowVersion();
        builder.Property(decision => decision.TaskId)
            .HasConversion(id => id.Value, value => new AgentTaskId(value))
            .HasColumnName("task_id");
        builder.Property(decision => decision.RunAttemptId)
            .HasConversion(id => id.Value, value => new AgentTaskRunAttemptId(value))
            .HasColumnName("run_attempt_id");
        builder.Property(decision => decision.NodeRunId)
            .HasConversion(id => id.Value, value => new AgentNodeRunId(value))
            .HasColumnName("node_run_id");
        builder.Property(decision => decision.TaskFencingToken).HasColumnName("task_fencing_token");
        builder.Property(decision => decision.NodeFencingToken).HasColumnName("node_fencing_token");
        builder.Property(decision => decision.ReconciliationFencingToken).HasColumnName("reconciliation_fencing_token");
        builder.Property(decision => decision.Resolution).HasConversion<string>().HasMaxLength(60).HasColumnName("resolution");
        builder.Property(decision => decision.ReasonCode).HasMaxLength(120).HasColumnName("reason_code");
        builder.Property(decision => decision.ActorType).HasMaxLength(40).HasColumnName("actor_type");
        builder.Property(decision => decision.ActorIdHash).HasMaxLength(128).HasColumnName("actor_id_hash");
        builder.Property(decision => decision.EvidenceDigest).HasMaxLength(128).HasColumnName("evidence_digest");
        builder.Property(decision => decision.ProviderReceiptHash).HasMaxLength(128).HasColumnName("provider_receipt_hash");
        builder.Property(decision => decision.DecisionDigest).HasMaxLength(128).HasColumnName("decision_digest");
        builder.Property(decision => decision.DecidedAtUtc).HasColumnType("timestamp with time zone").HasColumnName("decided_at_utc");

        builder.HasIndex(decision => new
            {
                decision.NodeRunId,
                decision.ReconciliationFencingToken
            })
            .IsUnique()
            .HasDatabaseName("ux_agent_node_reconciliation_fence");
        builder.HasIndex(decision => new { decision.TaskId, decision.DecidedAtUtc })
            .HasDatabaseName("ix_agent_node_reconciliation_task_time");
    }
}

public sealed class ModelQuotaReservationConfiguration : IEntityTypeConfiguration<ModelQuotaReservation>
{
    public void Configure(EntityTypeBuilder<ModelQuotaReservation> builder)
    {
        builder.ToTable("model_quota_reservations");
        builder.HasKey(reservation => reservation.Id);
        builder.Property(reservation => reservation.Id)
            .HasConversion(id => id.Value, value => new ModelQuotaReservationId(value))
            .HasColumnName("id");
        builder.Property<uint>("RowVersion").IsRowVersion();
        builder.Property(reservation => reservation.TenantKeyHash).HasMaxLength(128).HasColumnName("tenant_key_hash");
        builder.Property(reservation => reservation.UserId).HasColumnName("user_id");
        builder.Property(reservation => reservation.RoleKeyHash).HasMaxLength(128).HasColumnName("role_key_hash");
        builder.Property(reservation => reservation.ModelId)
            .HasConversion(id => id.Value, value => new LanguageModelId(value))
            .HasColumnName("model_id");
        builder.Property(reservation => reservation.EndpointId).HasMaxLength(160).HasColumnName("endpoint_id");
        builder.Property(reservation => reservation.PoolName).HasMaxLength(120).HasColumnName("pool_name");
        builder.Property(reservation => reservation.WindowStartedAtUtc).HasColumnType("timestamp with time zone").HasColumnName("window_started_at_utc");
        builder.Property(reservation => reservation.WindowEndsAtUtc).HasColumnType("timestamp with time zone").HasColumnName("window_ends_at_utc");
        builder.Property(reservation => reservation.EstimatedInputTokens).HasColumnName("estimated_input_tokens");
        builder.Property(reservation => reservation.EstimatedOutputTokens).HasColumnName("estimated_output_tokens");
        builder.Property(reservation => reservation.ActualInputTokens).HasColumnName("actual_input_tokens");
        builder.Property(reservation => reservation.ActualOutputTokens).HasColumnName("actual_output_tokens");
        builder.Property(reservation => reservation.ConcurrencySlots).HasColumnName("concurrency_slots");
        builder.Property(reservation => reservation.FencingToken).HasColumnName("fencing_token");
        builder.Property(reservation => reservation.CorrelationHash).HasMaxLength(128).HasColumnName("correlation_hash");
        builder.Property(reservation => reservation.Status).HasConversion<string>().HasMaxLength(40).HasColumnName("status");
        builder.Property(reservation => reservation.FailureCode).HasMaxLength(120).HasColumnName("failure_code");
        builder.Property(reservation => reservation.ReservedAtUtc).HasColumnType("timestamp with time zone").HasColumnName("reserved_at_utc");
        builder.Property(reservation => reservation.ExpiresAtUtc).HasColumnType("timestamp with time zone").HasColumnName("expires_at_utc");
        builder.Property(reservation => reservation.SettledAtUtc).HasColumnType("timestamp with time zone").HasColumnName("settled_at_utc");

        builder.HasIndex(reservation => reservation.CorrelationHash)
            .IsUnique()
            .HasDatabaseName("ux_model_quota_reservations_correlation");
        builder.HasIndex(reservation => new
            {
                reservation.EndpointId,
                reservation.ModelId,
                reservation.WindowStartedAtUtc,
                reservation.Status
            })
            .HasDatabaseName("ix_model_quota_reservations_endpoint_window");
        builder.HasIndex(reservation => new
            {
                reservation.TenantKeyHash,
                reservation.UserId,
                reservation.RoleKeyHash,
                reservation.WindowStartedAtUtc
            })
            .HasDatabaseName("ix_model_quota_reservations_authority_window");
        builder.HasIndex(reservation => new { reservation.Status, reservation.ExpiresAtUtc })
            .HasDatabaseName("ix_model_quota_reservations_expiry");
    }
}

public sealed class ArtifactFileSetOperationConfiguration : IEntityTypeConfiguration<ArtifactFileSetOperation>
{
    public void Configure(EntityTypeBuilder<ArtifactFileSetOperation> builder)
    {
        builder.ToTable("artifact_file_set_operations");
        builder.HasKey(operation => operation.Id);
        builder.Property(operation => operation.Id)
            .HasConversion(id => id.Value, value => new ArtifactFileSetOperationId(value))
            .HasColumnName("id");
        builder.Property<uint>("RowVersion").IsRowVersion();
        builder.Property(operation => operation.CommitId).HasColumnName("commit_id");
        builder.Property(operation => operation.TaskId)
            .HasConversion(id => id.Value, value => new AgentTaskId(value))
            .HasColumnName("task_id");
        builder.Property(operation => operation.WorkspaceId)
            .HasConversion(id => id.Value, value => new ArtifactWorkspaceId(value))
            .HasColumnName("workspace_id");
        builder.Property(operation => operation.NodeRunId)
            .HasConversion(
                id => id.HasValue ? id.Value.Value : (Guid?)null,
                value => value.HasValue ? new AgentNodeRunId(value.Value) : null)
            .HasColumnName("node_run_id");
        builder.Property(operation => operation.TaskFencingToken).HasColumnName("task_fencing_token");
        builder.Property(operation => operation.NodeFencingToken).HasColumnName("node_fencing_token");
        builder.Property(operation => operation.OperationKind).HasMaxLength(80).HasColumnName("operation_kind");
        builder.Property(operation => operation.Status).HasConversion<string>().HasMaxLength(40).HasColumnName("status");
        builder.Property(operation => operation.ManifestJson).HasColumnName("manifest_json");
        builder.Property(operation => operation.ManifestDigest).HasMaxLength(128).HasColumnName("manifest_digest");
        builder.Property(operation => operation.StagingReference).HasMaxLength(500).HasColumnName("staging_reference");
        builder.Property(operation => operation.PublishedReference).HasMaxLength(500).HasColumnName("published_reference");
        builder.Property(operation => operation.PublishedManifestDigest).HasMaxLength(128).HasColumnName("published_manifest_digest");
        builder.Property(operation => operation.FailureCode).HasMaxLength(120).HasColumnName("failure_code");
        builder.Property(operation => operation.SafeMessage).HasMaxLength(2000).HasColumnName("safe_message");
        builder.Property(operation => operation.CreatedAtUtc).HasColumnType("timestamp with time zone").HasColumnName("created_at_utc");
        builder.Property(operation => operation.UpdatedAtUtc).HasColumnType("timestamp with time zone").HasColumnName("updated_at_utc");
        builder.Property(operation => operation.CompletedAtUtc).HasColumnType("timestamp with time zone").HasColumnName("completed_at_utc");

        builder.HasIndex(operation => operation.CommitId)
            .IsUnique()
            .HasDatabaseName("ux_artifact_file_set_operations_commit");
        builder.HasIndex(operation => new { operation.WorkspaceId, operation.Status, operation.CreatedAtUtc })
            .HasDatabaseName("ix_artifact_file_set_operations_workspace_status");
        builder.HasIndex(operation => new { operation.NodeRunId, operation.TaskFencingToken, operation.NodeFencingToken })
            .HasDatabaseName("ix_artifact_file_set_operations_node_fence");
    }
}
