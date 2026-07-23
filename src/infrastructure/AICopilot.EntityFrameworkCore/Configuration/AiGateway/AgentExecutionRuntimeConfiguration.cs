using AICopilot.Core.AiGateway.Ids;
using AICopilot.Core.AiGateway.Runtime.AgentExecution;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AICopilot.EntityFrameworkCore.Configuration.AiGateway;

public sealed class AgentNodeRunConfiguration : IEntityTypeConfiguration<AgentNodeRun>
{
    public void Configure(EntityTypeBuilder<AgentNodeRun> builder)
    {
        builder.ToTable("agent_node_runs");
        builder.HasKey(node => node.Id);
        builder.Property(node => node.Id)
            .HasConversion(id => id.Value, value => new AgentNodeRunId(value))
            .HasColumnName("id");
        builder.Property<uint>("RowVersion").IsRowVersion();
        builder.Property(node => node.TaskId)
            .HasConversion(id => id.Value, value => new AgentTaskId(value))
            .HasColumnName("task_id");
        builder.Property(node => node.RunAttemptId)
            .HasConversion(id => id.Value, value => new AgentTaskRunAttemptId(value))
            .HasColumnName("run_attempt_id");
        builder.Property(node => node.QueueItemId)
            .HasConversion(id => id.Value, value => new AgentTaskRunQueueItemId(value))
            .HasColumnName("queue_item_id");
        builder.Property(node => node.PlanDigest).HasMaxLength(128).HasColumnName("plan_digest");
        builder.Property(node => node.ExecutionSnapshotDigest).HasMaxLength(128).HasColumnName("execution_snapshot_digest");
        builder.Property(node => node.NodeId).HasMaxLength(160).HasColumnName("node_id");
        builder.Property(node => node.NodeKind).HasMaxLength(80).HasColumnName("node_kind");
        builder.Property(node => node.ToolCode).HasMaxLength(120).HasColumnName("tool_code");
        builder.Property(node => node.DependenciesJson).HasColumnName("dependencies_json");
        builder.Property(node => node.InputJson).HasColumnName("input_json");
        builder.Property(node => node.InputDigest).HasMaxLength(128).HasColumnName("input_digest");
        builder.Property(node => node.OutputSchemaRef).HasMaxLength(160).HasColumnName("output_schema_ref");
        builder.Property(node => node.IsRequired).HasColumnName("is_required");
        builder.Property(node => node.RequiresApproval).HasColumnName("requires_approval");
        builder.Property(node => node.JoinPolicy).HasMaxLength(40).HasColumnName("join_policy");
        builder.Property(node => node.SideEffectClass).HasConversion<string>().HasMaxLength(40).HasColumnName("side_effect_class");
        builder.Property(node => node.Status).HasConversion<string>().HasMaxLength(40).HasColumnName("status");
        builder.Property(node => node.AttemptNo).HasColumnName("attempt_no");
        builder.Property(node => node.MaxAttempts).HasColumnName("max_attempts");
        builder.Property(node => node.TimeoutSeconds).HasColumnName("timeout_seconds");
        builder.Property(node => node.MaxToolCalls).HasColumnName("max_tool_calls");
        builder.Property(node => node.MaxModelCalls).HasColumnName("max_model_calls");
        builder.Property(node => node.MaxInputTokens).HasColumnName("max_input_tokens");
        builder.Property(node => node.MaxOutputTokens).HasColumnName("max_output_tokens");
        builder.Property(node => node.MaxCostAmount).HasPrecision(18, 6).HasColumnName("max_cost_amount");
        builder.Property(node => node.MaxArtifactCount).HasColumnName("max_artifact_count");
        builder.Property(node => node.MaxArtifactBytes).HasColumnName("max_artifact_bytes");
        builder.Property(node => node.BudgetReservationStatus).HasConversion<string>().HasMaxLength(40).HasColumnName("budget_reservation_status");
        builder.Property(node => node.BudgetReservationNodeFencingToken).HasColumnName("budget_reservation_node_fencing_token");
        builder.Property(node => node.ReservedToolCalls).HasColumnName("reserved_tool_calls");
        builder.Property(node => node.ReservedModelCalls).HasColumnName("reserved_model_calls");
        builder.Property(node => node.ReservedInputTokens).HasColumnName("reserved_input_tokens");
        builder.Property(node => node.ReservedOutputTokens).HasColumnName("reserved_output_tokens");
        builder.Property(node => node.ReservedElapsedMilliseconds).HasColumnName("reserved_elapsed_milliseconds");
        builder.Property(node => node.ReservedCostAmount).HasPrecision(18, 6).HasColumnName("reserved_cost_amount");
        builder.Property(node => node.ReservedRetryCount).HasColumnName("reserved_retry_count");
        builder.Property(node => node.ReservedArtifactCount).HasColumnName("reserved_artifact_count");
        builder.Property(node => node.ReservedArtifactBytes).HasColumnName("reserved_artifact_bytes");
        builder.Property(node => node.TaskFencingToken).HasColumnName("task_fencing_token");
        builder.Property(node => node.NodeFencingToken).HasColumnName("node_fencing_token");
        builder.Property(node => node.IdempotencyGeneration).HasColumnName("idempotency_generation");
        builder.Property(node => node.LeaseId).HasColumnName("lease_id");
        builder.Property(node => node.LeaseOwner).HasMaxLength(120).HasColumnName("lease_owner");
        builder.Property(node => node.LeaseExpiresAt).HasColumnType("timestamp with time zone").HasColumnName("lease_expires_at");
        builder.Property(node => node.IdempotencyKeyHash).HasMaxLength(128).HasColumnName("idempotency_key_hash");
        builder.Property(node => node.ProviderOperationCode).HasMaxLength(160).HasColumnName("provider_operation_code");
        builder.Property(node => node.ProviderReceiptHash).HasMaxLength(128).HasColumnName("provider_receipt_hash");
        builder.Property(node => node.ReconciliationPolicy).HasMaxLength(120).HasColumnName("reconciliation_policy");
        builder.Property(node => node.LastConfirmedStage).HasMaxLength(120).HasColumnName("last_confirmed_stage");
        builder.Property(node => node.IntegrityStatus).HasMaxLength(80).HasColumnName("integrity_status");
        builder.Property(node => node.ReconciliationFencingToken).HasColumnName("reconciliation_fencing_token");
        builder.Property(node => node.ReconciliationAttemptNo).HasColumnName("reconciliation_attempt_no");
        builder.Property(node => node.ReconciliationLeaseId).HasColumnName("reconciliation_lease_id");
        builder.Property(node => node.ReconciliationOwner).HasMaxLength(120).HasColumnName("reconciliation_owner");
        builder.Property(node => node.ReconciliationLeaseExpiresAt).HasColumnType("timestamp with time zone").HasColumnName("reconciliation_lease_expires_at");
        builder.Property(node => node.ReconciliationDeadlineAt).HasColumnType("timestamp with time zone").HasColumnName("reconciliation_deadline_at");
        builder.Property(node => node.RequiresManualResolution).HasColumnName("requires_manual_resolution");
        builder.Property(node => node.ReconciliationResolutionCode).HasMaxLength(120).HasColumnName("reconciliation_resolution_code");
        builder.Property(node => node.ReconciliationDecisionDigest).HasMaxLength(128).HasColumnName("reconciliation_decision_digest");
        builder.Property(node => node.ReconciledAt).HasColumnType("timestamp with time zone").HasColumnName("reconciled_at");
        builder.Property(node => node.OutputDigest).HasMaxLength(128).HasColumnName("output_digest");
        builder.Property(node => node.EvidenceId)
            .HasConversion(
                id => id.HasValue ? id.Value.Value : (Guid?)null,
                value => value.HasValue ? new AgentEvidenceRecordId(value.Value) : null)
            .HasColumnName("evidence_id");
        builder.Property(node => node.EvidenceSetDigest).HasMaxLength(128).HasColumnName("evidence_set_digest");
        builder.Property(node => node.FailureCode).HasMaxLength(120).HasColumnName("failure_code");
        builder.Property(node => node.SafeMessage).HasMaxLength(2000).HasColumnName("safe_message");
        builder.Property(node => node.NextAttemptAt).HasColumnType("timestamp with time zone").HasColumnName("next_attempt_at");
        builder.Property(node => node.StartedAt).HasColumnType("timestamp with time zone").HasColumnName("started_at");
        builder.Property(node => node.CompletedAt).HasColumnType("timestamp with time zone").HasColumnName("completed_at");
        builder.Property(node => node.CreatedAt).HasColumnType("timestamp with time zone").HasColumnName("created_at");
        builder.Property(node => node.UpdatedAt).HasColumnType("timestamp with time zone").HasColumnName("updated_at");

        builder.HasIndex(node => new { node.RunAttemptId, node.NodeId })
            .IsUnique()
            .HasDatabaseName("ux_agent_node_runs_attempt_node");
        builder.HasIndex(node => new { node.RunAttemptId, node.Status, node.NextAttemptAt })
            .HasDatabaseName("ix_agent_node_runs_runnable");
        builder.HasIndex(node => node.LeaseExpiresAt)
            .HasDatabaseName("ix_agent_node_runs_lease_expires_at");
        builder.HasIndex(node => new { node.Status, node.NextAttemptAt, node.ReconciliationLeaseExpiresAt })
            .HasDatabaseName("ix_agent_node_runs_reconciliation");
        builder.HasIndex(node => node.EvidenceId)
            .IsUnique()
            .HasFilter("evidence_id IS NOT NULL")
            .HasDatabaseName("ux_agent_node_runs_evidence_id");
    }
}

public sealed class AgentEvidenceRecordConfiguration : IEntityTypeConfiguration<AgentEvidenceRecord>
{
    public void Configure(EntityTypeBuilder<AgentEvidenceRecord> builder)
    {
        builder.ToTable("agent_evidence_records");
        builder.HasKey(evidence => evidence.Id);
        builder.Property(evidence => evidence.Id)
            .HasConversion(id => id.Value, value => new AgentEvidenceRecordId(value))
            .HasColumnName("id");
        builder.Property<uint>("RowVersion").IsRowVersion();
        builder.Property(evidence => evidence.TenantId).HasColumnName("tenant_id");
        builder.Property(evidence => evidence.UserId).HasColumnName("user_id");
        builder.Property(evidence => evidence.SessionId)
            .HasConversion(id => id.Value, value => new SessionId(value))
            .HasColumnName("session_id");
        builder.Property(evidence => evidence.TaskId)
            .HasConversion(id => id.Value, value => new AgentTaskId(value))
            .HasColumnName("task_id");
        builder.Property(evidence => evidence.RunAttemptId)
            .HasConversion(id => id.Value, value => new AgentTaskRunAttemptId(value))
            .HasColumnName("run_attempt_id");
        builder.Property(evidence => evidence.NodeRunId)
            .HasConversion(id => id.Value, value => new AgentNodeRunId(value))
            .HasColumnName("node_run_id");
        builder.Property(evidence => evidence.NodeId).HasMaxLength(160).HasColumnName("node_id");
        builder.Property(evidence => evidence.EvidenceKind).HasConversion<string>().HasMaxLength(40).HasColumnName("evidence_kind");
        builder.Property(evidence => evidence.TruthClass).HasConversion<string>().HasMaxLength(40).HasColumnName("truth_class");
        builder.Property(evidence => evidence.StorageMode).HasConversion<string>().HasMaxLength(40).HasColumnName("storage_mode");
        builder.Property(evidence => evidence.CanonicalEnvelopeJson).HasColumnName("canonical_envelope_json");
        builder.Property(evidence => evidence.EnvelopeDigest).HasMaxLength(128).HasColumnName("envelope_digest");
        builder.Property(evidence => evidence.OutputDigest).HasMaxLength(128).HasColumnName("output_digest");
        builder.Property(evidence => evidence.InlinePayloadJson).HasColumnName("inline_payload_json");
        builder.Property(evidence => evidence.PayloadRef).HasMaxLength(400).HasColumnName("payload_ref");
        builder.Property(evidence => evidence.MediaType).HasMaxLength(160).HasColumnName("media_type");
        builder.Property(evidence => evidence.ByteLength).HasColumnName("byte_length");
        builder.Property(evidence => evidence.PayloadSha256).HasMaxLength(128).HasColumnName("payload_sha256");
        builder.Property(evidence => evidence.AllowedConsumerScopeJson).HasColumnName("allowed_consumer_scope_json");
        builder.Property(evidence => evidence.TaskFencingToken).HasColumnName("task_fencing_token");
        builder.Property(evidence => evidence.NodeFencingToken).HasColumnName("node_fencing_token");
        builder.Property(evidence => evidence.IsRevoked).HasColumnName("is_revoked");
        builder.Property(evidence => evidence.CreatedAt).HasColumnType("timestamp with time zone").HasColumnName("created_at");
        builder.Property(evidence => evidence.ExpiresAt).HasColumnType("timestamp with time zone").HasColumnName("expires_at");

        builder.HasIndex(evidence => new { evidence.NodeRunId, evidence.NodeFencingToken })
            .IsUnique()
            .HasDatabaseName("ux_agent_evidence_node_fence");
        builder.HasIndex(evidence => new { evidence.UserId, evidence.TaskId, evidence.CreatedAt })
            .HasDatabaseName("ix_agent_evidence_consumer_scope");
        builder.HasIndex(evidence => evidence.EnvelopeDigest)
            .HasDatabaseName("ix_agent_evidence_digest");
    }
}

public sealed class AgentRunUsageLedgerEntryConfiguration : IEntityTypeConfiguration<AgentRunUsageLedgerEntry>
{
    public void Configure(EntityTypeBuilder<AgentRunUsageLedgerEntry> builder)
    {
        builder.ToTable("agent_run_usage_ledger");
        builder.HasKey(usage => usage.Id);
        builder.Property(usage => usage.Id)
            .HasConversion(id => id.Value, value => new AgentRunUsageLedgerEntryId(value))
            .HasColumnName("id");
        builder.Property<uint>("RowVersion").IsRowVersion();
        builder.Property(usage => usage.TaskId)
            .HasConversion(id => id.Value, value => new AgentTaskId(value))
            .HasColumnName("task_id");
        builder.Property(usage => usage.RunAttemptId)
            .HasConversion(id => id.Value, value => new AgentTaskRunAttemptId(value))
            .HasColumnName("run_attempt_id");
        builder.Property(usage => usage.NodeRunId)
            .HasConversion(id => id.Value, value => new AgentNodeRunId(value))
            .HasColumnName("node_run_id");
        builder.Property(usage => usage.TaskFencingToken).HasColumnName("task_fencing_token");
        builder.Property(usage => usage.NodeFencingToken).HasColumnName("node_fencing_token");
        builder.Property(usage => usage.InputTokens).HasColumnName("input_tokens");
        builder.Property(usage => usage.OutputTokens).HasColumnName("output_tokens");
        builder.Property(usage => usage.ModelCalls).HasColumnName("model_calls");
        builder.Property(usage => usage.ToolCalls).HasColumnName("tool_calls");
        builder.Property(usage => usage.ElapsedMilliseconds).HasColumnName("elapsed_milliseconds");
        builder.Property(usage => usage.CostAmount).HasPrecision(18, 6).HasColumnName("cost_amount");
        builder.Property(usage => usage.ArtifactCount).HasColumnName("artifact_count");
        builder.Property(usage => usage.ArtifactBytes).HasColumnName("artifact_bytes");
        builder.Property(usage => usage.CostCurrency).HasMaxLength(8).HasColumnName("cost_currency");
        builder.Property(usage => usage.CorrelationHash).HasMaxLength(128).HasColumnName("correlation_hash");
        builder.Property(usage => usage.CreatedAt).HasColumnType("timestamp with time zone").HasColumnName("created_at");

        builder.HasIndex(usage => new { usage.NodeRunId, usage.NodeFencingToken })
            .IsUnique()
            .HasDatabaseName("ux_agent_run_usage_node_fence");
        builder.HasIndex(usage => new { usage.TaskId, usage.RunAttemptId })
            .HasDatabaseName("ix_agent_run_usage_attempt");
    }
}
