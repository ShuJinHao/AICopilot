using AICopilot.Core.AiGateway.Aggregates.ProductionOperations;
using AICopilot.Core.AiGateway.Ids;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AICopilot.EntityFrameworkCore.Configuration.AiGateway;

public sealed class ProductionPilotEmergencyStopStateConfiguration : IEntityTypeConfiguration<ProductionPilotEmergencyStopState>
{
    public void Configure(EntityTypeBuilder<ProductionPilotEmergencyStopState> builder)
    {
        builder.ToTable("production_pilot_emergency_stop_states");

        builder.HasKey(state => state.Id);
        builder.Property(state => state.Id)
            .HasConversion(id => id.Value, value => new ProductionPilotEmergencyStopStateId(value))
            .HasColumnName("id");

        builder.Property<uint>("RowVersion").IsRowVersion();

        builder.Property(state => state.IsActive)
            .IsRequired()
            .HasColumnName("is_active");

        builder.Property(state => state.Reason)
            .HasMaxLength(240)
            .HasColumnName("reason");

        builder.Property(state => state.ActivatedBy)
            .HasMaxLength(160)
            .HasColumnName("activated_by");

        builder.Property(state => state.ActivatedAt)
            .HasColumnType("timestamp with time zone")
            .HasColumnName("activated_at");

        builder.Property(state => state.ClearedBy)
            .HasMaxLength(160)
            .HasColumnName("cleared_by");

        builder.Property(state => state.ClearedAt)
            .HasColumnType("timestamp with time zone")
            .HasColumnName("cleared_at");

        builder.Property(state => state.CreatedAt)
            .HasColumnType("timestamp with time zone")
            .HasColumnName("created_at");

        builder.Property(state => state.UpdatedAt)
            .HasColumnType("timestamp with time zone")
            .HasColumnName("updated_at");
    }
}

public sealed class ProductionPilotIncidentConfiguration : IEntityTypeConfiguration<ProductionPilotIncident>
{
    public void Configure(EntityTypeBuilder<ProductionPilotIncident> builder)
    {
        builder.ToTable("production_pilot_incidents");

        builder.HasKey(incident => incident.Id);
        builder.Property(incident => incident.Id)
            .HasConversion(id => id.Value, value => new ProductionPilotIncidentId(value))
            .HasColumnName("id");

        builder.Property<uint>("RowVersion").IsRowVersion();

        builder.Property(incident => incident.Severity)
            .IsRequired()
            .HasMaxLength(40)
            .HasColumnName("severity");

        builder.Property(incident => incident.Category)
            .IsRequired()
            .HasMaxLength(120)
            .HasColumnName("category");

        builder.Property(incident => incident.Status)
            .IsRequired()
            .HasMaxLength(40)
            .HasColumnName("status");

        builder.Property(incident => incident.Owner)
            .HasMaxLength(120)
            .HasColumnName("owner");

        builder.Property(incident => incident.SourceRef)
            .HasMaxLength(240)
            .HasColumnName("source_ref");

        builder.Property(incident => incident.ResolutionHash)
            .HasMaxLength(128)
            .HasColumnName("resolution_hash");

        builder.Property(incident => incident.CreatedAt)
            .HasColumnType("timestamp with time zone")
            .HasColumnName("created_at");

        builder.Property(incident => incident.UpdatedAt)
            .HasColumnType("timestamp with time zone")
            .HasColumnName("updated_at");

        builder.HasIndex(incident => incident.Status);
        builder.HasIndex(incident => incident.Severity);
        builder.HasIndex(incident => incident.UpdatedAt);
    }
}

public sealed class ProductionPilotRunLedgerConfiguration : IEntityTypeConfiguration<ProductionPilotRunLedger>
{
    public void Configure(EntityTypeBuilder<ProductionPilotRunLedger> builder)
    {
        builder.ToTable("production_pilot_run_ledgers");

        builder.HasKey(ledger => ledger.Id);
        builder.Property(ledger => ledger.Id)
            .HasConversion(id => id.Value, value => new ProductionPilotRunLedgerId(value))
            .HasColumnName("id");

        builder.Property<uint>("RowVersion").IsRowVersion();

        builder.Property(ledger => ledger.RunId)
            .IsRequired()
            .HasMaxLength(200)
            .HasColumnName("run_id");
        builder.HasIndex(ledger => ledger.RunId).IsUnique();

        builder.Property(ledger => ledger.TaskId)
            .HasColumnName("task_id");

        builder.Property(ledger => ledger.SourceMode)
            .IsRequired()
            .HasMaxLength(80)
            .HasColumnName("source_mode");

        builder.Property(ledger => ledger.Boundary)
            .IsRequired()
            .HasMaxLength(120)
            .HasColumnName("boundary");

        builder.Property(ledger => ledger.TrialMode)
            .IsRequired()
            .HasMaxLength(120)
            .HasColumnName("trial_mode");

        builder.Property(ledger => ledger.PilotWindowId)
            .IsRequired()
            .HasMaxLength(160)
            .HasColumnName("pilot_window_id");

        builder.Property(ledger => ledger.IntentId)
            .HasMaxLength(200)
            .HasColumnName("intent_id");

        builder.Property(ledger => ledger.EndpointCode)
            .IsRequired()
            .HasMaxLength(120)
            .HasColumnName("endpoint_code");

        builder.Property(ledger => ledger.ArtifactIds)
            .IsRequired()
            .HasColumnType("uuid[]")
            .HasColumnName("artifact_ids");

        builder.Property(ledger => ledger.ApprovalStatus)
            .IsRequired()
            .HasMaxLength(80)
            .HasColumnName("approval_status");

        builder.Property(ledger => ledger.Status)
            .IsRequired()
            .HasMaxLength(80)
            .HasColumnName("status");

        builder.Property(ledger => ledger.DurationMs)
            .HasColumnName("duration_ms");

        builder.Property(ledger => ledger.RowCount)
            .HasColumnName("row_count");

        builder.Property(ledger => ledger.IsTruncated)
            .HasColumnName("is_truncated");

        builder.Property(ledger => ledger.QueryHash)
            .IsRequired()
            .HasMaxLength(128)
            .HasColumnName("query_hash");

        builder.Property(ledger => ledger.ResultHash)
            .IsRequired()
            .HasMaxLength(128)
            .HasColumnName("result_hash");

        builder.Property(ledger => ledger.ExecutedAt)
            .HasColumnType("timestamp with time zone")
            .HasColumnName("executed_at");

        builder.Property(ledger => ledger.CreatedAt)
            .HasColumnType("timestamp with time zone")
            .HasColumnName("created_at");

        builder.Property(ledger => ledger.UpdatedAt)
            .HasColumnType("timestamp with time zone")
            .HasColumnName("updated_at");

        builder.HasIndex(ledger => ledger.SourceMode);
        builder.HasIndex(ledger => ledger.Boundary);
        builder.HasIndex(ledger => ledger.TrialMode);
        builder.HasIndex(ledger => ledger.EndpointCode);
        builder.HasIndex(ledger => ledger.Status);
        builder.HasIndex(ledger => ledger.ExecutedAt);
    }
}

public sealed class ProductionPilotGaReadinessAssessmentConfiguration : IEntityTypeConfiguration<ProductionPilotGaReadinessAssessment>
{
    public void Configure(EntityTypeBuilder<ProductionPilotGaReadinessAssessment> builder)
    {
        builder.ToTable("production_pilot_ga_readiness_assessments");

        builder.HasKey(assessment => assessment.Id);
        builder.Property(assessment => assessment.Id)
            .HasConversion(id => id.Value, value => new ProductionPilotGaReadinessAssessmentId(value))
            .HasColumnName("id");

        builder.Property(assessment => assessment.Status)
            .IsRequired()
            .HasMaxLength(80)
            .HasColumnName("status");

        builder.Property(assessment => assessment.ChecksJson)
            .IsRequired()
            .HasColumnType("jsonb")
            .HasColumnName("checks_json");

        builder.Property(assessment => assessment.Blockers)
            .IsRequired()
            .HasColumnType("text[]")
            .HasColumnName("blockers");

        builder.Property(assessment => assessment.Warnings)
            .IsRequired()
            .HasColumnType("text[]")
            .HasColumnName("warnings");

        builder.Property(assessment => assessment.TotalRuns).HasColumnName("total_runs");
        builder.Property(assessment => assessment.SucceededRuns).HasColumnName("succeeded_runs");
        builder.Property(assessment => assessment.FailedRuns).HasColumnName("failed_runs");
        builder.Property(assessment => assessment.RejectedRuns).HasColumnName("rejected_runs");
        builder.Property(assessment => assessment.TimeoutRuns).HasColumnName("timeout_runs");
        builder.Property(assessment => assessment.TruncatedRuns).HasColumnName("truncated_runs");
        builder.Property(assessment => assessment.TotalRows).HasColumnName("total_rows");
        builder.Property(assessment => assessment.FinalArtifactCount).HasColumnName("final_artifact_count");
        builder.Property(assessment => assessment.OpenIncidentCount).HasColumnName("open_incident_count");

        builder.Property(assessment => assessment.EndpointDistributionJson)
            .IsRequired()
            .HasColumnType("jsonb")
            .HasColumnName("endpoint_distribution_json");

        builder.Property(assessment => assessment.GeneratedAt)
            .HasColumnType("timestamp with time zone")
            .HasColumnName("generated_at");

        builder.Property(assessment => assessment.CreatedAt)
            .HasColumnType("timestamp with time zone")
            .HasColumnName("created_at");

        builder.HasIndex(assessment => assessment.Status);
        builder.HasIndex(assessment => assessment.GeneratedAt);
    }
}
