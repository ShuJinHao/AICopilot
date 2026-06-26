using AICopilot.Core.AiGateway.Aggregates.ProductionOperations;
using AICopilot.Core.AiGateway.Ids;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AICopilot.EntityFrameworkCore.Configuration.AiGateway;

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
