using AICopilot.Core.AiGateway.Aggregates.ProductionOperations;
using AICopilot.Core.AiGateway.Ids;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AICopilot.EntityFrameworkCore.Configuration.AiGateway;

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
