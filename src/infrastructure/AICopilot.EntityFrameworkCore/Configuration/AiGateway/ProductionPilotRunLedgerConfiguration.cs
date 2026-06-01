using AICopilot.Core.AiGateway.Aggregates.ProductionOperations;
using AICopilot.Core.AiGateway.Ids;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AICopilot.EntityFrameworkCore.Configuration.AiGateway;

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
