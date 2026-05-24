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

public sealed class ProductionPilotWindowConfiguration : IEntityTypeConfiguration<ProductionPilotWindow>
{
    public void Configure(EntityTypeBuilder<ProductionPilotWindow> builder)
    {
        builder.ToTable("production_pilot_windows");

        builder.HasKey(window => window.Id);
        builder.Property(window => window.Id)
            .HasConversion(id => id.Value, value => new ProductionPilotWindowId(value))
            .HasColumnName("id");

        builder.Property<uint>("RowVersion").IsRowVersion();

        builder.Property(window => window.WindowId)
            .IsRequired()
            .HasMaxLength(160)
            .HasColumnName("window_id");
        builder.HasIndex(window => window.WindowId).IsUnique();

        builder.Property(window => window.Name)
            .IsRequired()
            .HasMaxLength(200)
            .HasColumnName("name");

        builder.Property(window => window.Status)
            .IsRequired()
            .HasMaxLength(80)
            .HasColumnName("status");

        builder.Property(window => window.StartAt)
            .HasColumnType("timestamp with time zone")
            .HasColumnName("start_at");

        builder.Property(window => window.EndAt)
            .HasColumnType("timestamp with time zone")
            .HasColumnName("end_at");

        builder.Property(window => window.AllowedEndpointCodes)
            .IsRequired()
            .HasColumnType("text[]")
            .HasColumnName("allowed_endpoint_codes");

        builder.Property(window => window.MaxTimeRangeDays).HasColumnName("max_time_range_days");
        builder.Property(window => window.MaxRows).HasColumnName("max_rows");
        builder.Property(window => window.TimeoutMs).HasColumnName("timeout_ms");

        builder.Property(window => window.OwnerDepartment)
            .IsRequired()
            .HasMaxLength(120)
            .HasColumnName("owner_department");

        builder.Property(window => window.ApprovalPolicy)
            .IsRequired()
            .HasMaxLength(120)
            .HasColumnName("approval_policy");

        builder.Property(window => window.RollbackPolicy)
            .IsRequired()
            .HasMaxLength(160)
            .HasColumnName("rollback_policy");

        builder.Property(window => window.CreatedAt)
            .HasColumnType("timestamp with time zone")
            .HasColumnName("created_at");

        builder.Property(window => window.UpdatedAt)
            .HasColumnType("timestamp with time zone")
            .HasColumnName("updated_at");

        builder.HasIndex(window => window.Status);
        builder.HasIndex(window => window.StartAt);
        builder.HasIndex(window => window.EndAt);
    }
}

public sealed class ProductionPilotRunConfiguration : IEntityTypeConfiguration<ProductionPilotRun>
{
    public void Configure(EntityTypeBuilder<ProductionPilotRun> builder)
    {
        builder.ToTable("production_pilot_runs");

        builder.HasKey(run => run.Id);
        builder.Property(run => run.Id)
            .HasConversion(id => id.Value, value => new ProductionPilotRunId(value))
            .HasColumnName("id");

        builder.Property<uint>("RowVersion").IsRowVersion();

        builder.Property(run => run.RunId)
            .IsRequired()
            .HasMaxLength(200)
            .HasColumnName("run_id");
        builder.HasIndex(run => run.RunId).IsUnique();

        ConfigureRunCommon(builder);

        builder.Property(run => run.ScenarioId)
            .IsRequired()
            .HasMaxLength(160)
            .HasColumnName("scenario_id");

        builder.Property(run => run.ScenarioTitle)
            .IsRequired()
            .HasMaxLength(200)
            .HasColumnName("scenario_title");

        builder.HasIndex(run => run.ScenarioId);
    }

    private static void ConfigureRunCommon(EntityTypeBuilder<ProductionPilotRun> builder)
    {
        builder.Property(run => run.Status).IsRequired().HasMaxLength(80).HasColumnName("status");
        builder.Property(run => run.EndpointCode).IsRequired().HasMaxLength(120).HasColumnName("endpoint_code");
        builder.Property(run => run.SourceType).IsRequired().HasMaxLength(80).HasColumnName("source_type");
        builder.Property(run => run.SourceMode).IsRequired().HasMaxLength(80).HasColumnName("source_mode");
        builder.Property(run => run.IsProductionData).HasColumnName("is_production_data");
        builder.Property(run => run.IsSandbox).HasColumnName("is_sandbox");
        builder.Property(run => run.IsSimulation).HasColumnName("is_simulation");
        builder.Property(run => run.SourceLabel).IsRequired().HasMaxLength(200).HasColumnName("source_label");
        builder.Property(run => run.Boundary).IsRequired().HasMaxLength(120).HasColumnName("boundary");
        builder.Property(run => run.PilotWindowId).IsRequired().HasMaxLength(160).HasColumnName("pilot_window_id");
        builder.Property(run => run.QueryHash).IsRequired().HasMaxLength(128).HasColumnName("query_hash");
        builder.Property(run => run.ResultHash).IsRequired().HasMaxLength(128).HasColumnName("result_hash");
        builder.Property(run => run.RowCount).HasColumnName("row_count");
        builder.Property(run => run.IsTruncated).HasColumnName("is_truncated");
        builder.Property(run => run.ExecutedAt).HasColumnType("timestamp with time zone").HasColumnName("executed_at");
        builder.Property(run => run.DurationMs).HasColumnName("duration_ms");
        builder.Property(run => run.ApprovalStatus).IsRequired().HasMaxLength(80).HasColumnName("approval_status");
        builder.Property(run => run.ArtifactTypes).IsRequired().HasColumnType("text[]").HasColumnName("artifact_types");
        builder.Property(run => run.CreatedAt).HasColumnType("timestamp with time zone").HasColumnName("created_at");
        builder.Property(run => run.UpdatedAt).HasColumnType("timestamp with time zone").HasColumnName("updated_at");

        builder.HasIndex(run => run.Status);
        builder.HasIndex(run => run.EndpointCode);
        builder.HasIndex(run => run.SourceMode);
        builder.HasIndex(run => run.Boundary);
        builder.HasIndex(run => run.ExecutedAt);
    }
}

public sealed class ProductionControlledPilotIntentConfiguration : IEntityTypeConfiguration<ProductionControlledPilotIntent>
{
    public void Configure(EntityTypeBuilder<ProductionControlledPilotIntent> builder)
    {
        builder.ToTable("production_controlled_pilot_intents");

        builder.HasKey(intent => intent.Id);
        builder.Property(intent => intent.Id)
            .HasConversion(id => id.Value, value => new ProductionControlledPilotIntentId(value))
            .HasColumnName("id");

        builder.Property<uint>("RowVersion").IsRowVersion();

        builder.Property(intent => intent.IntentId)
            .IsRequired()
            .HasMaxLength(200)
            .HasColumnName("intent_id");
        builder.HasIndex(intent => intent.IntentId).IsUnique();

        builder.Property(intent => intent.GoalHash)
            .IsRequired()
            .HasMaxLength(128)
            .HasColumnName("goal_hash");

        builder.Property(intent => intent.EndpointCodes)
            .IsRequired()
            .HasColumnType("text[]")
            .HasColumnName("endpoint_codes");

        builder.Property(intent => intent.TimeRangeFrom)
            .HasColumnType("timestamp with time zone")
            .HasColumnName("time_range_from");

        builder.Property(intent => intent.TimeRangeTo)
            .HasColumnType("timestamp with time zone")
            .HasColumnName("time_range_to");

        builder.Property(intent => intent.MaxRows).HasColumnName("max_rows");

        builder.Property(intent => intent.ArtifactTypes)
            .IsRequired()
            .HasColumnType("text[]")
            .HasColumnName("artifact_types");

        builder.Property(intent => intent.AnalysisType)
            .IsRequired()
            .HasMaxLength(120)
            .HasColumnName("analysis_type");

        builder.Property(intent => intent.Warnings)
            .IsRequired()
            .HasColumnType("text[]")
            .HasColumnName("warnings");

        builder.Property(intent => intent.RejectedReasons)
            .IsRequired()
            .HasColumnType("text[]")
            .HasColumnName("rejected_reasons");

        builder.Property(intent => intent.RequiresToolApproval).HasColumnName("requires_tool_approval");
        builder.Property(intent => intent.RequiresFinalApproval).HasColumnName("requires_final_approval");
        builder.Property(intent => intent.CreatedAt).HasColumnType("timestamp with time zone").HasColumnName("created_at");
        builder.Property(intent => intent.UpdatedAt).HasColumnType("timestamp with time zone").HasColumnName("updated_at");

        builder.HasIndex(intent => intent.GoalHash);
        builder.HasIndex(intent => intent.CreatedAt);
    }
}

public sealed class ProductionControlledPilotRunConfiguration : IEntityTypeConfiguration<ProductionControlledPilotRun>
{
    public void Configure(EntityTypeBuilder<ProductionControlledPilotRun> builder)
    {
        builder.ToTable("production_controlled_pilot_runs");

        builder.HasKey(run => run.Id);
        builder.Property(run => run.Id)
            .HasConversion(id => id.Value, value => new ProductionControlledPilotRunId(value))
            .HasColumnName("id");

        builder.Property<uint>("RowVersion").IsRowVersion();

        builder.Property(run => run.RunId)
            .IsRequired()
            .HasMaxLength(200)
            .HasColumnName("run_id");
        builder.HasIndex(run => run.RunId).IsUnique();

        builder.Property(run => run.IntentId).IsRequired().HasMaxLength(200).HasColumnName("intent_id");
        builder.Property(run => run.AnalysisType).IsRequired().HasMaxLength(120).HasColumnName("analysis_type");
        builder.Property(run => run.Status).IsRequired().HasMaxLength(80).HasColumnName("status");
        builder.Property(run => run.EndpointCode).IsRequired().HasMaxLength(120).HasColumnName("endpoint_code");
        builder.Property(run => run.SourceType).IsRequired().HasMaxLength(80).HasColumnName("source_type");
        builder.Property(run => run.SourceMode).IsRequired().HasMaxLength(80).HasColumnName("source_mode");
        builder.Property(run => run.IsProductionData).HasColumnName("is_production_data");
        builder.Property(run => run.IsSandbox).HasColumnName("is_sandbox");
        builder.Property(run => run.IsSimulation).HasColumnName("is_simulation");
        builder.Property(run => run.SourceLabel).IsRequired().HasMaxLength(200).HasColumnName("source_label");
        builder.Property(run => run.Boundary).IsRequired().HasMaxLength(120).HasColumnName("boundary");
        builder.Property(run => run.PilotWindowId).IsRequired().HasMaxLength(160).HasColumnName("pilot_window_id");
        builder.Property(run => run.QueryHash).IsRequired().HasMaxLength(128).HasColumnName("query_hash");
        builder.Property(run => run.ResultHash).IsRequired().HasMaxLength(128).HasColumnName("result_hash");
        builder.Property(run => run.RowCount).HasColumnName("row_count");
        builder.Property(run => run.IsTruncated).HasColumnName("is_truncated");
        builder.Property(run => run.ExecutedAt).HasColumnType("timestamp with time zone").HasColumnName("executed_at");
        builder.Property(run => run.DurationMs).HasColumnName("duration_ms");
        builder.Property(run => run.ApprovalStatus).IsRequired().HasMaxLength(80).HasColumnName("approval_status");
        builder.Property(run => run.ArtifactTypes).IsRequired().HasColumnType("text[]").HasColumnName("artifact_types");
        builder.Property(run => run.CreatedAt).HasColumnType("timestamp with time zone").HasColumnName("created_at");
        builder.Property(run => run.UpdatedAt).HasColumnType("timestamp with time zone").HasColumnName("updated_at");

        builder.HasIndex(run => run.IntentId);
        builder.HasIndex(run => run.Status);
        builder.HasIndex(run => run.EndpointCode);
        builder.HasIndex(run => run.SourceMode);
        builder.HasIndex(run => run.Boundary);
        builder.HasIndex(run => run.ExecutedAt);
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
