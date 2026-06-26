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
