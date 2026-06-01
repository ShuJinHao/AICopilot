using AICopilot.Core.AiGateway.Aggregates.ProductionOperations;
using AICopilot.Core.AiGateway.Ids;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AICopilot.EntityFrameworkCore.Configuration.AiGateway;

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
