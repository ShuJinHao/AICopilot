using System.Linq.Expressions;
using AICopilot.Core.AiGateway.Ids;
using AICopilot.Core.AiGateway.Runtime.AgentExecution;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using static AICopilot.EntityFrameworkCore.Configuration.AiGateway.AgentExecutionRuntimeConfigurationMapping;

namespace AICopilot.EntityFrameworkCore.Configuration.AiGateway;

public sealed class AgentNodeRunConfiguration : IEntityTypeConfiguration<AgentNodeRun>
{
    public void Configure(EntityTypeBuilder<AgentNodeRun> builder)
    {
        ConfigureEntity(builder, "agent_node_runs", node => node.Id, id => id.Value, value => new AgentNodeRunId(value));
        MapGuidId(builder, node => node.TaskId, id => id.Value, value => new AgentTaskId(value), "task_id");
        MapGuidId(builder, node => node.RunAttemptId, id => id.Value, value => new AgentTaskRunAttemptId(value), "run_attempt_id");
        MapGuidId(builder, node => node.QueueItemId, id => id.Value, value => new AgentTaskRunQueueItemId(value), "queue_item_id");
        MapTextColumns(builder,
            (nameof(AgentNodeRun.PlanDigest), "plan_digest", 128), (nameof(AgentNodeRun.ExecutionSnapshotDigest), "execution_snapshot_digest", 128),
            (nameof(AgentNodeRun.NodeId), "node_id", 160), (nameof(AgentNodeRun.NodeKind), "node_kind", 80), (nameof(AgentNodeRun.ToolCode), "tool_code", 120), (nameof(AgentNodeRun.InputDigest), "input_digest", 128),
            (nameof(AgentNodeRun.OutputSchemaRef), "output_schema_ref", 160), (nameof(AgentNodeRun.JoinPolicy), "join_policy", 40), (nameof(AgentNodeRun.IdempotencyKeyHash), "idempotency_key_hash", 128), (nameof(AgentNodeRun.LeaseOwner), "lease_owner", 120),
            (nameof(AgentNodeRun.ProviderOperationCode), "provider_operation_code", 160), (nameof(AgentNodeRun.ProviderReceiptHash), "provider_receipt_hash", 128), (nameof(AgentNodeRun.ReconciliationPolicy), "reconciliation_policy", 120), (nameof(AgentNodeRun.LastConfirmedStage), "last_confirmed_stage", 120),
            (nameof(AgentNodeRun.IntegrityStatus), "integrity_status", 80), (nameof(AgentNodeRun.ReconciliationOwner), "reconciliation_owner", 120), (nameof(AgentNodeRun.ReconciliationResolutionCode), "reconciliation_resolution_code", 120), (nameof(AgentNodeRun.ReconciliationDecisionDigest), "reconciliation_decision_digest", 128),
            (nameof(AgentNodeRun.OutputDigest), "output_digest", 128), (nameof(AgentNodeRun.EvidenceSetDigest), "evidence_set_digest", 128), (nameof(AgentNodeRun.FailureCode), "failure_code", 120), (nameof(AgentNodeRun.SafeMessage), "safe_message", 2000));
        MapColumns(builder,
            (nameof(AgentNodeRun.DependenciesJson), "dependencies_json"), (nameof(AgentNodeRun.InputJson), "input_json"), (nameof(AgentNodeRun.IsRequired), "is_required"), (nameof(AgentNodeRun.RequiresApproval), "requires_approval"),
            (nameof(AgentNodeRun.AttemptNo), "attempt_no"), (nameof(AgentNodeRun.MaxAttempts), "max_attempts"), (nameof(AgentNodeRun.TimeoutSeconds), "timeout_seconds"), (nameof(AgentNodeRun.MaxToolCalls), "max_tool_calls"), (nameof(AgentNodeRun.MaxModelCalls), "max_model_calls"), (nameof(AgentNodeRun.MaxInputTokens), "max_input_tokens"),
            (nameof(AgentNodeRun.MaxOutputTokens), "max_output_tokens"), (nameof(AgentNodeRun.MaxArtifactCount), "max_artifact_count"), (nameof(AgentNodeRun.MaxArtifactBytes), "max_artifact_bytes"), (nameof(AgentNodeRun.BudgetReservationNodeFencingToken), "budget_reservation_node_fencing_token"), (nameof(AgentNodeRun.ReservedToolCalls), "reserved_tool_calls"), (nameof(AgentNodeRun.ReservedModelCalls), "reserved_model_calls"),
            (nameof(AgentNodeRun.ReservedInputTokens), "reserved_input_tokens"), (nameof(AgentNodeRun.ReservedOutputTokens), "reserved_output_tokens"), (nameof(AgentNodeRun.ReservedElapsedMilliseconds), "reserved_elapsed_milliseconds"), (nameof(AgentNodeRun.ReservedRetryCount), "reserved_retry_count"), (nameof(AgentNodeRun.ReservedArtifactCount), "reserved_artifact_count"), (nameof(AgentNodeRun.ReservedArtifactBytes), "reserved_artifact_bytes"),
            (nameof(AgentNodeRun.TaskFencingToken), "task_fencing_token"), (nameof(AgentNodeRun.NodeFencingToken), "node_fencing_token"), (nameof(AgentNodeRun.IdempotencyGeneration), "idempotency_generation"), (nameof(AgentNodeRun.LeaseId), "lease_id"), (nameof(AgentNodeRun.ReconciliationFencingToken), "reconciliation_fencing_token"), (nameof(AgentNodeRun.ReconciliationAttemptNo), "reconciliation_attempt_no"),
            (nameof(AgentNodeRun.ReconciliationLeaseId), "reconciliation_lease_id"), (nameof(AgentNodeRun.RequiresManualResolution), "requires_manual_resolution"));
        MapEnumColumns(builder,
            (nameof(AgentNodeRun.SideEffectClass), "side_effect_class"), (nameof(AgentNodeRun.Status), "status"),
            (nameof(AgentNodeRun.BudgetReservationStatus), "budget_reservation_status"));
        MapDecimalColumns(builder,
            (nameof(AgentNodeRun.MaxCostAmount), "max_cost_amount"), (nameof(AgentNodeRun.ReservedCostAmount), "reserved_cost_amount"));
        MapTimestampColumns(builder,
            (nameof(AgentNodeRun.LeaseExpiresAt), "lease_expires_at"), (nameof(AgentNodeRun.ReconciliationLeaseExpiresAt), "reconciliation_lease_expires_at"),
            (nameof(AgentNodeRun.ReconciliationDeadlineAt), "reconciliation_deadline_at"), (nameof(AgentNodeRun.ReconciledAt), "reconciled_at"),
            (nameof(AgentNodeRun.NextAttemptAt), "next_attempt_at"), (nameof(AgentNodeRun.StartedAt), "started_at"),
            (nameof(AgentNodeRun.CompletedAt), "completed_at"), (nameof(AgentNodeRun.CreatedAt), "created_at"),
            (nameof(AgentNodeRun.UpdatedAt), "updated_at"));
        builder.Property(node => node.EvidenceId)
            .HasConversion(
                id => id.HasValue ? id.Value.Value : (Guid?)null,
                value => value.HasValue ? new AgentEvidenceRecordId(value.Value) : null)
            .HasColumnName("evidence_id");

        builder.HasIndex(node => new { node.RunAttemptId, node.NodeId })
            .IsUnique()
            .HasDatabaseName("ux_agent_node_runs_attempt_node");
        builder.HasIndex(node => new { node.RunAttemptId, node.Status, node.NextAttemptAt })
            .HasDatabaseName("ix_agent_node_runs_runnable");
        builder.HasIndex(node => node.LeaseExpiresAt)
            .HasDatabaseName("ix_agent_node_runs_lease_expires_at");
        builder.HasIndex(node => new { node.Status, node.NextAttemptAt, node.ReconciliationLeaseExpiresAt })
            .HasDatabaseName("ix_agent_node_runs_reconciliation");
        MapUniqueFilteredIndex(builder, node => node.EvidenceId, "evidence_id IS NOT NULL", "ux_agent_node_runs_evidence_id");
    }
}

public sealed class AgentEvidenceRecordConfiguration : IEntityTypeConfiguration<AgentEvidenceRecord>
{
    public void Configure(EntityTypeBuilder<AgentEvidenceRecord> builder)
    {
        ConfigureEntity(builder, "agent_evidence_records", evidence => evidence.Id, id => id.Value, value => new AgentEvidenceRecordId(value));
        MapColumns(builder,
            (nameof(AgentEvidenceRecord.TenantId), "tenant_id"), (nameof(AgentEvidenceRecord.UserId), "user_id"),
            (nameof(AgentEvidenceRecord.CanonicalEnvelopeJson), "canonical_envelope_json"), (nameof(AgentEvidenceRecord.InlinePayloadJson), "inline_payload_json"),
            (nameof(AgentEvidenceRecord.ByteLength), "byte_length"), (nameof(AgentEvidenceRecord.AllowedConsumerScopeJson), "allowed_consumer_scope_json"),
            (nameof(AgentEvidenceRecord.TaskFencingToken), "task_fencing_token"), (nameof(AgentEvidenceRecord.NodeFencingToken), "node_fencing_token"),
            (nameof(AgentEvidenceRecord.IsRevoked), "is_revoked"));
        MapGuidId(builder, evidence => evidence.SessionId, id => id.Value, value => new SessionId(value), "session_id");
        MapGuidId(builder, evidence => evidence.TaskId, id => id.Value, value => new AgentTaskId(value), "task_id");
        MapGuidId(builder, evidence => evidence.RunAttemptId, id => id.Value, value => new AgentTaskRunAttemptId(value), "run_attempt_id");
        MapGuidId(builder, evidence => evidence.NodeRunId, id => id.Value, value => new AgentNodeRunId(value), "node_run_id");
        MapTextColumns(builder,
            (nameof(AgentEvidenceRecord.NodeId), "node_id", 160), (nameof(AgentEvidenceRecord.EnvelopeDigest), "envelope_digest", 128),
            (nameof(AgentEvidenceRecord.OutputDigest), "output_digest", 128), (nameof(AgentEvidenceRecord.PayloadRef), "payload_ref", 400),
            (nameof(AgentEvidenceRecord.MediaType), "media_type", 160), (nameof(AgentEvidenceRecord.PayloadSha256), "payload_sha256", 128));
        MapEnumColumns(builder,
            (nameof(AgentEvidenceRecord.EvidenceKind), "evidence_kind"), (nameof(AgentEvidenceRecord.TruthClass), "truth_class"),
            (nameof(AgentEvidenceRecord.StorageMode), "storage_mode"));
        MapTimestampColumns(builder,
            (nameof(AgentEvidenceRecord.CreatedAt), "created_at"), (nameof(AgentEvidenceRecord.ExpiresAt), "expires_at"));

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
        ConfigureEntity(builder, "agent_run_usage_ledger", usage => usage.Id, id => id.Value, value => new AgentRunUsageLedgerEntryId(value));
        MapGuidId(builder, usage => usage.TaskId, id => id.Value, value => new AgentTaskId(value), "task_id");
        MapGuidId(builder, usage => usage.RunAttemptId, id => id.Value, value => new AgentTaskRunAttemptId(value), "run_attempt_id");
        MapGuidId(builder, usage => usage.NodeRunId, id => id.Value, value => new AgentNodeRunId(value), "node_run_id");
        MapColumns(builder,
            (nameof(AgentRunUsageLedgerEntry.TaskFencingToken), "task_fencing_token"), (nameof(AgentRunUsageLedgerEntry.NodeFencingToken), "node_fencing_token"),
            (nameof(AgentRunUsageLedgerEntry.InputTokens), "input_tokens"), (nameof(AgentRunUsageLedgerEntry.OutputTokens), "output_tokens"),
            (nameof(AgentRunUsageLedgerEntry.ModelCalls), "model_calls"), (nameof(AgentRunUsageLedgerEntry.ToolCalls), "tool_calls"),
            (nameof(AgentRunUsageLedgerEntry.ElapsedMilliseconds), "elapsed_milliseconds"), (nameof(AgentRunUsageLedgerEntry.ArtifactCount), "artifact_count"),
            (nameof(AgentRunUsageLedgerEntry.ArtifactBytes), "artifact_bytes"));
        MapDecimalColumns(builder, (nameof(AgentRunUsageLedgerEntry.CostAmount), "cost_amount"));
        MapTextColumns(builder,
            (nameof(AgentRunUsageLedgerEntry.CostCurrency), "cost_currency", 8), (nameof(AgentRunUsageLedgerEntry.CorrelationHash), "correlation_hash", 128));
        MapTimestampColumns(builder, (nameof(AgentRunUsageLedgerEntry.CreatedAt), "created_at"));

        builder.HasIndex(usage => new { usage.NodeRunId, usage.NodeFencingToken })
            .IsUnique()
            .HasDatabaseName("ux_agent_run_usage_node_fence");
        builder.HasIndex(usage => new { usage.TaskId, usage.RunAttemptId })
            .HasDatabaseName("ix_agent_run_usage_attempt");
    }
}

internal static class AgentExecutionRuntimeConfigurationMapping
{
    public static void ConfigureEntity<TEntity, TId>(
        EntityTypeBuilder<TEntity> builder,
        string table,
        Expression<Func<TEntity, TId>> idProperty,
        Expression<Func<TId, Guid>> toProvider,
        Expression<Func<Guid, TId>> fromProvider,
        bool usesRowVersion = true)
        where TEntity : class
    {
        builder.ToTable(table);
        var keyProperty = Expression.Lambda<Func<TEntity, object?>>(
            Expression.Convert(idProperty.Body, typeof(object)),
            idProperty.Parameters);
        builder.HasKey(keyProperty);
        builder.Property(idProperty).HasConversion(toProvider, fromProvider).HasColumnName("id");
        if (usesRowVersion)
        {
            builder.Property<uint>("RowVersion").IsRowVersion();
        }
    }

    public static void MapGuidId<TEntity, TId>(
        EntityTypeBuilder<TEntity> builder,
        Expression<Func<TEntity, TId>> property,
        Expression<Func<TId, Guid>> toProvider,
        Expression<Func<Guid, TId>> fromProvider,
        string column)
        where TEntity : class =>
        builder.Property(property).HasConversion(toProvider, fromProvider).HasColumnName(column);

    public static void MapLeaseColumns<TEntity>(
        EntityTypeBuilder<TEntity> builder,
        (string Property, string Column) leaseId,
        (string Property, string Column) leaseOwner,
        (string Property, string Column) leaseExpiresAt,
        (string Property, string Column) fencingToken)
        where TEntity : class
    {
        builder.Property(leaseId.Property).HasColumnName(leaseId.Column);
        builder.Property(leaseOwner.Property).HasMaxLength(120).HasColumnName(leaseOwner.Column);
        builder.Property(leaseExpiresAt.Property).HasColumnType("timestamp with time zone").HasColumnName(leaseExpiresAt.Column);
        builder.Property(fencingToken.Property).IsRequired().HasDefaultValue(0L).HasColumnName(fencingToken.Column);
    }

    public static void MapUniqueFilteredIndex<TEntity>(
        EntityTypeBuilder<TEntity> builder,
        Expression<Func<TEntity, object?>> property,
        string filter,
        string databaseName)
        where TEntity : class =>
        builder.HasIndex(property).IsUnique().HasFilter(filter).HasDatabaseName(databaseName);

    public static void MapColumns<TEntity>(
        EntityTypeBuilder<TEntity> builder,
        params (string Property, string Column)[] mappings)
        where TEntity : class
    {
        foreach (var mapping in mappings)
        {
            builder.Property(mapping.Property).HasColumnName(mapping.Column);
        }
    }

    public static void MapTextColumns<TEntity>(
        EntityTypeBuilder<TEntity> builder,
        params (string Property, string Column, int MaxLength)[] mappings)
        where TEntity : class
    {
        foreach (var mapping in mappings)
        {
            builder.Property(mapping.Property).HasMaxLength(mapping.MaxLength).HasColumnName(mapping.Column);
        }
    }

    public static void MapEnumColumns<TEntity>(
        EntityTypeBuilder<TEntity> builder,
        params (string Property, string Column)[] mappings)
        where TEntity : class
    {
        foreach (var mapping in mappings)
        {
            builder.Property(mapping.Property).HasConversion<string>().HasMaxLength(40).HasColumnName(mapping.Column);
        }
    }

    public static void MapDecimalColumns<TEntity>(
        EntityTypeBuilder<TEntity> builder,
        params (string Property, string Column)[] mappings)
        where TEntity : class
    {
        foreach (var mapping in mappings)
        {
            builder.Property(mapping.Property).HasPrecision(18, 6).HasColumnName(mapping.Column);
        }
    }

    public static void MapTimestampColumns<TEntity>(
        EntityTypeBuilder<TEntity> builder,
        params (string Property, string Column)[] mappings)
        where TEntity : class
    {
        foreach (var mapping in mappings)
        {
            builder.Property(mapping.Property).HasColumnType("timestamp with time zone").HasColumnName(mapping.Column);
        }
    }
}
